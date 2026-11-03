/**
 * Pure logic for the Simple Filter Model.
 * Handles state management, parameter conversion, date presets,
 * active filter chip computation, and dirty detection.
 */

export const DATE_PRESETS = [
  { id: 'all', label: 'All time' },
  { id: 'today', label: 'Today' },
  { id: 'yesterday', label: 'Yesterday' },
  { id: 'last7days', label: 'Last 7 days' },
  { id: 'last30days', label: 'Last 30 days' },
  { id: 'thisMonth', label: 'This month' },
  { id: 'lastMonth', label: 'Last month' },
  { id: 'thisYear', label: 'This year' },
  { id: 'lastYear', label: 'Last year' },
  { id: 'custom', label: 'Custom range...' }
];

export const RANGE_FIELDS = [
  { id: 'effective', label: 'Effective date' },
  { id: 'created', label: 'Date created' },
  { id: 'modified', label: 'Date modified' },
  { id: 'taken', label: 'Date taken' },
  { id: 'added', label: 'Date added' }
];

export const RATING_PRESETS = [
  { id: '', label: 'Any rating' },
  { id: 'unrated', label: 'Unrated' },
  { id: '1', label: '★☆☆☆☆ and up (1+)' },
  { id: '2', label: '★★☆☆☆ and up (2+)' },
  { id: '3', label: '★★★☆☆ and up (3+)' },
  { id: '4', label: '★★★★☆ and up (4+)' },
  { id: '5', label: '★★★★★ only (5)' }
];

/**
 * Returns a pristine, empty Simple filter state.
 */
export function createDefaultFilterState() {
  return {
    text: '',
    types: [],            // string[] e.g. ['png', 'svg']
    filterFolderId: null, // number | null
    filterTags: [],       // string[]
    filterTagMode: 'any', // 'any' | 'all'
    untagged: false,      // boolean
    rangeField: 'effective',
    datePreset: 'all',    // 'all' | 'today' | 'yesterday' | ... | 'custom'
    dateFrom: '',         // 'YYYY-MM-DD'
    dateTo: '',           // 'YYYY-MM-DD'
    ratingChoice: '',     // '' | 'unrated' | '1' | '2' | '3' | '4' | '5'
    minWidth: null,
    maxWidth: null,
    minHeight: null,
    maxHeight: null,
    minSize: null,        // bytes
    maxSize: null,        // bytes
    favourite: null,      // boolean | null
    hidden: 'exclude',    // 'exclude' | 'only' | 'include'
    duplicate: 'exclude'  // 'exclude' | 'only' | 'include'
  };
}

/**
 * Pads a number to two digits.
 */
function pad2(n) {
  return n < 10 ? '0' + n : String(n);
}

/**
 * Formats a Date object to YYYY-MM-DD using local time.
 */
export function formatDate(d) {
  if (!d || isNaN(d.getTime())) return '';
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
}

/**
 * Computes dateFrom and dateTo (YYYY-MM-DD) for a given preset.
 * Clock parameter defaults to new Date().
 */
export function computeDatePreset(presetId, clock = new Date()) {
  const now = new Date(clock.getTime());
  const y = now.getFullYear();
  const m = now.getMonth();
  const d = now.getDate();

  switch (presetId) {
    case 'today': {
      const todayStr = formatDate(now);
      return { dateFrom: todayStr, dateTo: todayStr };
    }
    case 'yesterday': {
      const yDate = new Date(y, m, d - 1);
      const yStr = formatDate(yDate);
      return { dateFrom: yStr, dateTo: yStr };
    }
    case 'last7days': {
      const fromDate = new Date(y, m, d - 6);
      return { dateFrom: formatDate(fromDate), dateTo: formatDate(now) };
    }
    case 'last30days': {
      const fromDate = new Date(y, m, d - 29);
      return { dateFrom: formatDate(fromDate), dateTo: formatDate(now) };
    }
    case 'thisMonth': {
      const firstDay = new Date(y, m, 1);
      const lastDay = new Date(y, m + 1, 0);
      return { dateFrom: formatDate(firstDay), dateTo: formatDate(lastDay) };
    }
    case 'lastMonth': {
      const firstDay = new Date(y, m - 1, 1);
      const lastDay = new Date(y, m, 0);
      return { dateFrom: formatDate(firstDay), dateTo: formatDate(lastDay) };
    }
    case 'thisYear': {
      const firstDay = new Date(y, 0, 1);
      const lastDay = new Date(y, 11, 31);
      return { dateFrom: formatDate(firstDay), dateTo: formatDate(lastDay) };
    }
    case 'lastYear': {
      const firstDay = new Date(y - 1, 0, 1);
      const lastDay = new Date(y - 1, 11, 31);
      return { dateFrom: formatDate(firstDay), dateTo: formatDate(lastDay) };
    }
    default:
      return { dateFrom: '', dateTo: '' };
  }
}

/**
 * Checks if two states differ (dirty check).
 */
export function isFilterDirty(a, b) {
  if (!a || !b) return false;

  if ((a.text || '').trim() !== (b.text || '').trim()) return true;
  if ((a.filterFolderId || null) !== (b.filterFolderId || null)) return true;
  if ((a.untagged || false) !== (b.untagged || false)) return true;
  if ((a.filterTagMode || 'any') !== (b.filterTagMode || 'any')) return true;
  if ((a.rangeField || 'effective') !== (b.rangeField || 'effective')) return true;
  if ((a.datePreset || 'all') !== (b.datePreset || 'all')) return true;
  if ((a.dateFrom || '') !== (b.dateFrom || '')) return true;
  if ((a.dateTo || '') !== (b.dateTo || '')) return true;
  if ((a.ratingChoice || '') !== (b.ratingChoice || '')) return true;
  if ((a.minWidth || null) !== (b.minWidth || null)) return true;
  if ((a.maxWidth || null) !== (b.maxWidth || null)) return true;
  if ((a.minHeight || null) !== (b.minHeight || null)) return true;
  if ((a.maxHeight || null) !== (b.maxHeight || null)) return true;
  if ((a.minSize || null) !== (b.minSize || null)) return true;
  if ((a.maxSize || null) !== (b.maxSize || null)) return true;
  if ((a.favourite || null) !== (b.favourite || null)) return true;
  if ((a.hidden || 'exclude') !== (b.hidden || 'exclude')) return true;
  if ((a.duplicate || 'exclude') !== (b.duplicate || 'exclude')) return true;

  // Compare arrays
  const typesA = (a.types || []).slice().sort().join(',');
  const typesB = (b.types || []).slice().sort().join(',');
  if (typesA !== typesB) return true;

  const tagsA = (a.filterTags || []).slice().sort().join(',');
  const tagsB = (b.filterTags || []).slice().sort().join(',');
  if (tagsA !== tagsB) return true;

  return false;
}

/**
 * Converts a Simple filter state to API search parameters.
 */
export function filterStateToParams(state) {
  const params = {};

  if (state.text && state.text.trim()) {
    params.text = state.text.trim();
  }

  if (state.types && state.types.length > 0) {
    params.type = state.types.map(t => t.toLowerCase());
  }

  if (state.filterFolderId) {
    params.filterFolderId = state.filterFolderId;
  }

  if (state.untagged) {
    params.untagged = true;
  } else if (state.filterTags && state.filterTags.length > 0) {
    params.filterTag = state.filterTags.slice();
    if (state.filterTagMode === 'all') {
      params.filterTagMode = 'all';
    }
  }

  if (state.dateFrom || state.dateTo) {
    if (state.rangeField && state.rangeField !== 'effective') {
      params.rangeField = state.rangeField;
    }
    if (state.dateFrom) params.dateFrom = state.dateFrom;
    if (state.dateTo) params.dateTo = state.dateTo;
  }

  if (state.ratingChoice) {
    if (state.ratingChoice === 'unrated') {
      params.ratingMax = 0;
    } else {
      const r = parseInt(state.ratingChoice, 10);
      if (!isNaN(r) && r >= 1 && r <= 5) {
        if (r === 5) {
          params.ratingMin = 5;
          params.ratingMax = 5;
        } else {
          params.ratingMin = r;
        }
      }
    }
  }

  if (typeof state.minWidth === 'number' && !isNaN(state.minWidth)) params.minWidth = state.minWidth;
  if (typeof state.maxWidth === 'number' && !isNaN(state.maxWidth)) params.maxWidth = state.maxWidth;
  if (typeof state.minHeight === 'number' && !isNaN(state.minHeight)) params.minHeight = state.minHeight;
  if (typeof state.maxHeight === 'number' && !isNaN(state.maxHeight)) params.maxHeight = state.maxHeight;
  if (typeof state.minSize === 'number' && !isNaN(state.minSize)) params.minSize = state.minSize;
  if (typeof state.maxSize === 'number' && !isNaN(state.maxSize)) params.maxSize = state.maxSize;

  if (state.favourite !== null && state.favourite !== undefined) {
    params.favourite = Boolean(state.favourite);
  }

  if (state.hidden && state.hidden !== 'exclude') {
    params.hidden = state.hidden;
  }

  if (state.duplicate && state.duplicate !== 'exclude') {
    params.duplicate = state.duplicate;
  }

  return params;
}

/**
 * Returns a list of active filter chips with removal callbacks.
 * Each chip: { id, label, remove(state) => newState }
 */
export function getActiveFilterChips(state, options = {}) {
  const chips = [];
  if (!state) return chips;

  // 1. Text
  if (state.text && state.text.trim()) {
    chips.push({
      id: 'text',
      label: `Text: "${state.text.trim()}"`,
      remove: (s) => ({ ...s, text: '' })
    });
  }

  // 2. Types/Formats
  if (state.types && state.types.length > 0) {
    const list = state.types.map(t => t.toUpperCase()).join(', ');
    chips.push({
      id: 'types',
      label: `Format: ${list}`,
      remove: (s) => ({ ...s, types: [] })
    });
  }

  // 3. Folder
  if (state.filterFolderId) {
    const folderName = options.folderNames?.[state.filterFolderId] || `Folder #${state.filterFolderId}`;
    chips.push({
      id: 'folder',
      label: `Folder: ${folderName}`,
      remove: (s) => ({ ...s, filterFolderId: null })
    });
  }

  // 4. Untagged
  if (state.untagged) {
    chips.push({
      id: 'untagged',
      label: 'Untagged',
      remove: (s) => ({ ...s, untagged: false })
    });
  } else if (state.filterTags && state.filterTags.length > 0) {
    const mode = state.filterTagMode === 'all' ? 'all' : 'any';
    chips.push({
      id: 'tags',
      label: `Tags (${mode}): ${state.filterTags.join(', ')}`,
      remove: (s) => ({ ...s, filterTags: [], filterTagMode: 'any' })
    });
  }

  // 5. Date Range
  if (state.dateFrom || state.dateTo) {
    let dateLabel = '';
    if (state.datePreset && state.datePreset !== 'custom' && state.datePreset !== 'all') {
      const p = DATE_PRESETS.find(x => x.id === state.datePreset);
      dateLabel = p ? p.label : state.datePreset;
    } else if (state.dateFrom && state.dateTo) {
      dateLabel = `${state.dateFrom} to ${state.dateTo}`;
    } else if (state.dateFrom) {
      dateLabel = `From ${state.dateFrom}`;
    } else {
      dateLabel = `To ${state.dateTo}`;
    }

    const fieldLabel = state.rangeField && state.rangeField !== 'effective'
      ? ` (${state.rangeField})`
      : '';

    chips.push({
      id: 'date',
      label: `Date: ${dateLabel}${fieldLabel}`,
      remove: (s) => ({ ...s, datePreset: 'all', dateFrom: '', dateTo: '' })
    });
  }

  // 6. Rating
  if (state.ratingChoice) {
    let ratingLabel = '';
    if (state.ratingChoice === 'unrated') {
      ratingLabel = 'Unrated';
    } else if (state.ratingChoice === '5') {
      ratingLabel = 'Rating: 5★';
    } else {
      ratingLabel = `Rating: ${state.ratingChoice}★+`;
    }
    chips.push({
      id: 'rating',
      label: ratingLabel,
      remove: (s) => ({ ...s, ratingChoice: '' })
    });
  }

  // 7. Dimensions / Size
  if (state.minWidth || state.maxWidth || state.minHeight || state.maxHeight) {
    const parts = [];
    if (state.minWidth) parts.push(`w ≥ ${state.minWidth}`);
    if (state.maxWidth) parts.push(`w ≤ ${state.maxWidth}`);
    if (state.minHeight) parts.push(`h ≥ ${state.minHeight}`);
    if (state.maxHeight) parts.push(`h ≤ ${state.maxHeight}`);
    chips.push({
      id: 'dimensions',
      label: `Dim: ${parts.join(', ')}`,
      remove: (s) => ({ ...s, minWidth: null, maxWidth: null, minHeight: null, maxHeight: null })
    });
  }

  if (state.minSize || state.maxSize) {
    const parts = [];
    if (state.minSize) parts.push(`≥ ${formatBytes(state.minSize)}`);
    if (state.maxSize) parts.push(`≤ ${formatBytes(state.maxSize)}`);
    chips.push({
      id: 'size',
      label: `Size: ${parts.join(', ')}`,
      remove: (s) => ({ ...s, minSize: null, maxSize: null })
    });
  }

  // 8. Favourite
  if (state.favourite !== null && state.favourite !== undefined) {
    chips.push({
      id: 'favourite',
      label: state.favourite ? 'Favourites only' : 'Non-favourites',
      remove: (s) => ({ ...s, favourite: null })
    });
  }

  // 9. Hidden
  if (state.hidden && state.hidden !== 'exclude') {
    chips.push({
      id: 'hidden',
      label: `Hidden: ${state.hidden}`,
      remove: (s) => ({ ...s, hidden: 'exclude' })
    });
  }

  // 10. Duplicate
  if (state.duplicate && state.duplicate !== 'exclude') {
    chips.push({
      id: 'duplicate',
      label: `Duplicate: ${state.duplicate}`,
      remove: (s) => ({ ...s, duplicate: 'exclude' })
    });
  }

  return chips;
}

function formatBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * Returns which filter controls are visible for a specific browser view.
 */
export function getVisibleControlsForView(viewId) {
  return {
    showFolder: viewId !== 'folders',
    showTags: viewId !== 'tags',
    showDate: viewId !== 'years',
    showFavourite: viewId !== 'favourites',
    showDuplicate: viewId !== 'duplicates',
    showHidden: viewId !== 'missing'
  };
}

