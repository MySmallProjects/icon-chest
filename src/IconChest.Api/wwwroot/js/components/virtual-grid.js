import { el, clear, createSvg } from '../lib/dom.js';
import {
  calculateColumnCount,
  calculateRowCount,
  calculateTotalHeight,
  getCellRect,
  getVisibleRange,
  getAnchorScrollTop,
  getMarqueeHits
} from '../lib/grid-geometry.js';
import { navigateIndex } from '../lib/keyboard-nav.js';
import { pickThumbnailSize, ThumbnailManager } from '../lib/thumb-loader.js';

const CHECK_ICON = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>';
const HEART_ICON = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>';

export class VirtualGrid {
  constructor(options = {}) {
    this.containerEl = options.containerEl || el('div', { className: 'virtual-grid-container' });
    this.imageSource = options.imageSource;
    this.selectionModel = options.selectionModel;
    this.cellSize = options.cellSize || 160;
    this.gap = options.gap || 8;
    this.bufferRows = options.bufferRows || 3;
    this.backgroundMode = options.backgroundMode || 'checker';
    this.captionMode = options.captionMode || 'filename';

    this.onOpenPreview = options.onOpenPreview || (() => {});
    this.onToggleFavourite = options.onToggleFavourite || (() => {});
    this.onSelectionChange = options.onSelectionChange || (() => {});
    this.onToggleDetails = options.onToggleDetails || (() => {});

    this.thumbnailManager = new ThumbnailManager();
    this.columnCount = 1;
    this.totalItems = 0;
    this.focusedIndex = -1;

    // Recycled cell pool: index -> DOMElement
    this.cellPool = new Map();
    this.freePool = []; // Available unassigned DOM elements
    this._failedThumbnails = new Set(); // Set of `${imageId}_${size}`

    // Marquee state
    this.isMarqueeDragging = false;
    this.marqueeStart = { x: 0, y: 0 };
    this.marqueeModifiers = { ctrl: false, shift: false };
    this.autoScrollTimer = null;

    this._buildDom();
    this._bindEvents();
  }

  _getCaptionHeight() {
    if (this.captionMode === 'none') return 0;
    if (this.captionMode === 'filename_and_dimensions') return 36;
    return 22;
  }

  get cellHeight() {
    return this.cellSize + this._getCaptionHeight();
  }

  _buildDom() {
    clear(this.containerEl);

    this.scrollEl = el('div', {
      className: 'virtual-grid-scroll',
      tabIndex: '0',
      role: 'grid',
      'aria-label': 'Images grid'
    });

    this.spacerEl = el('div', { className: 'virtual-grid-spacer' });
    this.poolEl = el('div', { className: 'virtual-grid-pool' });

    this.marqueeEl = el('div', {
      className: 'grid-marquee-box',
      style: { display: 'none' }
    });

    this.emptyStateEl = el('div', {
      className: 'grid-empty-state',
      style: { display: 'none' }
    }, [
      el('p', { className: 'grid-empty-title' }, 'No images found'),
      el('p', { className: 'grid-empty-subtitle' }, 'Try clearing filters or scanning a folder in Settings.')
    ]);

    this.spacerEl.appendChild(this.poolEl);
    this.spacerEl.appendChild(this.marqueeEl);
    this.scrollEl.appendChild(this.spacerEl);
    this.scrollEl.appendChild(this.emptyStateEl);
    this.containerEl.appendChild(this.scrollEl);
  }

  _bindEvents() {
    // Scroll listener with RAF throttling
    let scrollRaf = null;
    this.scrollEl.addEventListener('scroll', () => {
      if (scrollRaf) return;
      scrollRaf = requestAnimationFrame(() => {
        scrollRaf = null;
        this.renderVisible();
      });
    }, { passive: true });

    // ResizeObserver
    this.resizeObserver = new ResizeObserver(() => {
      this.recalculateLayout();
      this.renderVisible();
    });
    this.resizeObserver.observe(this.scrollEl);

    // Selection model listener
    if (this.selectionModel) {
      this.selectionModel.onChange(() => {
        this._updateSelectionStyles();
        this.onSelectionChange(this.selectionModel.getSelectedList());
      });
    }

    // Keyboard navigation
    this.scrollEl.addEventListener('keydown', (e) => this._handleKeyDown(e));

    // Pointer events for marquee and click
    this.scrollEl.addEventListener('pointerdown', (e) => this._handlePointerDown(e));
  }

  recalculateLayout() {
    const containerWidth = Math.max(0, this.scrollEl.clientWidth - 16);
    this.totalItems = this.imageSource ? this.imageSource.total : 0;

    if (containerWidth <= 0) {
      this.columnCount = 1;
      return;
    }

    this.columnCount = calculateColumnCount(containerWidth, this.cellSize, this.gap);
    const totalHeight = calculateTotalHeight(this.totalItems, this.columnCount, this.cellSize, this.cellHeight, this.gap);

    this.spacerEl.style.height = `${Math.max(0, totalHeight + 16)}px`;

    if (this.totalItems === 0) {
      this.emptyStateEl.style.display = 'flex';
      this.poolEl.style.display = 'none';
    } else {
      this.emptyStateEl.style.display = 'none';
      this.poolEl.style.display = 'block';
    }
  }

  renderVisible(force = false) {
    if (this.totalItems === 0) {
      this._recycleAll();
      return;
    }

    const scrollTop = this.scrollEl.scrollTop;
    const viewportHeight = this.scrollEl.clientHeight || 600;

    const { startIndex, endIndex } = getVisibleRange(
      scrollTop,
      viewportHeight,
      this.totalItems,
      this.columnCount,
      this.cellHeight,
      this.gap,
      this.bufferRows
    );

    if (startIndex > endIndex) {
      this._recycleAll();
      return;
    }

    // Trigger data fetching for visible range
    if (this.imageSource) {
      this.imageSource.ensureRange(startIndex, endIndex).then(() => {
        // Refresh cells that were rendered in placeholder state
        this._updateCellContents(startIndex, endIndex);
      }).catch(() => {});
    }

    // Recycle cells that are out of range
    for (const [index, cell] of this.cellPool.entries()) {
      if (force || index < startIndex || index > endIndex) {
        this._recycleCell(index, cell);
      }
    }

    // Create or update cells in visible range
    for (let i = startIndex; i <= endIndex; i++) {
      let cell = this.cellPool.get(i);
      const rect = getCellRect(i, this.columnCount, this.cellSize, this.cellHeight, this.gap);

      if (!cell) {
        cell = this._acquireCell();
        this.cellPool.set(i, cell);
        this.poolEl.appendChild(cell);
      }

      // Update position
      cell.style.transform = `translate3d(${rect.x + 8}px, ${rect.y + 8}px, 0)`;
      cell.style.width = `${this.cellSize}px`;
      cell.style.height = `${this.cellHeight}px`;
      cell.dataset.index = String(i);

      this._populateCell(cell, i);
    }
  }

  _acquireCell() {
    if (this.freePool.length > 0) {
      const cell = this.freePool.pop();
      cell.style.display = 'flex';
      return cell;
    }
    return this._createCellElement();
  }

  _recycleCell(index, cell) {
    this.cellPool.delete(index);
    cell.style.display = 'none';
    this.freePool.push(cell);
  }

  _recycleAll() {
    for (const [index, cell] of this.cellPool.entries()) {
      this._recycleCell(index, cell);
    }
  }

  _createCellElement() {
    const cell = el('div', {
      className: 'grid-cell',
      role: 'gridcell',
      tabIndex: '-1'
    });

    // Image wrapper
    const imgWrap = el('div', { className: `grid-cell-img-wrap bg-transparency-${this.backgroundMode}` });
    const img = el('img', { className: 'grid-cell-img', alt: 'Image' });
    const placeholder = el('div', { className: 'grid-cell-placeholder' });

    // Buttons
    const checkbox = el('button', {
      type: 'button',
      className: 'grid-cell-checkbox',
      'aria-label': 'Select item',
      tabIndex: '-1'
    }, [createSvg(CHECK_ICON)]);

    const heart = el('button', {
      type: 'button',
      className: 'grid-cell-heart',
      'aria-label': 'Toggle favourite',
      tabIndex: '-1'
    }, [createSvg(HEART_ICON)]);

    // Badges
    const badgeHidden = el('span', { className: 'grid-badge grid-badge-hidden', style: { display: 'none' } }, 'Hidden');
    const badgeError = el('span', { className: 'grid-badge grid-badge-error', style: { display: 'none' } }, 'Retry');

    imgWrap.appendChild(placeholder);
    imgWrap.appendChild(img);
    imgWrap.appendChild(checkbox);
    imgWrap.appendChild(heart);
    imgWrap.appendChild(badgeHidden);
    imgWrap.appendChild(badgeError);

    // Caption
    const caption = el('div', { className: 'grid-cell-caption' });

    cell.appendChild(imgWrap);
    cell.appendChild(caption);

    // Click handlers on buttons
    checkbox.addEventListener('click', (e) => {
      e.stopPropagation();
      const idx = parseInt(cell.dataset.index, 10);
      const item = this.imageSource?.getItem(idx);
      if (item && this.selectionModel) {
        this.selectionModel.toggle(item.id);
      }
    });

    heart.addEventListener('click', (e) => {
      e.stopPropagation();
      const idx = parseInt(cell.dataset.index, 10);
      const item = this.imageSource?.getItem(idx);
      if (item) {
        const curFav = Boolean(item.isFavourite ?? item.is_favourite);
        const nextFav = !curFav;
        item.isFavourite = nextFav;
        item.is_favourite = nextFav;
        heart.setAttribute('aria-pressed', String(nextFav));
        heart.classList.toggle('active', nextFav);
        this.onToggleFavourite(item.id, nextFav);
      }
    });

    // Cell click and double-click
    cell.addEventListener('click', async (e) => {
      const idx = parseInt(cell.dataset.index, 10);
      const item = this.imageSource?.getItem(idx);
      if (!item) return;

      this.focusedIndex = idx;
      if (this.selectionModel) {
        const orderedIds = await this.imageSource.getOrderedIds();
        this.selectionModel.handleClick(item.id, e.ctrlKey || e.metaKey, e.shiftKey, orderedIds);
      }
    });

    cell.addEventListener('dblclick', () => {
      const idx = parseInt(cell.dataset.index, 10);
      const item = this.imageSource?.getItem(idx);
      if (item) {
        this.onOpenPreview(idx, item);
      }
    });

    return cell;
  }

  _populateCell(cell, index) {
    const item = this.imageSource?.getItem(index);
    const imgWrap = cell.querySelector('.grid-cell-img-wrap');
    const img = cell.querySelector('.grid-cell-img');
    const placeholder = cell.querySelector('.grid-cell-placeholder');
    const heart = cell.querySelector('.grid-cell-heart');
    const badgeHidden = cell.querySelector('.grid-badge-hidden');
    const badgeError = cell.querySelector('.grid-badge-error');
    const caption = cell.querySelector('.grid-cell-caption');

    imgWrap.className = `grid-cell-img-wrap bg-transparency-${this.backgroundMode}`;

    if (!item) {
      // Data still loading
      img.style.display = 'none';
      placeholder.style.display = 'block';
      cell.dataset.id = '';
      cell.classList.remove('is-selected', 'is-focus');
      heart.setAttribute('aria-pressed', 'false');
      heart.classList.remove('active');
      badgeHidden.style.display = 'none';
      badgeError.style.display = 'none';
      caption.textContent = '';
      return;
    }

    cell.dataset.id = String(item.id);

    // Selection & focus state
    const isSelected = this.selectionModel ? this.selectionModel.isSelected(item.id) : false;
    cell.classList.toggle('is-selected', isSelected);
    cell.classList.toggle('is-focus', this.focusedIndex === index);

    // Favourite state
    const isFav = Boolean(item.isFavourite ?? item.is_favourite);
    heart.setAttribute('aria-pressed', String(isFav));
    heart.classList.toggle('active', isFav);

    // Hidden badge
    badgeHidden.style.display = (item.isHidden ?? item.is_hidden) ? 'inline-block' : 'none';

    // Caption
    this._renderCaption(caption, item);

    // Thumbnail loading
    const pickedSize = pickThumbnailSize(this.cellSize, window.devicePixelRatio || 1);
    const thumbUrl = `/api/images/${item.id}/thumbnail?size=${pickedSize}`;
    const cacheKey = `${item.id}_${pickedSize}`;

    if (this._failedThumbnails.has(cacheKey)) {
      img.style.display = 'none';
      placeholder.style.display = 'none';
      badgeError.style.display = 'inline-block';
      return;
    }

    badgeError.style.display = 'none';
    placeholder.style.display = 'none';
    img.style.display = 'block';

    if (img.dataset.src !== thumbUrl) {
      img.dataset.src = thumbUrl;
      img.src = thumbUrl;

      img.onload = () => {
        img.style.opacity = '1';
        this.thumbnailManager.cancel(cacheKey);
      };

      img.onerror = () => {
        // Thumbnail may be generating (202) or retryable
        this._handleThumbnailRetry(cacheKey, thumbUrl, img, badgeError);
      };
    }
  }

  _handleThumbnailRetry(cacheKey, thumbUrl, img, badgeError) {
    let attempt = 0;
    const retry = () => {
      attempt++;
      const scheduled = this.thumbnailManager.scheduleRetry(cacheKey, attempt, () => {
        const testImg = new Image();
        testImg.onload = () => {
          img.src = `${thumbUrl}&_t=${Date.now()}`;
          img.style.display = 'block';
          badgeError.style.display = 'none';
        };
        testImg.onerror = () => {
          retry();
        };
        testImg.src = `${thumbUrl}&_t=${Date.now()}`;
      });

      if (!scheduled) {
        this._failedThumbnails.add(cacheKey);
        img.style.display = 'none';
        badgeError.style.display = 'inline-block';
      }
    };

    retry();
  }

  _renderCaption(captionEl, item) {
    if (this.captionMode === 'none') {
      captionEl.textContent = '';
      captionEl.style.display = 'none';
      return;
    }

    captionEl.style.display = 'flex';
    clear(captionEl);

    if (this.captionMode === 'filename') {
      captionEl.appendChild(el('span', { className: 'caption-text truncate', title: item.name }, item.name));
    } else if (this.captionMode === 'dimensions') {
      const dim = item.width && item.height ? `${item.width} × ${item.height}` : '-';
      captionEl.appendChild(el('span', { className: 'caption-text' }, dim));
    } else if (this.captionMode === 'filesize') {
      captionEl.appendChild(el('span', { className: 'caption-text' }, this._formatBytes(item.file_size_bytes)));
    } else if (this.captionMode === 'filename_and_dimensions') {
      const dim = item.width && item.height ? `${item.width} × ${item.height}` : '';
      captionEl.appendChild(el('span', { className: 'caption-text truncate', title: item.name }, item.name));
      if (dim) {
        captionEl.appendChild(el('span', { className: 'caption-subtext' }, dim));
      }
    }
  }

  _formatBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(1)} ${sizes[i]}`;
  }

  _updateCellContents(startIndex, endIndex) {
    for (let i = startIndex; i <= endIndex; i++) {
      const cell = this.cellPool.get(i);
      if (cell) {
        this._populateCell(cell, i);
      }
    }
  }

  _updateSelectionStyles() {
    for (const [index, cell] of this.cellPool.entries()) {
      const id = cell.dataset.id ? parseInt(cell.dataset.id, 10) : null;
      if (id && this.selectionModel) {
        const isSelected = this.selectionModel.isSelected(id);
        cell.classList.toggle('is-selected', isSelected);
      }
      cell.classList.toggle('is-focus', this.focusedIndex === index);
    }
  }

  // Marquee Selection Logic
  _handlePointerDown(e) {
    // If clicking on an existing interactive cell button, do not start marquee
    if (e.target.closest('.grid-cell-checkbox') || e.target.closest('.grid-cell-heart')) {
      return;
    }

    const cellTarget = e.target.closest('.grid-cell');
    if (cellTarget && e.button === 0) {
      // Normal click inside cell
      return;
    }

    if (e.button !== 0) return; // Only primary button

    // Marquee start
    this.isMarqueeDragging = true;
    this.scrollEl.setPointerCapture(e.pointerId);

    const rect = this.scrollEl.getBoundingClientRect();
    const startX = e.clientX - rect.left + this.scrollEl.scrollLeft;
    const startY = e.clientY - rect.top + this.scrollEl.scrollTop;

    this.marqueeStart = { x: startX, y: startY };
    this.marqueeModifiers = { ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey };

    this.marqueeEl.style.left = `${startX}px`;
    this.marqueeEl.style.top = `${startY}px`;
    this.marqueeEl.style.width = '0px';
    this.marqueeEl.style.height = '0px';
    this.marqueeEl.style.display = 'block';

    const onPointerMove = (moveEvent) => {
      if (!this.isMarqueeDragging) return;
      this._updateMarquee(moveEvent, rect);
    };

    const onPointerUp = async (upEvent) => {
      this.isMarqueeDragging = false;
      this.marqueeEl.style.display = 'none';
      if (this.autoScrollTimer) {
        clearInterval(this.autoScrollTimer);
        this.autoScrollTimer = null;
      }

      this.scrollEl.removeEventListener('pointermove', onPointerMove);
      this.scrollEl.removeEventListener('pointerup', onPointerUp);
      this.scrollEl.removeEventListener('pointercancel', onPointerUp);

      try {
        this.scrollEl.releasePointerCapture(upEvent.pointerId);
      } catch {}
    };

    this.scrollEl.addEventListener('pointermove', onPointerMove);
    this.scrollEl.addEventListener('pointerup', onPointerUp);
    this.scrollEl.addEventListener('pointercancel', onPointerUp);
  }

  async _updateMarquee(e, rect) {
    const currentX = e.clientX - rect.left + this.scrollEl.scrollLeft;
    const currentY = e.clientY - rect.top + this.scrollEl.scrollTop;

    const left = Math.min(this.marqueeStart.x, currentX);
    const top = Math.min(this.marqueeStart.y, currentY);
    const width = Math.abs(currentX - this.marqueeStart.x);
    const height = Math.abs(currentY - this.marqueeStart.y);

    this.marqueeEl.style.left = `${left}px`;
    this.marqueeEl.style.top = `${top}px`;
    this.marqueeEl.style.width = `${width}px`;
    this.marqueeEl.style.height = `${height}px`;

    // Edge auto-scrolling
    const viewportY = e.clientY - rect.top;
    if (viewportY < 40) {
      this.scrollEl.scrollTop -= 15;
    } else if (viewportY > rect.height - 40) {
      this.scrollEl.scrollTop += 15;
    }

    // Hit test with pure arithmetic
    const marqueeRect = { x: left - 8, y: top - 8, width, height };
    const hits = getMarqueeHits(
      marqueeRect,
      this.totalItems,
      this.columnCount,
      this.cellSize,
      this.cellHeight,
      this.gap
    );

    if (this.selectionModel && this.imageSource) {
      const orderedIds = await this.imageSource.getOrderedIds();
      const hitIds = hits.map(idx => orderedIds[idx]).filter(Boolean);

      if (this.marqueeModifiers.ctrl || this.marqueeModifiers.shift) {
        this.selectionModel.addRange(hitIds);
      } else {
        this.selectionModel.replaceSelection(hitIds);
      }
    }
  }

  // Keyboard navigation
  async _handleKeyDown(e) {
    if (this.totalItems === 0) return;

    if (e.key === 'a' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      if (this.selectionModel && this.imageSource) {
        const orderedIds = await this.imageSource.getOrderedIds();
        this.selectionModel.selectAll(orderedIds);
      }
      return;
    }

    if (e.key === 'Escape') {
      e.preventDefault();
      if (this.selectionModel) {
        this.selectionModel.clear();
      }
      return;
    }

    if ((e.key === 'i' || e.key === 'I') && !e.ctrlKey && !e.metaKey && !e.altKey) {
      e.preventDefault();
      this.onToggleDetails();
      return;
    }

    if (e.key === 'Enter') {
      e.preventDefault();
      if (this.focusedIndex >= 0) {
        const item = this.imageSource?.getItem(this.focusedIndex);
        if (item) this.onOpenPreview(this.focusedIndex, item);
      }
      return;
    }

    if (e.key === ' ') {
      e.preventDefault();
      if (this.focusedIndex >= 0) {
        const item = this.imageSource?.getItem(this.focusedIndex);
        if (item && this.selectionModel) {
          this.selectionModel.toggle(item.id);
        }
      }
      return;
    }

    // Arrow navigation
    const viewportHeight = this.scrollEl.clientHeight || 600;
    const rowsPerPage = Math.max(1, Math.floor(viewportHeight / (this.cellHeight + this.gap)));

    const nextIndex = navigateIndex(
      this.focusedIndex,
      e.key,
      this.totalItems,
      this.columnCount,
      rowsPerPage
    );

    if (nextIndex !== this.focusedIndex) {
      e.preventDefault();
      this.focusedIndex = nextIndex;

      // Scroll into view
      const rect = getCellRect(nextIndex, this.columnCount, this.cellSize, this.cellHeight, this.gap);
      if (rect.y < this.scrollEl.scrollTop) {
        this.scrollEl.scrollTop = rect.y;
      } else if (rect.y + this.cellHeight > this.scrollEl.scrollTop + viewportHeight) {
        this.scrollEl.scrollTop = rect.y + this.cellHeight - viewportHeight + 16;
      }

      this.renderVisible();

      // Update selection based on modifier
      if (this.selectionModel && this.imageSource) {
        const orderedIds = await this.imageSource.getOrderedIds();
        const nextId = orderedIds[nextIndex];
        if (nextId) {
          this.selectionModel.handleClick(nextId, e.ctrlKey || e.metaKey, e.shiftKey, orderedIds);
        }
      }
    }
  }

  setCellSize(newSize) {
    if (newSize === this.cellSize || newSize < 64 || newSize > 320) return;

    const oldColCount = this.columnCount;
    const oldCellHeight = this.cellHeight;
    const anchorIndex = Math.max(0, this.focusedIndex >= 0 ? this.focusedIndex : 0);

    this.cellSize = newSize;
    this.recalculateLayout();

    const newScrollTop = getAnchorScrollTop(
      anchorIndex,
      oldColCount,
      this.columnCount,
      oldCellHeight,
      this.cellHeight,
      this.gap
    );

    this.scrollEl.scrollTop = newScrollTop;
    this.renderVisible(true);
  }

  setBackgroundMode(mode) {
    this.backgroundMode = mode;
    for (const cell of this.cellPool.values()) {
      const imgWrap = cell.querySelector('.grid-cell-img-wrap');
      if (imgWrap) {
        imgWrap.className = `grid-cell-img-wrap bg-transparency-${mode}`;
      }
    }
  }

  setCaptionMode(mode) {
    this.captionMode = mode;
    this.recalculateLayout();
    this.renderVisible(true);
  }

  refresh() {
    this.recalculateLayout();
    this.renderVisible(true);
  }

  destroy() {
    if (this.resizeObserver) {
      this.resizeObserver.disconnect();
    }
    this.thumbnailManager.cancelAll();
    this._recycleAll();
  }
}

