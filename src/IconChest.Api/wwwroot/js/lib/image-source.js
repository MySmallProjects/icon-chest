/**
 * Paged image data source with sparse page caching, LRU eviction,
 * request de-duplication, aborting stale pages, and ordered IDs caching.
 */

export class ImageSource {
  constructor(options = {}) {
    this.fetchFn = options.fetchFn || (async () => ({ total: 0, items: [] }));
    this.fetchIdsFn = options.fetchIdsFn || (async () => []);
    this.pageSize = options.pageSize || 200;
    this.maxCachedPages = options.maxCachedPages || 20;
    this.maxConcurrency = options.maxConcurrency || 4;

    this.total = 0;
    this._pages = new Map(); // pageIndex -> items
    this._lruOrder = []; // pageIndex in order of use (most recent at end)
    this._inFlight = new Map(); // pageIndex -> { promise, abortController }
    this._visiblePages = new Set();
    this._activeCount = 0;
    this._queue = []; // pending fetch requests { pageIndex, resolve, reject }
    this._orderedIds = null; // cached array of all IDs in current query order
    this._orderedIdsPromise = null;
  }

  getPagesNeeded(startIndex, endIndex) {
    if (startIndex < 0 || endIndex < startIndex) return [];
    const startPage = Math.floor(startIndex / this.pageSize);
    const endPage = Math.floor(endIndex / this.pageSize);
    const pages = [];
    for (let p = startPage; p <= endPage; p++) {
      pages.push(p);
    }
    return pages;
  }

  setVisibleRange(startIndex, endIndex) {
    const needed = this.getPagesNeeded(startIndex, endIndex);
    this._visiblePages = new Set(needed);

    // Cancel in-flight requests that are far away from visible range
    for (const [pageIdx, req] of this._inFlight.entries()) {
      if (!this._visiblePages.has(pageIdx) && Math.abs(pageIdx - (needed[0] || 0)) > 2) {
        req.abortController.abort();
        this._inFlight.delete(pageIdx);
      }
    }
  }

  getItem(index) {
    const pageIndex = Math.floor(index / this.pageSize);
    const offsetInPage = index % this.pageSize;
    const page = this._pages.get(pageIndex);
    if (!page) return null;
    this._touchPage(pageIndex);
    return page[offsetInPage] || null;
  }

  hasPage(pageIndex) {
    return this._pages.has(pageIndex);
  }

  getPage(pageIndex) {
    const page = this._pages.get(pageIndex);
    if (page) {
      this._touchPage(pageIndex);
      return page;
    }
    return null;
  }

  _touchPage(pageIndex) {
    const idx = this._lruOrder.indexOf(pageIndex);
    if (idx !== -1) {
      this._lruOrder.splice(idx, 1);
    }
    this._lruOrder.push(pageIndex);
  }

  async loadPage(pageIndex) {
    if (this._pages.has(pageIndex)) {
      this._touchPage(pageIndex);
      return this._pages.get(pageIndex);
    }

    if (this._inFlight.has(pageIndex)) {
      return this._inFlight.get(pageIndex).promise;
    }

    const abortController = new AbortController();
    const promise = this._enqueuePageRequest(pageIndex, abortController.signal);
    this._inFlight.set(pageIndex, { promise, abortController });

    try {
      const items = await promise;
      this._pages.set(pageIndex, items);
      this._touchPage(pageIndex);
      this._evictLruPages();
      return items;
    } finally {
      this._inFlight.delete(pageIndex);
    }
  }

  async ensureRange(startIndex, endIndex, prefetchCount = 1) {
    const pages = this.getPagesNeeded(startIndex, endIndex);
    this.setVisibleRange(startIndex, endIndex);

    // Add prefetch pages
    if (pages.length > 0) {
      const minPage = pages[0];
      const maxPage = pages[pages.length - 1];
      for (let i = 1; i <= prefetchCount; i++) {
        if (minPage - i >= 0) pages.unshift(minPage - i);
        const nextP = maxPage + i;
        if (nextP * this.pageSize < this.total) pages.push(nextP);
      }
    }

    const promises = pages.map(p => this.loadPage(p).catch(() => []));
    await Promise.all(promises);
  }

  _enqueuePageRequest(pageIndex, signal) {
    return new Promise((resolve, reject) => {
      this._queue.push({ pageIndex, signal, resolve, reject });
      this._processQueue();
    });
  }

  async _processQueue() {
    if (this._activeCount >= this.maxConcurrency || this._queue.length === 0) {
      return;
    }

    const item = this._queue.shift();
    if (item.signal && item.signal.aborted) {
      item.reject(new DOMException('Aborted', 'AbortError'));
      this._processQueue();
      return;
    }

    this._activeCount++;
    try {
      const offset = item.pageIndex * this.pageSize;
      const res = await this.fetchFn(offset, this.pageSize, item.signal);
      if (typeof res.total === 'number') {
        this.total = res.total;
      }
      item.resolve(res.items || []);
    } catch (err) {
      item.reject(err);
    } finally {
      this._activeCount--;
      this._processQueue();
    }
  }

  _evictLruPages() {
    while (this._pages.size > this.maxCachedPages && this._lruOrder.length > 0) {
      // Find candidate from beginning of LRU that is not currently visible
      let candidateIdx = -1;
      for (let i = 0; i < this._lruOrder.length; i++) {
        const p = this._lruOrder[i];
        if (!this._visiblePages.has(p)) {
          candidateIdx = i;
          break;
        }
      }

      if (candidateIdx === -1) break; // All cached pages are currently visible

      const evicted = this._lruOrder.splice(candidateIdx, 1)[0];
      this._pages.delete(evicted);
    }
  }

  invalidate() {
    // Abort all in-flight requests
    for (const req of this._inFlight.values()) {
      req.abortController.abort();
    }
    this._inFlight.clear();
    this._queue.length = 0;
    this._pages.clear();
    this._lruOrder.length = 0;
    this._orderedIds = null;
    this._orderedIdsPromise = null;
  }

  async softRefresh() {
    // Invalidate cached pages and ordered IDs, but keep visible pages refetching immediately
    this._orderedIds = null;
    this._orderedIdsPromise = null;

    const visible = Array.from(this._visiblePages);
    this._pages.clear();
    this._lruOrder.length = 0;

    if (visible.length === 0) {
      visible.push(0);
    }

    const promises = visible.map(p => this.loadPage(p).catch(() => []));
    await Promise.all(promises);
  }

  async getOrderedIds() {
    if (this._orderedIds) {
      return this._orderedIds;
    }
    if (this._orderedIdsPromise) {
      return this._orderedIdsPromise;
    }

    this._orderedIdsPromise = (async () => {
      try {
        const ids = await this.fetchIdsFn();
        this._orderedIds = ids;
        return ids;
      } finally {
        this._orderedIdsPromise = null;
      }
    })();

    return this._orderedIdsPromise;
  }
}

