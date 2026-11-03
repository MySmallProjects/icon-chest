import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { store } from '../lib/store.js';
import { dialog } from '../lib/dialog.js';
import { ImageSource } from '../lib/image-source.js';
import { SelectionModel } from '../lib/selection-model.js';
import { VirtualGrid } from '../components/virtual-grid.js';
import { DetailsPanel } from '../components/details-panel.js';
import { PreviewOverlay } from '../components/preview.js';

// SVG Icons
const ICON_ASC = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z"/></svg>';
const ICON_DESC = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M20 12l-1.41-1.41L13 16.17V4h-2v12.17l-5.58-5.59L4 12l8 8 8-8z"/></svg>';
const ICON_INFO = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>';
const ICON_BG = '<svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14v12c3.31 0 6-2.69 6-6s-2.69-6-6-6z"/></svg>';

export function createImageBrowser(options = {}) {
  const container = el('div', { className: 'view-container image-browser-view' });

  // Load preferences
  const appSettings = store.get('settings') || {};
  let backgroundMode = localStorage.getItem('iconchest_grid_bg') ||
    appSettings?.appearance?.transparencyBackground || 'checker';
  let cellSize = parseInt(localStorage.getItem('iconchest_grid_size') || '160', 10);
  if (isNaN(cellSize) || cellSize < 64 || cellSize > 320) cellSize = 160;

  let currentSort = options.defaultSort || 'effectivedate';
  let currentDir = options.defaultDir || 'desc';
  let filterParams = options.initialFilter || {};
  let currentTitle = options.defaultTitle || 'All Images';

  const captionMode = appSettings?.appearance?.gridCaption || 'filename';

  // Instantiate Sub-Systems
  const selectionModel = new SelectionModel();
  let imageSource = null;
  let virtualGrid = null;

  // Preview overlay instance
  const previewOverlay = new PreviewOverlay({
    onSelectIndex: (idx) => {
      if (virtualGrid) {
        virtualGrid.focusedIndex = idx;
      }
    }
  });

  // Details panel instance
  const detailsPanel = new DetailsPanel({
    backgroundMode,
    onRestoreSelection: (ids) => {
      selectionModel.replaceSelection(ids);
    },
    onSelectImage: async (id) => {
      if (imageSource) {
        const allIds = await imageSource.getOrderedIds();
        selectionModel.handleClick(id, false, false, allIds);
      } else {
        selectionModel.handleClick(id, false, false, []);
      }
    },
    onSaveSuccess: () => {
      loadData();
    },
    onFavouriteToggled: (id, isFav) => {
      if (virtualGrid) {
        virtualGrid.renderVisible();
      }
      if (options.viewId === 'favourites' && !isFav) {
        loadData();
      }
    }
  });

  // Top Toolbar
  const titleEl = el('span', { className: 'browser-title truncate' }, currentTitle);
  const countBadgeEl = el('span', { className: 'browser-count-badge' }, '0 images');

  const sortSelect = el('select', {
    className: 'select browser-sort-select',
    'aria-label': 'Sort images by',
    onChange: (e) => {
      currentSort = e.target.value;
      loadData();
    }
  }, [
    el('option', { value: 'effectivedate' }, 'Date (Effective)'),
    el('option', { value: 'created' }, 'Date Created'),
    el('option', { value: 'modified' }, 'Date Modified'),
    el('option', { value: 'taken' }, 'Date Taken'),
    el('option', { value: 'filename' }, 'File Name'),
    el('option', { value: 'size' }, 'File Size'),
    el('option', { value: 'width' }, 'Width'),
    el('option', { value: 'height' }, 'Height'),
    el('option', { value: 'rating' }, 'Rating')
  ]);
  sortSelect.value = currentSort;

  const dirBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm browser-dir-btn',
    title: `Sort ${currentDir === 'asc' ? 'Ascending' : 'Descending'} (click to change)`,
    'aria-label': `Sort ${currentDir === 'asc' ? 'Ascending' : 'Descending'}`,
    onClick: () => {
      currentDir = currentDir === 'asc' ? 'desc' : 'asc';
      dirBtn.title = `Sort ${currentDir === 'asc' ? 'Ascending' : 'Descending'} (click to change)`;
      dirBtn.setAttribute('aria-label', `Sort ${currentDir === 'asc' ? 'Ascending' : 'Descending'}`);
      clear(dirBtn);
      dirBtn.appendChild(createSvg(currentDir === 'asc' ? ICON_ASC : ICON_DESC));
      loadData();
    }
  }, [createSvg(currentDir === 'asc' ? ICON_ASC : ICON_DESC)]);

  const bgBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm browser-bg-btn',
    title: `Transparency background: ${backgroundMode} (click to toggle)`,
    'aria-label': `Transparency background: ${backgroundMode}`,
    onClick: () => {
      const modes = ['checker', 'light', 'dark'];
      const nextIdx = (modes.indexOf(backgroundMode) + 1) % modes.length;
      backgroundMode = modes[nextIdx];
      localStorage.setItem('iconchest_grid_bg', backgroundMode);
      bgBtn.title = `Transparency background: ${backgroundMode} (click to toggle)`;
      bgBtn.setAttribute('aria-label', `Transparency background: ${backgroundMode}`);
      if (virtualGrid) virtualGrid.setBackgroundMode(backgroundMode);
      if (detailsPanel) detailsPanel.setBackgroundMode(backgroundMode);
    }
  }, [createSvg(ICON_BG)]);

  const infoBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm browser-info-btn',
    title: 'Toggle Details Panel',
    'aria-label': 'Toggle Details Panel',
    onClick: () => {
      detailsPanel.toggle();
    }
  }, [createSvg(ICON_INFO)]);

  const topToolbar = el('div', { className: 'browser-toolbar' }, [
    el('div', { className: 'browser-toolbar-left' }, [titleEl, countBadgeEl]),
    el('div', { className: 'browser-toolbar-right' }, [sortSelect, dirBtn, bgBtn, infoBtn])
  ]);

  // Main Working Area (Grid + Details Drawer)
  const gridWrap = el('div', { className: 'browser-grid-wrap' });
  const mainArea = el('div', { className: 'browser-main-area' }, [
    gridWrap,
    detailsPanel.containerEl
  ]);

  // Bottom Footer
  const selectionCountEl = el('span', { className: 'browser-selection-count' }, '0 of 0 selected');

  const selectAllBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm',
    onClick: async () => {
      if (imageSource) {
        const ids = await imageSource.getOrderedIds();
        selectionModel.selectAll(ids);
      }
    }
  }, 'Select All');

  const clearBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm',
    onClick: () => {
      selectionModel.clear();
    }
  }, 'Clear');

  const previewBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm btn-primary',
    onClick: async () => {
      if (!imageSource || imageSource.total === 0) return;
      const allIds = await imageSource.getOrderedIds();
      let startIdx = 0;
      if (selectionModel.count > 0 && selectionModel.focusId) {
        startIdx = allIds.indexOf(selectionModel.focusId);
        if (startIdx === -1) startIdx = 0;
      }
      previewOverlay.open(allIds, startIdx, backgroundMode);
    }
  }, 'Preview');

  const zoomSlider = el('input', {
    type: 'range',
    className: 'browser-zoom-slider',
    min: '64',
    max: '320',
    step: '8',
    value: String(cellSize),
    'aria-label': 'Thumbnail size slider',
    onInput: (e) => {
      const val = parseInt(e.target.value, 10);
      zoomLabel.textContent = `${val}px`;
      cellSize = val;
      localStorage.setItem('iconchest_grid_size', String(val));
      if (virtualGrid) {
        virtualGrid.setCellSize(val);
      }
    }
  });

  const zoomLabel = el('span', { className: 'browser-zoom-label' }, `${cellSize}px`);

  const bottomFooter = el('div', { className: 'browser-footer' }, [
    el('div', { className: 'browser-footer-left' }, [
      selectionCountEl,
      selectAllBtn,
      clearBtn,
      previewBtn
    ]),
    el('div', { className: 'browser-footer-right' }, [
      zoomSlider,
      zoomLabel
    ])
  ]);

  container.appendChild(topToolbar);
  container.appendChild(mainArea);
  container.appendChild(bottomFooter);

  // 'I' shortcut toggles details panel
  container.addEventListener('keydown', (e) => {
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) return;
    if ((e.key === 'i' || e.key === 'I') && !e.ctrlKey && !e.metaKey && !e.altKey) {
      e.preventDefault();
      detailsPanel.toggle();
    }
  });

  // Build query string
  function buildQueryString(offset, limit) {
    const params = new URLSearchParams();

    // Sorting & pagination
    params.set('sort', currentSort);
    params.set('dir', currentDir);
    if (typeof offset === 'number') params.set('offset', String(offset));
    if (typeof limit === 'number') params.set('limit', String(limit));

    // Filters
    for (const [key, value] of Object.entries(filterParams)) {
      if (value === null || value === undefined || value === '') continue;

      if (key === 'tag' && Array.isArray(value)) {
        for (const t of value) {
          if (t) params.append('tag', String(t));
        }
      } else if (key === 'tags' && Array.isArray(value)) {
        for (const t of value) {
          if (t) params.append('tag', String(t));
        }
      } else {
        params.set(key, String(value));
      }
    }

    return params.toString();
  }

  // Data loader
  async function loadData() {
    if (imageSource) {
      imageSource.invalidate();
    }

    imageSource = new ImageSource({
      pageSize: 200,
      fetchFn: async (offset, limit, signal) => {
        const qs = buildQueryString(offset, limit);
        const res = await api.get(`/api/images?${qs}`, { signal });
        return {
          total: res.total || 0,
          items: res.items || []
        };
      },
      fetchIdsFn: async () => {
        const qs = buildQueryString();
        const res = await api.get(`/api/images/ids?${qs}`);
        return res || [];
      }
    });

    if (virtualGrid) {
      virtualGrid.imageSource = imageSource;
    } else {
      virtualGrid = new VirtualGrid({
        containerEl: gridWrap,
        imageSource,
        selectionModel,
        cellSize,
        backgroundMode,
        captionMode,
        onOpenPreview: async (index, item) => {
          const allIds = await imageSource.getOrderedIds();
          previewOverlay.open(allIds, index, backgroundMode);
        },
        onToggleFavourite: async (imageId, isFav) => {
          try {
            await api.patch('/api/images', {
              ids: [Number(imageId)],
              isFavourite: isFav,
              favourite: isFav
            });
            if (detailsPanel) {
              detailsPanel.updateFavourite(Number(imageId), isFav);
            }
            // If in favourites view and unfavourited, soft refresh
            if (options.viewId === 'favourites' && !isFav) {
              loadData();
            }
          } catch (err) {
            console.error('Failed to update favourite status', err);
          }
        },
        onSelectionChange: (selectedIds) => {
          updateFooterSelection(selectedIds);
          detailsPanel.updateSelection(selectedIds);
        },
        onToggleDetails: () => {
          detailsPanel.toggle();
        }
      });
    }

    try {
      // Load initial page (page 0) to establish total count
      const firstPage = await imageSource.loadPage(0);
      countBadgeEl.textContent = `${imageSource.total.toLocaleString()} ${imageSource.total === 1 ? 'image' : 'images'}`;
      updateFooterSelection(selectionModel.getSelectedList());
      virtualGrid.recalculateLayout();
      virtualGrid.renderVisible(true);
    } catch (err) {
      if (err.name !== 'AbortError') {
        console.error('Failed to load images:', err);
        countBadgeEl.textContent = '0 images';
        virtualGrid.recalculateLayout();
        virtualGrid.renderVisible(true);
      }
    }
  }

  function updateFooterSelection(selectedIds = []) {
    const total = imageSource ? imageSource.total : 0;
    const count = selectedIds.length;
    selectionCountEl.textContent = `${count} of ${total.toLocaleString()} selected`;
    clearBtn.disabled = count === 0;
    previewBtn.disabled = total === 0;
    selectAllBtn.disabled = total === 0;
  }

  async function setFilter(newFilterParams = {}, newTitle = null) {
    if (detailsPanel && detailsPanel.isDirty()) {
      const action = await dialog.confirmThree({
        title: 'Unsaved Changes',
        message: 'You have unsaved changes. Do you want to save them before changing view?',
        saveText: 'Save',
        discardText: 'Discard',
        cancelText: 'Cancel'
      });
      if (action === 'save') {
        const saved = await detailsPanel.save();
        if (!saved) return;
      } else if (action === 'discard') {
        detailsPanel.revert();
      } else {
        return;
      }
    }

    filterParams = { ...newFilterParams };
    if (newTitle !== null) {
      currentTitle = newTitle;
      titleEl.textContent = newTitle;
    }
    selectionModel.clear();
    loadData();
  }

  function refresh() {
    loadData();
  }

  // Initial load
  loadData();

  return {
    element: container,
    setFilter,
    refresh,
    hasUnsavedChanges: () => detailsPanel && detailsPanel.isDirty(),
    getSelectionModel: () => selectionModel,
    getImageSource: () => imageSource,
    getVirtualGrid: () => virtualGrid,
    getDetailsPanel: () => detailsPanel,
    getPreviewOverlay: () => previewOverlay
  };
}

