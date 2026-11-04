import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { store } from '../lib/store.js';
import {
  buildGroupLayout,
  getVisibleGroupedRange,
  getItemPosition,
  getItemIndexAtPoint,
  getAdjacentItemIndex,
  findGroupIndexForItem
} from '../lib/grouped-geometry.js';
import { SelectionModel } from '../lib/selection-model.js';
import { DetailsPanel } from '../components/details-panel.js';
import { PreviewOverlay } from '../components/preview.js';
import { pickThumbnailSize, ThumbnailManager } from '../lib/thumb-loader.js';
import { toast } from '../lib/toast.js';

const ICON_ASC = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z"/></svg>';
const ICON_DESC = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M20 12l-1.41-1.41L13 16.17V4h-2v12.17l-5.58-5.59L4 12l8 8 8-8z"/></svg>';
const ICON_BG = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14v12c3.31 0 6-2.69 6-6s-2.69-6-6-6z"/></svg>';
const ICON_INFO = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>';
const HEART_ICON = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>';

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  if (!bytes || isNaN(bytes)) return '-';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function shortenPath(path, maxLength = 26) {
  if (!path) return '';
  if (path.length <= maxLength) return path;
  const half = Math.floor((maxLength - 3) / 2);
  return path.slice(0, half) + '...' + path.slice(-half);
}

export function createDuplicatesView() {
  const container = el('div', { className: 'view-container duplicates-view' });

  // Preferences
  const appSettings = store.get('settings') || {};
  let backgroundMode = localStorage.getItem('iconchest_grid_bg') ||
    (appSettings?.appearance?.checkerboardMode || 'checker').toLowerCase();
  let cellSize = parseInt(localStorage.getItem('iconchest_grid_size') || '160', 10);
  if (isNaN(cellSize) || cellSize < 64 || cellSize > 320) cellSize = 160;

  let currentSort = 'reclaimable';
  let currentDir = 'desc';

  const captionHeight = 22;
  const headerHeight = 36;
  const gap = 8;
  const groupGap = 16;
  const PAGE_SIZE = 50; // Groups per page

  // Selection & Components
  const selectionModel = new SelectionModel();
  const thumbnailManager = new ThumbnailManager();
  let layout = null;
  let summaryData = null;
  let allDuplicateIds = [];
  let focusedIndex = -1;

  // Group pages cache: pageIndex -> array of groups
  const groupPageCache = new Map();
  const pendingPageRequests = new Map();

  // Details Panel
  let infoBtn = null;
  const detailsPanel = new DetailsPanel({
    backgroundMode,
    onRestoreSelection: (ids) => {
      selectionModel.replaceSelection(ids);
      renderVisible();
    },
    onSelectImage: (id) => {
      selectionModel.handleClick(id, false, false, allDuplicateIds);
      renderVisible();
    },
    onSaveSuccess: () => {
      loadData();
    },
    onFavouriteToggled: (id, isFav) => {
      updateImageFavourite(id, isFav);
      renderVisible();
    },
    onVisibilityChange: (isOpen) => {
      if (infoBtn) infoBtn.classList.toggle('active', isOpen);
    },
    onTransitionEnd: () => {
      recalculateLayout();
      renderVisible();
    }
  });

  // Preview Overlay
  const previewOverlay = new PreviewOverlay({
    onSelectIndex: (idx) => {
      focusedIndex = idx;
      renderVisible();
    }
  });

  // Top Toolbar Elements
  const totalsBadgeEl = el('span', { className: 'browser-count-badge' }, 'Loading duplicates...');

  const sortSelect = el('select', {
    className: 'select browser-sort-select',
    'aria-label': 'Sort duplicates by',
    onChange: (e) => {
      currentSort = e.target.value;
      loadData();
    }
  }, [
    el('option', { value: 'reclaimable' }, 'Reclaimable Size'),
    el('option', { value: 'copies' }, 'Number of Copies'),
    el('option', { value: 'size' }, 'File Size'),
    el('option', { value: 'name' }, 'File Name')
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

  const bgBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm browser-bg-btn',
    title: `Background: ${backgroundMode}`,
    'aria-label': 'Toggle grid background',
    onClick: () => {
      const modes = ['checker', 'light', 'dark'];
      backgroundMode = modes[(modes.indexOf(backgroundMode) + 1) % modes.length];
      localStorage.setItem('iconchest_grid_bg', backgroundMode);
      bgBtn.title = `Background: ${backgroundMode}`;
      updateGridBackground();
      detailsPanel.setBackgroundMode(backgroundMode);
    }
  }, [createSvg(ICON_BG)]);

  const sizeSlider = el('input', {
    type: 'range',
    className: 'browser-size-slider',
    min: '64',
    max: '320',
    step: '16',
    value: String(cellSize),
    title: `Thumbnail size: ${cellSize}px`,
    'aria-label': 'Thumbnail size',
    onInput: (e) => {
      cellSize = parseInt(e.target.value, 10);
      sizeSlider.title = `Thumbnail size: ${cellSize}px`;
      localStorage.setItem('iconchest_grid_size', String(cellSize));
      recalculateLayout();
      renderVisible();
    }
  });

  infoBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm browser-info-btn',
    title: 'Toggle Details Panel (I)',
    'aria-label': 'Toggle Details Panel',
    onClick: () => {
      detailsPanel.toggle();
    }
  }, [createSvg(ICON_INFO)]);

  const toolbar = el('div', { className: 'browser-toolbar' },
    el('div', { className: 'browser-toolbar-left' },
      el('span', { className: 'browser-title truncate' }, 'Duplicate Icons'),
      totalsBadgeEl
    ),
    el('div', { className: 'browser-toolbar-right' },
      el('label', { className: 'browser-sort-label' }, 'Sort:'),
      sortSelect,
      dirBtn,
      bgBtn,
      sizeSlider,
      infoBtn
    )
  );

  // Main Working Area (Grid + Details Drawer)
  const gridWrap = el('div', { className: 'browser-grid-wrap' });
  const mainArea = el('div', { className: 'browser-main-area' }, [
    gridWrap,
    detailsPanel.containerEl
  ]);

  const scrollEl = el('div', {
    className: 'virtual-grid-scroll',
    tabIndex: '0',
    role: 'grid',
    'aria-label': 'Duplicate sets grid'
  });

  const spacerEl = el('div', { className: 'virtual-grid-spacer' });
  const poolEl = el('div', { className: 'virtual-grid-pool' });

  const emptyStateEl = el('div', {
    className: 'grid-empty-state',
    style: { display: 'none' }
  }, [
    el('p', { className: 'grid-empty-title' }, 'No duplicates found'),
    el('p', { className: 'grid-empty-subtitle' }, 'All indexed images in your library folders are unique.')
  ]);

  spacerEl.appendChild(poolEl);
  scrollEl.appendChild(spacerEl);
  scrollEl.appendChild(emptyStateEl);
  gridWrap.appendChild(scrollEl);

  container.appendChild(toolbar);
  container.appendChild(mainArea);

  // Element DOM recycling maps
  const activeHeaderEls = new Map(); // groupIndex -> headerElement
  const activeCellEls = new Map();   // itemIndex -> cellElement

  function updateGridBackground() {
    scrollEl.classList.remove('bg-checker', 'bg-light', 'bg-dark');
    scrollEl.classList.add(`bg-${backgroundMode}`);
    for (const cell of activeCellEls.values()) {
      const imgWrap = cell.querySelector('.grid-cell-img-wrap');
      if (imgWrap) {
        imgWrap.className = `grid-cell-img-wrap bg-transparency-${backgroundMode}`;
      }
    }
  }
  updateGridBackground();

  function calculateColumns() {
    const width = scrollEl.clientWidth || 800;
    const cols = Math.floor((width + gap) / (cellSize + gap));
    return Math.max(1, cols);
  }

  function recalculateLayout() {
    if (!summaryData || !summaryData.groupSizes) return;
    const cols = calculateColumns();
    const cellH = cellSize + captionHeight;
    layout = buildGroupLayout(summaryData.groupSizes, cols, cellSize, cellH, {
      gap,
      headerHeight,
      groupGap
    });
    spacerEl.style.height = `${layout.totalHeight}px`;
  }

  async function loadData() {
    totalsBadgeEl.textContent = 'Scanning duplicate sets...';
    groupPageCache.clear();
    pendingPageRequests.clear();

    try {
      summaryData = await api.get(`/api/duplicates/summary?sort=${currentSort}&dir=${currentDir}`);
      const count = summaryData.groupCount || 0;

      if (count === 0) {
        totalsBadgeEl.textContent = '0 sets';
        emptyStateEl.style.display = 'flex';
        spacerEl.style.display = 'none';
        allDuplicateIds = [];
        return;
      }

      emptyStateEl.style.display = 'none';
      spacerEl.style.display = 'block';

      totalsBadgeEl.textContent = `${count.toLocaleString()} sets, ${(summaryData.imageCount || 0).toLocaleString()} files, ${formatBytes(summaryData.reclaimableBytes || 0)} reclaimable`;

      // Fetch duplicate IDs for select all and details lookup
      api.get(`/api/duplicates/ids?sort=${currentSort}&dir=${currentDir}`).then(ids => {
        allDuplicateIds = Array.isArray(ids) ? ids : [];
      }).catch(() => {});

      recalculateLayout();
      renderVisible();
    } catch (err) {
      console.error('Failed to load duplicates summary:', err);
      totalsBadgeEl.textContent = 'Error loading duplicates';
    }
  }

  async function fetchGroupPage(pageIndex) {
    if (groupPageCache.has(pageIndex)) {
      return groupPageCache.get(pageIndex);
    }
    if (pendingPageRequests.has(pageIndex)) {
      return pendingPageRequests.get(pageIndex);
    }

    const offset = pageIndex * PAGE_SIZE;
    const promise = api.get(`/api/duplicates?sort=${currentSort}&dir=${currentDir}&offset=${offset}&limit=${PAGE_SIZE}`)
      .then(res => {
        const groups = res.groups || [];
        groupPageCache.set(pageIndex, groups);
        pendingPageRequests.delete(pageIndex);
        return groups;
      })
      .catch(err => {
        pendingPageRequests.delete(pageIndex);
        throw err;
      });

    pendingPageRequests.set(pageIndex, promise);
    return promise;
  }

  async function getGroupData(groupIndex) {
    const pageIndex = Math.floor(groupIndex / PAGE_SIZE);
    const offsetInPage = groupIndex % PAGE_SIZE;
    const groups = await fetchGroupPage(pageIndex);
    return groups[offsetInPage] || null;
  }

  let renderRaf = null;
  function renderVisible() {
    if (!layout || layout.groups.length === 0) return;

    const scrollTop = scrollEl.scrollTop;
    const viewportHeight = scrollEl.clientHeight || 600;
    const visible = getVisibleGroupedRange(layout, scrollTop, viewportHeight);

    const neededHeaders = new Set();
    const neededCells = new Set();

    for (const vg of visible.visibleGroups) {
      if (vg.headerVisible) {
        neededHeaders.add(vg.groupIndex);
      }
      for (let i = vg.startItemIndex; i <= vg.endItemIndex; i++) {
        neededCells.add(i);
      }
    }

    // Remove headers out of range
    for (const [gIdx, headerEl] of activeHeaderEls) {
      if (!neededHeaders.has(gIdx)) {
        headerEl.remove();
        activeHeaderEls.delete(gIdx);
      }
    }

    // Remove cells out of range
    for (const [iIdx, cellEl] of activeCellEls) {
      if (!neededCells.has(iIdx)) {
        cellEl.remove();
        activeCellEls.delete(iIdx);
      }
    }

    // Render or update visible headers
    for (const gIdx of neededHeaders) {
      let headerEl = activeHeaderEls.get(gIdx);
      const grp = layout.groups[gIdx];

      if (!headerEl) {
        headerEl = el('div', {
          className: 'duplicate-group-header',
          style: {
            position: 'absolute',
            top: `${grp.y}px`,
            left: '0',
            right: '0',
            height: `${headerHeight}px`
          }
        }, 'Loading group details...');
        poolEl.appendChild(headerEl);
        activeHeaderEls.set(gIdx, headerEl);

        getGroupData(gIdx).then(gData => {
          if (!activeHeaderEls.has(gIdx) || !gData) return;
          const copies = gData.count ?? gData.items?.length ?? 0;
          const reclaimable = gData.reclaimableBytes ?? Math.max(0, (copies - 1) * gData.sizeBytes);
          headerEl.textContent = `${copies} copies, ${formatBytes(gData.sizeBytes)} each, ${formatBytes(reclaimable)} reclaimable`;
        });
      } else {
        headerEl.style.top = `${grp.y}px`;
      }
    }

    // Render or update visible cells
    for (const iIdx of neededCells) {
      const pos = getItemPosition(layout, iIdx);
      let cellEl = activeCellEls.get(iIdx);

      if (!cellEl) {
        cellEl = createCellElement(iIdx);
        poolEl.appendChild(cellEl);
        activeCellEls.set(iIdx, cellEl);
      }

      cellEl.style.transform = `translate(${pos.x}px, ${pos.y}px)`;
      cellEl.style.width = `${pos.width}px`;
      cellEl.style.height = `${pos.height}px`;

      // Update selection class and thumbnail resolution
      const imgId = cellEl.dataset.imageId ? parseInt(cellEl.dataset.imageId, 10) : null;
      if (imgId) {
        cellEl.classList.toggle('is-selected', selectionModel.isSelected(imgId));
        const neededThumbSize = pickThumbnailSize(cellSize, window.devicePixelRatio || 1);
        if (cellEl.dataset.thumbSize !== String(neededThumbSize)) {
          cellEl.dataset.thumbSize = String(neededThumbSize);
          const img = cellEl.querySelector('.grid-cell-img');
          if (img) {
            img.src = `/api/images/${imgId}/thumbnail?size=${neededThumbSize}`;
          }
        }
      }
      cellEl.classList.toggle('is-focused', iIdx === focusedIndex);
    }
  }

  function createCellElement(itemIndex) {
    const cellEl = el('div', {
      className: 'grid-cell',
      tabIndex: '-1',
      role: 'gridcell'
    });

    const thumbWrap = el('div', { className: `grid-cell-img-wrap bg-transparency-${backgroundMode}` });
    const imgEl = el('img', {
      className: 'grid-cell-img',
      alt: 'Thumbnail',
      loading: 'lazy'
    });
    thumbWrap.appendChild(imgEl);

    const heartBtn = el('button', {
      type: 'button',
      className: 'grid-cell-heart',
      title: 'Toggle favourite',
      'aria-label': 'Toggle favourite',
      tabIndex: '-1',
      onClick: (e) => {
        e.stopPropagation();
        const imgId = cellEl.dataset.imageId ? parseInt(cellEl.dataset.imageId, 10) : null;
        if (!imgId) return;
        const curFav = heartBtn.classList.contains('active');
        updateImageFavourite(imgId, !curFav);
      }
    }, [createSvg(HEART_ICON)]);
    thumbWrap.appendChild(heartBtn);

    const captionEl = el('div', { className: 'grid-cell-caption' });
    const captionTextEl = el('span', { className: 'caption-text truncate' });
    captionEl.appendChild(captionTextEl);

    cellEl.appendChild(thumbWrap);
    cellEl.appendChild(captionEl);

    // Click handler for selection and details panel
    cellEl.addEventListener('click', async (e) => {
      // Don't toggle selection if heart button clicked
      if (e.target.closest('.grid-cell-heart')) return;

      let imgId = cellEl.dataset.imageId ? parseInt(cellEl.dataset.imageId, 10) : null;
      if (!imgId && layout) {
        const grpIdx = findGroupIndexForItem(layout, itemIndex);
        if (grpIdx >= 0) {
          const gData = await getGroupData(grpIdx);
          const idxInGrp = itemIndex - layout.groups[grpIdx].firstItemIndex;
          const items = gData?.items || gData?.images || [];
          if (items[idxInGrp]) {
            imgId = items[idxInGrp].id;
            cellEl.dataset.imageId = String(imgId);
          }
        }
      }
      if (!imgId) return;

      focusedIndex = itemIndex;
      selectionModel.handleClick(imgId, e.ctrlKey || e.metaKey, e.shiftKey, allDuplicateIds);
      await detailsPanel.updateSelection(selectionModel.getSelectedList());
      detailsPanel.openByUser();
      renderVisible();
    });

    // Populate async image data from group
    const grpIdx = layout ? findGroupIndexForItem(layout, itemIndex) : -1;
    if (grpIdx >= 0) {
      const idxInGrp = itemIndex - layout.groups[grpIdx].firstItemIndex;
      getGroupData(grpIdx).then(gData => {
        if (!activeCellEls.has(itemIndex) || !gData) return;
        const items = gData.items || gData.images || [];
        const img = items[idxInGrp];
        if (!img) return;

        cellEl.dataset.imageId = String(img.id);
        const folder = img.directory || img.folder || '';
        captionTextEl.textContent = shortenPath(folder);
        captionTextEl.title = `${img.fileName || ''}\n${folder}`;

        const isFav = Boolean(img.isFavourite || img.is_favourite);
        heartBtn.classList.toggle('active', isFav);
        heartBtn.setAttribute('aria-pressed', String(isFav));

        const thumbSize = pickThumbnailSize(cellSize, window.devicePixelRatio || 1);
        cellEl.dataset.thumbSize = String(thumbSize);
        imgEl.src = `/api/images/${img.id}/thumbnail?size=${thumbSize}`;

        if (selectionModel.isSelected(img.id)) {
          cellEl.classList.add('is-selected');
        }
      });
    }

    return cellEl;
  }

  async function updateImageFavourite(id, isFav) {
    try {
      await api.patch(`/api/images/${id}`, { favourite: isFav });
      // Update locally
      for (const [idx, cell] of activeCellEls) {
        if (cell.dataset.imageId === String(id)) {
          const heart = cell.querySelector('.grid-cell-heart');
          if (heart) {
            heart.classList.toggle('active', isFav);
            heart.setAttribute('aria-pressed', String(isFav));
          }
        }
      }
    } catch (err) {
      toast.show('Failed to update favourite status', 'error');
    }
  }

  // Keyboard navigation
  scrollEl.addEventListener('keydown', (e) => {
    if (e.key === 'a' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      selectionModel.selectAll(allDuplicateIds);
      detailsPanel.updateSelection(selectionModel.getSelectedList());
      renderVisible();
      return;
    }

    if (e.key === ' ' || e.key === 'Enter') {
      e.preventDefault();
      const selected = selectionModel.getSelectedList();
      if (selected.length > 0) {
        const startIdx = allDuplicateIds.indexOf(selected[0]);
        previewOverlay.open(allDuplicateIds, startIdx >= 0 ? startIdx : 0, backgroundMode);
      }
      return;
    }

    if ((e.key === 'i' || e.key === 'I') && !e.ctrlKey && !e.metaKey && !e.altKey) {
      e.preventDefault();
      detailsPanel.toggle();
      return;
    }

    let dir = null;
    if (e.key === 'ArrowLeft') dir = 'left';
    else if (e.key === 'ArrowRight') dir = 'right';
    else if (e.key === 'ArrowUp') dir = 'up';
    else if (e.key === 'ArrowDown') dir = 'down';

    if (dir && layout) {
      e.preventDefault();
      if (focusedIndex === -1) focusedIndex = 0;
      const nextIdx = getAdjacentItemIndex(layout, focusedIndex, dir);
      if (nextIdx !== focusedIndex) {
        focusedIndex = nextIdx;
        const targetPos = getItemPosition(layout, focusedIndex);
        // Scroll into view if needed
        if (targetPos.y < scrollEl.scrollTop) {
          scrollEl.scrollTop = targetPos.y;
        } else if (targetPos.y + targetPos.height > scrollEl.scrollTop + scrollEl.clientHeight) {
          scrollEl.scrollTop = targetPos.y + targetPos.height - scrollEl.clientHeight;
        }

        const cell = activeCellEls.get(focusedIndex);
        const imgId = cell?.dataset?.imageId ? parseInt(cell.dataset.imageId, 10) : null;
        if (imgId) {
          selectionModel.handleClick(imgId, e.ctrlKey || e.metaKey, e.shiftKey, allDuplicateIds);
          detailsPanel.updateSelection(selectionModel.getSelectedList());
        }
        renderVisible();
      }
    }
  });

  scrollEl.addEventListener('scroll', () => {
    if (renderRaf) return;
    renderRaf = requestAnimationFrame(() => {
      renderRaf = null;
      renderVisible();
    });
  });

  window.addEventListener('resize', () => {
    recalculateLayout();
    renderVisible();
  });

  return {
    element: container,
    onActivated: () => {
      loadData();
    },
    reload: () => {
      loadData();
    },
    refresh: () => {
      loadData();
    },
    hasUnsavedChanges: () => detailsPanel && detailsPanel.isDirty(),
    getDetailsPanel: () => detailsPanel
  };
}
