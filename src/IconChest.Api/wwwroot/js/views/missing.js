import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { dialog } from '../lib/dialog.js';
import { eventStream } from '../lib/events.js';
import { SelectionModel } from '../lib/selection-model.js';
import { PreviewOverlay } from '../components/preview.js';

const ICON_ASC = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z"/></svg>';
const ICON_DESC = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M20 12l-1.41-1.41L13 16.17V4h-2v12.17l-5.58-5.59L4 12l8 8 8-8z"/></svg>';
const ICON_SCAN = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46C19.54 15.03 20 13.57 20 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74C4.46 8.97 4 10.43 4 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z"/></svg>';

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  if (!bytes || isNaN(bytes)) return '-';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function shortenPath(path, maxLength = 32) {
  if (!path) return '';
  if (path.length <= maxLength) return path;
  const half = Math.floor((maxLength - 3) / 2);
  return path.slice(0, half) + '...' + path.slice(-half);
}

function formatRelativeTime(isoString) {
  if (!isoString) return '-';
  try {
    const diffMs = Date.now() - new Date(isoString).getTime();
    if (diffMs < 0) return 'Just now';
    const sec = Math.floor(diffMs / 1000);
    if (sec < 60) return `${sec}s ago`;
    const min = Math.floor(sec / 60);
    if (min < 60) return `${min}m ago`;
    const hrs = Math.floor(min / 60);
    if (hrs < 24) return `${hrs}h ago`;
    const days = Math.floor(hrs / 24);
    if (days < 30) return `${days}d ago`;
    const months = Math.floor(days / 30);
    return `${months}mo ago`;
  } catch {
    return isoString;
  }
}

export function createMissingView() {
  const container = el('div', { className: 'view-container missing-view' });

  const ROW_HEIGHT = 44;
  const BUFFER_ROWS = 5;
  const PAGE_SIZE = 50;

  let currentSort = 'since';
  let currentDir = 'desc';
  let totalCount = 0;
  let allMissingIds = [];
  let focusedIndex = -1;

  const selectionModel = new SelectionModel();
  const pageCache = new Map();
  const pendingRequests = new Map();

  // Preview overlay instance
  const previewOverlay = new PreviewOverlay();

  // Top Toolbar
  const countBadgeEl = el('span', { className: 'browser-count-badge' }, 'Loading missing records...');

  const sortSelect = el('select', {
    className: 'select browser-sort-select',
    'aria-label': 'Sort missing files by',
    onChange: (e) => {
      currentSort = e.target.value;
      loadData();
    }
  }, [
    el('option', { value: 'since' }, 'Missing Since'),
    el('option', { value: 'name' }, 'File Name'),
    el('option', { value: 'path' }, 'Folder Path'),
    el('option', { value: 'size' }, 'File Size')
  ]);
  sortSelect.value = currentSort;

  const dirBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm browser-dir-btn',
    title: 'Sort Descending (click to toggle)',
    'aria-label': 'Sort direction',
    onClick: () => {
      currentDir = currentDir === 'asc' ? 'desc' : 'asc';
      clear(dirBtn);
      dirBtn.appendChild(createSvg(currentDir === 'asc' ? ICON_ASC : ICON_DESC));
      dirBtn.title = `Sort ${currentDir === 'asc' ? 'Ascending' : 'Descending'} (click to change)`;
      loadData();
    }
  }, [createSvg(ICON_DESC)]);

  const btnRemove = el('button', {
    type: 'button',
    className: 'btn btn-sm btn-danger',
    onClick: () => promptRemove()
  }, 'Remove From Library');

  const btnRescan = el('button', {
    type: 'button',
    className: 'btn btn-sm btn-secondary',
    title: 'Rescan library folders for missing files',
    onClick: async () => {
      try {
        btnRescan.disabled = true;
        toast.show('Triggering library rescan...', 'info');
        await api.post('/api/scans', {});
        toast.show('Scan started in background', 'info');
      } catch (err) {
        toast.show(err.message || 'Scan trigger failed', 'error');
      } finally {
        setTimeout(() => { btnRescan.disabled = false; }, 3000);
      }
    }
  }, [createSvg(ICON_SCAN), el('span', { style: { marginLeft: '6px' } }, 'Rescan')]);

  const toolbar = el('div', { className: 'browser-toolbar' },
    el('div', { className: 'browser-toolbar-left' },
      el('span', { className: 'browser-title truncate' }, 'Missing Files'),
      countBadgeEl
    ),
    el('div', { className: 'browser-toolbar-right' },
      el('label', { className: 'browser-sort-label' }, 'Sort:'),
      sortSelect,
      dirBtn,
      btnRemove,
      btnRescan
    )
  );

  // Table Structure
  const tableHeader = el('div', {
    className: 'missing-table-header',
    style: {
      display: 'grid',
      gridTemplateColumns: '48px 2fr 3fr 1.5fr 1fr 1.2fr',
      gap: '8px',
      padding: '8px 16px',
      borderBottom: '2px solid var(--border-subtle)',
      fontWeight: '600',
      fontSize: '0.85rem',
      color: 'var(--text-muted)'
    }
  },
    el('div', {}, ''),
    el('div', {}, 'File Name'),
    el('div', {}, 'Folder Location'),
    el('div', {}, 'Library Folder'),
    el('div', {}, 'Size'),
    el('div', {}, 'Missing Since')
  );

  const scrollEl = el('div', {
    className: 'virtual-grid-scroll missing-scroll-area',
    tabIndex: '0',
    role: 'grid',
    'aria-label': 'Missing files list',
    style: { flex: '1', overflowY: 'auto', position: 'relative' }
  });

  const spacerEl = el('div', { style: { position: 'relative', width: '100%', minHeight: '100%' } });
  const poolEl = el('div', { style: { position: 'absolute', top: 0, left: 0, right: 0, bottom: 0 } });

  const emptyStateEl = el('div', {
    className: 'grid-empty-state',
    style: { display: 'none' }
  }, [
    el('p', { className: 'grid-empty-title' }, 'No missing files'),
    el('p', { className: 'grid-empty-subtitle' }, 'All files indexed in your library folders are present on disk.')
  ]);

  spacerEl.appendChild(poolEl);
  scrollEl.appendChild(spacerEl);
  scrollEl.appendChild(emptyStateEl);

  const mainArea = el('div', {
    style: { display: 'flex', flexDirection: 'column', flex: '1', height: 'calc(100% - 48px)', overflow: 'hidden' }
  }, tableHeader, scrollEl);

  container.appendChild(toolbar);
  container.appendChild(mainArea);

  const activeRowEls = new Map(); // rowIndex -> HTMLElement

  async function loadData() {
    countBadgeEl.textContent = 'Checking missing files...';
    pageCache.clear();
    pendingRequests.clear();

    try {
      const res = await api.get(`/api/missing?sort=${currentSort}&dir=${currentDir}&offset=0&limit=${PAGE_SIZE}`);
      totalCount = res?.total ?? 0;

      if (totalCount === 0) {
        countBadgeEl.textContent = '0 files';
        emptyStateEl.style.display = 'flex';
        spacerEl.style.display = 'none';
        btnRemove.disabled = true;
        updateRemoveButtonLabel();
        updateNavBadge(0);
        return;
      }

      emptyStateEl.style.display = 'none';
      spacerEl.style.display = 'block';
      spacerEl.style.height = `${totalCount * ROW_HEIGHT}px`;

      countBadgeEl.textContent = `${totalCount.toLocaleString()} missing files`;
      updateNavBadge(totalCount);
      updateRemoveButtonLabel();

      pageCache.set(0, res.items || []);

      // Build ID list for selection ranges
      allMissingIds = (res.items || []).map(i => i.id);

      renderVisible();
    } catch (err) {
      console.error('Failed to query missing files:', err);
      countBadgeEl.textContent = 'Error loading missing files';
    }
  }

  function updateRemoveButtonLabel() {
    const selCount = selectionModel.size;
    if (selCount > 0) {
      btnRemove.textContent = `Remove ${selCount} Selected`;
      btnRemove.disabled = false;
    } else {
      btnRemove.textContent = totalCount > 0 ? `Remove All ${totalCount} Missing` : 'Remove From Library';
      btnRemove.disabled = totalCount === 0;
    }
  }

  function updateNavBadge(count) {
    const badge = document.getElementById('nav-badge-missing');
    if (badge) {
      if (count > 0) {
        badge.textContent = count > 99 ? '99+' : String(count);
        badge.classList.remove('hidden');
      } else {
        badge.classList.add('hidden');
      }
    }
  }

  async function fetchPage(pageIndex) {
    if (pageCache.has(pageIndex)) return pageCache.get(pageIndex);
    if (pendingRequests.has(pageIndex)) return pendingRequests.get(pageIndex);

    const offset = pageIndex * PAGE_SIZE;
    const promise = api.get(`/api/missing?sort=${currentSort}&dir=${currentDir}&offset=${offset}&limit=${PAGE_SIZE}`)
      .then(res => {
        const items = res.items || [];
        pageCache.set(pageIndex, items);
        pendingRequests.delete(pageIndex);
        return items;
      })
      .catch(err => {
        pendingRequests.delete(pageIndex);
        throw err;
      });

    pendingRequests.set(pageIndex, promise);
    return promise;
  }

  async function getItem(index) {
    const pageIndex = Math.floor(index / PAGE_SIZE);
    const offsetInPage = index % PAGE_SIZE;
    const items = await fetchPage(pageIndex);
    return items[offsetInPage] || null;
  }

  let renderRaf = null;
  function renderVisible() {
    if (totalCount === 0) return;

    const scrollTop = scrollEl.scrollTop;
    const viewportHeight = scrollEl.clientHeight || 500;

    const rawStartRow = Math.floor(scrollTop / ROW_HEIGHT);
    const rawEndRow = Math.floor((scrollTop + viewportHeight) / ROW_HEIGHT);

    const startRow = Math.max(0, rawStartRow - BUFFER_ROWS);
    const endRow = Math.min(totalCount - 1, rawEndRow + BUFFER_ROWS);

    const neededRows = new Set();
    for (let r = startRow; r <= endRow; r++) {
      neededRows.add(r);
    }

    // Remove rows out of range
    for (const [rIdx, rowEl] of activeRowEls) {
      if (!neededRows.has(rIdx)) {
        rowEl.remove();
        activeRowEls.delete(rIdx);
      }
    }

    // Add or reposition needed rows
    for (const rIdx of neededRows) {
      let rowEl = activeRowEls.get(rIdx);
      if (!rowEl) {
        rowEl = createRowElement(rIdx);
        poolEl.appendChild(rowEl);
        activeRowEls.set(rIdx, rowEl);
      }

      rowEl.style.transform = `translateY(${rIdx * ROW_HEIGHT}px)`;

      const imgId = rowEl.dataset.imageId ? parseInt(rowEl.dataset.imageId, 10) : null;
      if (imgId) {
        rowEl.classList.toggle('is-selected', selectionModel.isSelected(imgId));
      }
      rowEl.classList.toggle('is-focused', rIdx === focusedIndex);
    }
  }

  function createRowElement(rowIndex) {
    const rowEl = el('div', {
      className: 'missing-table-row',
      tabIndex: '-1',
      role: 'row',
      style: {
        position: 'absolute',
        top: '0',
        left: '0',
        right: '0',
        height: `${ROW_HEIGHT}px`,
        display: 'grid',
        gridTemplateColumns: '48px 2fr 3fr 1.5fr 1fr 1.2fr',
        gap: '8px',
        alignItems: 'center',
        padding: '0 16px',
        borderBottom: '1px solid var(--border-subtle)',
        fontSize: '0.85rem',
        cursor: 'pointer'
      }
    });

    const thumbWrap = el('div', {
      style: {
        width: '32px',
        height: '32px',
        borderRadius: 'var(--radius-sm, 4px)',
        overflow: 'hidden',
        background: 'var(--bg-subtle, rgba(0,0,0,0.05))',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      }
    });
    const thumbImg = el('img', {
      style: { width: '100%', height: '100%', objectFit: 'contain' },
      alt: 'Thumb',
      loading: 'lazy'
    });
    thumbWrap.appendChild(thumbImg);

    const filenameEl = el('div', { className: 'truncate', style: { fontWeight: '500' } }, 'Loading...');
    const folderEl = el('div', { className: 'truncate', style: { color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)' } }, '');
    const libFolderEl = el('div', { className: 'truncate', style: { color: 'var(--text-muted)' } }, '');
    const sizeEl = el('div', { style: { color: 'var(--text-muted)' } }, '');
    const sinceEl = el('div', { style: { color: 'var(--color-danger, #ef4444)' } }, '');

    rowEl.appendChild(thumbWrap);
    rowEl.appendChild(filenameEl);
    rowEl.appendChild(folderEl);
    rowEl.appendChild(libFolderEl);
    rowEl.appendChild(sizeEl);
    rowEl.appendChild(sinceEl);

    rowEl.addEventListener('click', (e) => {
      const imgId = rowEl.dataset.imageId ? parseInt(rowEl.dataset.imageId, 10) : null;
      if (!imgId) return;

      focusedIndex = rowIndex;
      selectionModel.handleClick(imgId, e.ctrlKey || e.metaKey, e.shiftKey, allMissingIds);
      updateRemoveButtonLabel();
      renderVisible();
    });

    getItem(rowIndex).then(item => {
      if (!activeRowEls.has(rowIndex) || !item) return;

      rowEl.dataset.imageId = String(item.id);
      rowEl.dataset.missingSince = item.missingSinceUtc || '';
      filenameEl.textContent = item.fileName || 'Unnamed';
      filenameEl.title = item.fileName || '';

      folderEl.textContent = shortenPath(item.folder);
      folderEl.title = item.path || item.folder || '';

      libFolderEl.textContent = item.libraryFolder || '';
      libFolderEl.title = item.libraryFolder || '';

      sizeEl.textContent = formatBytes(item.sizeBytes || 0);

      sinceEl.textContent = formatRelativeTime(item.missingSinceUtc);
      sinceEl.title = item.missingSinceUtc ? new Date(item.missingSinceUtc).toLocaleString() : '';

      thumbImg.src = `/api/images/${item.id}/thumbnail?size=128`;

      if (selectionModel.isSelected(item.id)) {
        rowEl.classList.add('is-selected');
      }
    });

    return rowEl;
  }

  async function promptRemove() {
    const selCount = selectionModel.size;
    const isRemoveAll = selCount === 0;
    const message = isRemoveAll
      ? `Are you sure you want to remove all ${totalCount} missing records from the catalog?\n\nThis removes the catalog records and cached thumbnails for the selected images. No files on disk will be deleted, moved or modified.`
      : `Are you sure you want to remove ${selCount} missing records from the catalog?\n\nThis removes the catalog records and cached thumbnails for the selected images. No files on disk will be deleted, moved or modified.`;

    const ok = await dialog.confirm({
      title: 'Remove Missing Records',
      message,
      confirmText: isRemoveAll ? 'Remove All Missing' : 'Remove Selected',
      isDanger: true
    });
    if (!ok) return;

    try {
      const payload = isRemoveAll ? {} : { ids: selectionModel.getSelectedList() };
      const res = await api.post('/api/missing/remove', payload);
      toast.show(`Removed ${res?.removedCount ?? 0} missing records.`, 'success');

      // Clear selection and run orphan cleanup
      selectionModel.clear();
      api.post('/api/thumbnails/cleanup-orphans').catch(() => {});

      loadData();
    } catch (err) {
      toast.show(err.message || 'Failed to remove records', 'error');
    }
  }

  // Keyboard navigation
  scrollEl.addEventListener('keydown', (e) => {
    if (e.key === 'a' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      selectionModel.selectAll(allMissingIds);
      updateRemoveButtonLabel();
      renderVisible();
      return;
    }

    if (e.key === ' ' || e.key === 'Enter') {
      e.preventDefault();
      const selected = selectionModel.getSelectedList();
      if (selected.length > 0) {
        const startIdx = allMissingIds.indexOf(selected[0]);
        previewOverlay.open(allMissingIds, startIdx >= 0 ? startIdx : 0);
      }
      return;
    }

    if (e.key === 'ArrowUp' && focusedIndex > 0) {
      e.preventDefault();
      focusedIndex--;
      if (focusedIndex * ROW_HEIGHT < scrollEl.scrollTop) {
        scrollEl.scrollTop = focusedIndex * ROW_HEIGHT;
      }
      const itemRow = activeRowEls.get(focusedIndex);
      const imgId = itemRow?.dataset?.imageId ? parseInt(itemRow.dataset.imageId, 10) : null;
      if (imgId) {
        selectionModel.handleClick(imgId, e.ctrlKey || e.metaKey, e.shiftKey, allMissingIds);
        updateRemoveButtonLabel();
      }
      renderVisible();
    } else if (e.key === 'ArrowDown' && focusedIndex < totalCount - 1) {
      e.preventDefault();
      focusedIndex++;
      if ((focusedIndex + 1) * ROW_HEIGHT > scrollEl.scrollTop + scrollEl.clientHeight) {
        scrollEl.scrollTop = (focusedIndex + 1) * ROW_HEIGHT - scrollEl.clientHeight;
      }
      const itemRow = activeRowEls.get(focusedIndex);
      const imgId = itemRow?.dataset?.imageId ? parseInt(itemRow.dataset.imageId, 10) : null;
      if (imgId) {
        selectionModel.handleClick(imgId, e.ctrlKey || e.metaKey, e.shiftKey, allMissingIds);
        updateRemoveButtonLabel();
      }
      renderVisible();
    }
  });

  scrollEl.addEventListener('scroll', () => {
    if (renderRaf) return;
    renderRaf = requestAnimationFrame(() => {
      renderRaf = null;
      renderVisible();
    });
  });

  function updateRelativeTimes() {
    for (const [, rowEl] of activeRowEls) {
      const iso = rowEl.dataset.missingSince;
      if (iso) {
        const sinceCell = rowEl.children[5];
        if (sinceCell) {
          sinceCell.textContent = formatRelativeTime(iso);
        }
      }
    }
  }

  const ticker = setInterval(updateRelativeTimes, 10000);

  const unsubScan = eventStream.on('scan.completed', () => loadData());
  const unsubLib = eventStream.on('library.changed', () => loadData());
  const unsubData = eventStream.on('data.reloaded', () => loadData());

  return {
    element: container,
    onActivated: () => {
      loadData();
    },
    reload: () => {
      loadData();
    },
    destroy: () => {
      clearInterval(ticker);
      if (typeof unsubScan === 'function') unsubScan();
      if (typeof unsubLib === 'function') unsubLib();
      if (typeof unsubData === 'function') unsubData();
    }
  };
}
