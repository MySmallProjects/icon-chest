/**
 * Tag Combobox Model.
 * Pure logic for filtering, ranking, and tag creation validation.
 */

/**
 * Validates a tag name for creation.
 * Rules: non-empty, 1-50 characters after trimming, no control characters (\x00-\x1F, \x7F-\x9F).
 */
export function validateTagName(name) {
  if (typeof name !== 'string') {
    return { valid: false, error: 'Tag name must be a string' };
  }
  const trimmed = name.trim();
  if (trimmed.length === 0) {
    return { valid: false, error: 'Tag name cannot be empty' };
  }
  if (trimmed.length > 50) {
    return { valid: false, error: 'Tag name cannot exceed 50 characters' };
  }
  // Control characters check
  if (/[\x00-\x1F\x7F-\x9F]/.test(trimmed)) {
    return { valid: false, error: 'Tag name cannot contain control characters' };
  }
  return { valid: true, name: trimmed };
}

/**
 * Filters and ranks tags matching a query string.
 * Ranking:
 * 1. Prefix matches (alphabetical)
 * 2. Substring matches (alphabetical)
 * Uses Unicode-aware case folding (localeCompare with sensitivity 'base' or toLowerCase()).
 */
export function filterAndRankTags(tags, query = '') {
  if (!Array.isArray(tags)) return [];
  const q = (query || '').trim().toLowerCase();

  if (!q) {
    // Return all tags sorted alphabetically
    return [...tags].sort((a, b) => (a.name || '').localeCompare(b.name || '', undefined, { sensitivity: 'base' }));
  }

  const prefixMatches = [];
  const substringMatches = [];

  for (const tag of tags) {
    const tagName = (tag.name || '').toLowerCase();
    if (tagName.startsWith(q)) {
      prefixMatches.push(tag);
    } else if (tagName.includes(q)) {
      substringMatches.push(tag);
    }
  }

  const sorter = (a, b) => (a.name || '').localeCompare(b.name || '', undefined, { sensitivity: 'base' });
  prefixMatches.sort(sorter);
  substringMatches.sort(sorter);

  return [...prefixMatches, ...substringMatches];
}

/**
 * Evaluates whether a "Create 'name'" option should be offered.
 * Shown only when no tag matches case-insensitively and the name is valid.
 * If an exact match exists, canCreate is false, and exactMatchTag is returned.
 */
export function evaluateCreateOption(tags, query = '') {
  const validation = validateTagName(query);
  if (!validation.valid) {
    return {
      canCreate: false,
      error: validation.error,
      exactMatchTag: null,
      name: ''
    };
  }

  const trimmed = validation.name;
  const lower = trimmed.toLowerCase();

  const exactMatch = (tags || []).find(t => (t.name || '').toLowerCase() === lower);
  if (exactMatch) {
    return {
      canCreate: false,
      exactMatchTag: exactMatch,
      name: trimmed
    };
  }

  return {
    canCreate: true,
    exactMatchTag: null,
    name: trimmed
  };
}

