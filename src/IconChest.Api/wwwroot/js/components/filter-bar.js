/**
 * Comprehensive Filter Bar Component for IconChest.
 * Supports Simple mode (structured dropdowns, presets, text search)
 * and Advanced mode (query language with live syntax validation).
 */

import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import {
  DATE_PRESETS,
  RANGE_FIELDS,
  RATING_PRESETS,
  createDefaultFilterState,
  computeDatePreset,
  isFilterDirty,
  filterStateToParams,
  getActiveFilterChips,
  getVisibleControlsForView
} from '../lib/filter-model.js';
import { QueryBox } from './query-box.js';
import { ActiveFiltersBar } from './active-filters.js';

const ICON_SEARCH = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z"/></svg>';
const ICON_CONVERT = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M16 17.01V10h-2v7.01h-3L15 21l4-3.99h-3zM9 3L5 6.99h3V14h2V6.99h3L9 3z"/></svg>';
const ICON_CLOSE = '<svg viewBox="0 0 24 24" width="12" height="12"><path fill="currentColor" d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>';

export class FilterBar {
  /**
   * @param {Object} options
   * @param {string} options.viewId - e.g. 'folders', 'years', 'tags', 'favourites', etc.
   * @param {Function} options.onFilterChanged - callback (params, isAdvanced, queryText) => void
   * @param {Function} [options.onNavigateHelp] - callback () => void
   * @param {Function} [options.onClose] - callback () => void
   */
  constructor(options = {}) {
    this.viewId = options.viewId || 'folders';
    this.onFilterChanged = options.onFilterChanged || (() => {});
    this.onNavigateHelp = options.onNavigateHelp;
    this.onClose = options.onClose;

    this.activeMode = 'simple'; // 'simple' | 'advanced'
    this.pendingState = createDefaultFilterState();
    this.appliedState = createDefaultFilterState();
    this.appliedQuery = '';

    // Cache of filter options from /api/filters/options
    this.filterOptions = { formats: [], folders: [], minDate: null, maxDate: null };
    this.allTags = [];

    this._buildUI();
    this._loadFilterOptions();
    this._loadTags();
  }

  _buildUI() {
    this.containerEl = el('div', { className: 'filter-bar-container', 'aria-label': 'Filter bar' });

    // Mode tabs: Simple & Advanced
    this.simpleTabBtn = el('button', {
      type: 'button',
      role: 'tab',
      id: 'tab-filter-simple',
      className: 'filter-tab-btn active',
      'aria-selected': 'true',
      'aria-controls': 'panel-filter-simple',
      onClick: () => this.switchMode('simple')
    }, 'Simple');

    this.advancedTabBtn = el('button', {
      type: 'button',
      role: 'tab',
      id: 'tab-filter-advanced',
      className: 'filter-tab-btn',
      'aria-selected': 'false',
      'aria-controls': 'panel-filter-advanced',
      onClick: () => this.switchMode('advanced')
    }, 'Advanced');

    this.tabList = el('div', {
      className: 'filter-tab-list',
      role: 'tablist',
      'aria-label': 'Filter mode'
    }, [
      this.simpleTabBtn,
      this.advancedTabBtn
    ]);

    this.closeBtn = el('button', {
      type: 'button',
      className: 'filter-close-btn',
      title: 'Hide Filter Bar (/)',
      'aria-label': 'Hide Filter Bar',
      onClick: () => {
        if (this.onClose) {
          this.onClose();
        } else {
          this.setVisible(false);
        }
      }
    }, [
      createSvg(ICON_CLOSE),
      el('span', {}, 'Hide')
    ]);

    const header = el('div', { className: 'filter-bar-header' }, [
      this.tabList,
      this.closeBtn
    ]);

    // Simple Panel
    this.simplePanelEl = el('div', {
      id: 'panel-filter-simple',
      role: 'tabpanel',
      className: 'filter-panel simple-filter-panel',
      'aria-labelledby': 'tab-filter-simple'
    });
    this.simplePanelEl.hidden = false;

    // Advanced Panel
    this.queryBox = new QueryBox({
      onRunQuery: (queryText) => {
        this.appliedQuery = queryText;
        this.onFilterChanged({ q: queryText }, true, queryText);
        this._updateActiveChips();
      },
      onClear: () => {
        this.appliedQuery = '';
        this.onFilterChanged({}, true, '');
        this._updateActiveChips();
      },
      onNavigateHelp: this.onNavigateHelp
    });

    this.advancedPanelEl = el('div', {
      id: 'panel-filter-advanced',
      role: 'tabpanel',
      className: 'filter-panel advanced-filter-panel hidden',
      'aria-labelledby': 'tab-filter-advanced'
    }, [
      this.queryBox.containerEl
    ]);
    this.advancedPanelEl.hidden = true;

    // Active filters chips
    this.activeFiltersBar = new ActiveFiltersBar({
      onRemoveChip: (chip) => {
        if (this.activeMode === 'simple') {
          this.pendingState = chip.remove(this.pendingState);
          this.appliedState = { ...this.pendingState };
          this._syncControlsFromState();
          this.applySimpleFilter();
        } else {
          this.queryBox.clear();
        }
      },
      onClearAll: () => {
        if (this.activeMode === 'simple') {
          this.clearSimpleFilter();
        } else {
          this.queryBox.clear();
        }
      }
    });

    this._buildSimpleControls();

    this.containerEl.appendChild(header);
    this.containerEl.appendChild(this.simplePanelEl);
    this.containerEl.appendChild(this.advancedPanelEl);
    this.containerEl.appendChild(this.activeFiltersBar.containerEl);
  }

  setVisible(visible) {
    this.containerEl.hidden = !visible;
    this.containerEl.classList.toggle('hidden', !visible);
  }

  isVisible() {
    return !this.containerEl.hidden && !this.containerEl.classList.contains('hidden');
  }

  toggleVisibility() {
    const next = !this.isVisible();
    this.setVisible(next);
    return next;
  }

  _buildSimpleControls() {
    clear(this.simplePanelEl);

    const vis = getVisibleControlsForView(this.viewId);

    // 1. Text input
    this.textInput = el('input', {
      type: 'text',
      className: 'input input-sm filter-text-input',
      placeholder: 'Filter text (words ANDed)...',
      'aria-label': 'Filter text',
      value: this.pendingState.text || '',
      onInput: (e) => {
        this.pendingState.text = e.target.value;
        this._updateDirtyState();
      },
      onKeyDown: (e) => {
        if (e.key === 'Enter') {
          e.preventDefault();
          this.applySimpleFilter();
        } else if (e.key === 'Escape') {
          if (this.textInput.value) {
            e.preventDefault();
            this.textInput.value = '';
            this.pendingState.text = '';
            this._updateDirtyState();
          } else {
            this.textInput.blur();
          }
        }
      }
    });

    // 2. Format select
    this.formatSelect = el('select', {
      className: 'select select-sm filter-select',
      'aria-label': 'Filter by format',
      onChange: (e) => {
        this.pendingState.types = e.target.value ? [e.target.value] : [];
        this.applySimpleFilter();
      }
    }, [
      el('option', { value: '' }, 'All formats')
    ]);

    // 3. Tag select & untagged checkbox
    this.tagSelect = el('select', {
      className: 'select select-sm filter-select',
      'aria-label': 'Filter by tag',
      onChange: (e) => {
        this.pendingState.filterTags = e.target.value ? [e.target.value] : [];
        if (e.target.value) this.pendingState.untagged = false;
        this.applySimpleFilter();
      }
    }, [
      el('option', { value: '' }, 'All tags')
    ]);

    this.untaggedCheckbox = el('input', {
      type: 'checkbox',
      id: 'filter-untagged-chk',
      className: 'checkbox filter-untagged-chk',
      onChange: (e) => {
        this.pendingState.untagged = e.target.checked;
        if (e.target.checked) {
          this.pendingState.filterTags = [];
          this.tagSelect.value = '';
        }
        this.applySimpleFilter();
      }
    });

    const untaggedLabel = el('label', {
      htmlFor: 'filter-untagged-chk',
      className: 'filter-checkbox-label'
    }, [
      this.untaggedCheckbox,
      el('span', {}, 'Untagged')
    ]);

    // 4. Date Preset & Range
    this.datePresetSelect = el('select', {
      className: 'select select-sm filter-select',
      'aria-label': 'Date range preset',
      onChange: (e) => {
        const val = e.target.value;
        this.pendingState.datePreset = val;
        if (val !== 'custom') {
          const { dateFrom, dateTo } = computeDatePreset(val);
          this.pendingState.dateFrom = dateFrom;
          this.pendingState.dateTo = dateTo;
          this.customDateWrap.classList.add('hidden');
          this.customDateWrap.hidden = true;
          this.applySimpleFilter();
        } else {
          this.customDateWrap.classList.remove('hidden');
          this.customDateWrap.hidden = false;
          this._updateDirtyState();
        }
      }
    }, DATE_PRESETS.map(p => el('option', { value: p.id }, p.label)));

    this.rangeFieldSelect = el('select', {
      className: 'select select-sm filter-select',
      'aria-label': 'Date field',
      onChange: (e) => {
        this.pendingState.rangeField = e.target.value;
        if (this.pendingState.dateFrom || this.pendingState.dateTo) {
          this.applySimpleFilter();
        } else {
          this._updateDirtyState();
        }
      }
    }, RANGE_FIELDS.map(f => el('option', { value: f.id }, f.label)));

    this.dateFromInput = el('input', {
      type: 'text',
      className: 'input input-sm filter-date-input',
      placeholder: 'YYYY-MM-DD',
      'aria-label': 'Start date',
      onInput: (e) => {
        this.pendingState.dateFrom = e.target.value.trim();
        this._updateDirtyState();
      }
    });

    this.dateToInput = el('input', {
      type: 'text',
      className: 'input input-sm filter-date-input',
      placeholder: 'YYYY-MM-DD',
      'aria-label': 'End date',
      onInput: (e) => {
        this.pendingState.dateTo = e.target.value.trim();
        this._updateDirtyState();
      }
    });

    this.customDateWrap = el('div', { className: 'custom-date-wrap hidden' }, [
      this.dateFromInput,
      el('span', { className: 'date-sep' }, 'to'),
      this.dateToInput
    ]);
    this.customDateWrap.hidden = true;

    // 5. Rating Select
    this.ratingSelect = el('select', {
      className: 'select select-sm filter-select',
      'aria-label': 'Rating filter',
      onChange: (e) => {
        this.pendingState.ratingChoice = e.target.value;
        this.applySimpleFilter();
      }
    }, RATING_PRESETS.map(r => el('option', { value: r.id }, r.label)));

    // 6. Action buttons
    this.applyBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm btn-primary filter-apply-btn',
      onClick: () => this.applySimpleFilter()
    }, [
      createSvg(ICON_SEARCH),
      el('span', {}, 'Filter')
    ]);

    this.clearBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm filter-clear-btn',
      title: 'Reset filters',
      onClick: () => this.clearSimpleFilter()
    }, 'Clear');

    this.convertBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm filter-convert-btn',
      title: 'Convert simple filters to query language',
      onClick: () => this.convertToQuery()
    }, [
      createSvg(ICON_CONVERT),
      el('span', {}, 'Convert to query')
    ]);

    // Assemble controls row
    const controlsRow = el('div', { className: 'filter-controls-row' }, [
      this.textInput,
      this.formatSelect,
      vis.showTags ? this.tagSelect : el('span'),
      vis.showTags ? untaggedLabel : el('span'),
      vis.showDate ? this.datePresetSelect : el('span'),
      vis.showDate ? this.rangeFieldSelect : el('span'),
      vis.showDate ? this.customDateWrap : el('span'),
      this.ratingSelect,
      el('div', { className: 'filter-actions-group' }, [
        this.applyBtn,
        this.clearBtn,
        this.convertBtn
      ])
    ]);

    this.simplePanelEl.appendChild(controlsRow);
  }

  async _loadFilterOptions() {
    try {
      const opts = await api.getFilterOptions();
      if (opts) {
        this.filterOptions = opts;

        // Populate format dropdown
        if (Array.isArray(opts.formats) && this.formatSelect) {
          clear(this.formatSelect);
          this.formatSelect.appendChild(el('option', { value: '' }, 'All formats'));
          for (const f of opts.formats) {
            const label = `${f.format.toUpperCase()} (${f.count})`;
            this.formatSelect.appendChild(el('option', { value: f.format }, label));
          }
        }
      }
    } catch {
      // Offline or error
    }
  }

  async _loadTags() {
    try {
      const tags = await api.get('/api/tags');
      if (Array.isArray(tags) && this.tagSelect) {
        this.allTags = tags;
        clear(this.tagSelect);
        this.tagSelect.appendChild(el('option', { value: '' }, 'All tags'));
        for (const t of tags) {
          this.tagSelect.appendChild(el('option', { value: t.name }, t.name));
        }
      }
    } catch {
      // Offline or error
    }
  }

  _updateDirtyState() {
    const dirty = isFilterDirty(this.pendingState, this.appliedState);
    if (dirty) {
      this.applyBtn.classList.add('btn-dirty');
      this.applyBtn.title = 'Filters modified — click to apply';
    } else {
      this.applyBtn.classList.remove('btn-dirty');
      this.applyBtn.title = 'Filter';
    }
  }

  _syncControlsFromState() {
    if (this.textInput) this.textInput.value = this.pendingState.text || '';
    if (this.formatSelect) this.formatSelect.value = (this.pendingState.types?.[0] || '').toLowerCase();
    if (this.tagSelect) this.tagSelect.value = this.pendingState.filterTags?.[0] || '';
    if (this.untaggedCheckbox) this.untaggedCheckbox.checked = Boolean(this.pendingState.untagged);
    if (this.datePresetSelect) this.datePresetSelect.value = this.pendingState.datePreset || 'all';
    if (this.rangeFieldSelect) this.rangeFieldSelect.value = this.pendingState.rangeField || 'effective';
    if (this.dateFromInput) this.dateFromInput.value = this.pendingState.dateFrom || '';
    if (this.dateToInput) this.dateToInput.value = this.pendingState.dateTo || '';
    if (this.ratingSelect) this.ratingSelect.value = this.pendingState.ratingChoice || '';

    if (this.pendingState.datePreset === 'custom') {
      this.customDateWrap.classList.remove('hidden');
    } else {
      this.customDateWrap.classList.add('hidden');
    }

    this._updateDirtyState();
  }

  applySimpleFilter() {
    this.appliedState = { ...this.pendingState };
    this._updateDirtyState();

    const params = filterStateToParams(this.appliedState);
    this.onFilterChanged(params, false, null);
    this._updateActiveChips();
  }

  clearSimpleFilter() {
    this.pendingState = createDefaultFilterState();
    this.appliedState = createDefaultFilterState();
    this._syncControlsFromState();
    this.onFilterChanged({}, false, null);
    this._updateActiveChips();
  }

  async convertToQuery() {
    const params = filterStateToParams(this.pendingState);

    // If empty filter, convert produces empty query
    const keys = Object.keys(params);
    if (keys.length === 0) {
      this.switchMode('advanced');
      this.queryBox.setQuery('');
      return;
    }

    try {
      const res = await api.convertFilterToQuery(params);
      if (res && res.success && res.text) {
        this.switchMode('advanced');
        this.queryBox.setQuery(res.text);
        toast.show({ message: `Filter converted: ${res.text}`, type: 'success' });
      } else {
        const reason = res?.unsupportedReason || 'Filter combination cannot be expressed as a query.';
        toast.show({ message: `Cannot convert to query: ${reason}`, type: 'error' });
      }
    } catch (err) {
      toast.show({ message: `Conversion failed: ${err.message}`, type: 'error' });
    }
  }

  switchMode(targetMode) {
    if (targetMode === this.activeMode) return;

    const prevMode = this.activeMode;
    const prevSimpleState = { ...this.appliedState };
    const prevQuery = this.appliedQuery;

    this.activeMode = targetMode;

    if (targetMode === 'simple') {
      this.simpleTabBtn.classList.add('active');
      this.simpleTabBtn.setAttribute('aria-selected', 'true');
      this.advancedTabBtn.classList.remove('active');
      this.advancedTabBtn.setAttribute('aria-selected', 'false');

      this.simplePanelEl.classList.remove('hidden');
      this.simplePanelEl.hidden = false;
      this.advancedPanelEl.classList.add('hidden');
      this.advancedPanelEl.hidden = true;

      // Check if previous advanced mode had a query
      if (prevQuery) {
        toast.show({
          message: 'Switched to Simple filter.',
          type: 'info',
          actionText: 'Undo',
          onAction: () => {
            this.switchMode('advanced');
            this.queryBox.setQuery(prevQuery);
            this.queryBox.run();
          }
        });
      }

      this.applySimpleFilter();
    } else {
      this.advancedTabBtn.classList.add('active');
      this.advancedTabBtn.setAttribute('aria-selected', 'true');
      this.simpleTabBtn.classList.remove('active');
      this.simpleTabBtn.setAttribute('aria-selected', 'false');

      this.advancedPanelEl.classList.remove('hidden');
      this.advancedPanelEl.hidden = false;
      this.simplePanelEl.classList.add('hidden');
      this.simplePanelEl.hidden = true;

      // Check if previous simple mode had active filters
      const prevParams = filterStateToParams(prevSimpleState);
      if (Object.keys(prevParams).length > 0) {
        toast.show({
          message: 'Switched to Advanced mode.',
          type: 'info',
          actionText: 'Undo',
          onAction: () => {
            this.switchMode('simple');
            this.pendingState = { ...prevSimpleState };
            this._syncControlsFromState();
            this.applySimpleFilter();
          }
        });
      }

      if (this.appliedQuery) {
        this.queryBox.setQuery(this.appliedQuery);
      }
    }

    this._updateActiveChips();
  }

  _updateActiveChips() {
    if (this.activeMode === 'simple') {
      const folderMap = {};
      for (const f of this.filterOptions.folders || []) {
        folderMap[f.id] = f.name || f.path;
      }
      const chips = getActiveFilterChips(this.appliedState, { folderNames: folderMap });
      this.activeFiltersBar.render(chips);
    } else {
      if (this.appliedQuery) {
        const queryChip = {
          id: 'query',
          label: `Query: ${this.appliedQuery}`,
          remove: () => {
            this.queryBox.clear();
          }
        };
        this.activeFiltersBar.render([queryChip]);
      } else {
        this.activeFiltersBar.render([]);
      }
    }
  }

  focus() {
    if (this.activeMode === 'simple') {
      if (this.textInput) {
        this.textInput.focus();
        this.textInput.select();
      }
    } else {
      this.queryBox.focus();
    }
  }
}

