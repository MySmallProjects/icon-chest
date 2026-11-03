/**
 * Pure logic and state management for the Advanced Query Box.
 * Manages live validation requests, debouncing, stale response rejection,
 * diagnostic text ranges, and recent queries storage.
 */

export const MAX_RECENT_QUERIES = 20;
export const RECENT_QUERIES_STORAGE_KEY = 'iconchest_recent_queries';

export class QueryBoxModel {
  /**
   * @param {Object} options
   * @param {Function} options.validateFn - async (text) => { valid, errors, normalized, mentionsHidden, mentionsMissing }
   * @param {Storage} [options.storage] - localStorage or mock
   * @param {number} [options.debounceMs] - Debounce delay in ms (default 250)
   * @param {Function} [options.onStateChange] - callback (state) => void
   */
  constructor(options = {}) {
    this.validateFn = options.validateFn || (async () => ({ valid: true, errors: [] }));
    this.storage = options.storage || (typeof localStorage !== 'undefined' ? localStorage : null);
    this.debounceMs = typeof options.debounceMs === 'number' ? options.debounceMs : 250;
    this.onStateChange = options.onStateChange || (() => {});

    this.text = '';
    this.isValidating = false;
    this.isValid = true;
    this.errors = [];
    this.normalized = '';
    this.mentionsHidden = false;
    this.mentionsMissing = false;

    this._seq = 0;
    this._debounceTimer = null;
  }

  /**
   * Updates the query text and schedules validation.
   */
  setText(newText, immediate = false) {
    this.text = newText;

    if (this._debounceTimer) {
      clearTimeout(this._debounceTimer);
      this._debounceTimer = null;
    }

    if (!newText.trim()) {
      this.isValidating = false;
      this.isValid = true;
      this.errors = [];
      this.normalized = '';
      this.mentionsHidden = false;
      this.mentionsMissing = false;
      this._notify();
      return;
    }

    this.isValidating = true;
    this._notify();

    if (immediate || this.debounceMs <= 0) {
      this._runValidation(newText);
    } else {
      this._debounceTimer = setTimeout(() => {
        this._runValidation(newText);
      }, this.debounceMs);
    }
  }

  async _runValidation(queryText) {
    const currentSeq = ++this._seq;

    try {
      const res = await this.validateFn(queryText);

      // Ignore stale responses
      if (currentSeq !== this._seq) return;

      this.isValidating = false;
      this.isValid = Boolean(res?.valid);
      this.errors = Array.isArray(res?.errors) ? res.errors : [];
      this.normalized = res?.normalized || '';
      this.mentionsHidden = Boolean(res?.mentionsHidden);
      this.mentionsMissing = Boolean(res?.mentionsMissing);
    } catch (err) {
      if (currentSeq !== this._seq) return;
      this.isValidating = false;
      this.isValid = false;
      this.errors = [{ message: err.message || 'Validation request failed', position: 0, length: 0 }];
    }

    this._notify();
  }

  _notify() {
    this.onStateChange({
      text: this.text,
      isValidating: this.isValidating,
      isValid: this.isValid,
      errors: this.errors,
      normalized: this.normalized,
      mentionsHidden: this.mentionsHidden,
      mentionsMissing: this.mentionsMissing
    });
  }

  /**
   * Maps an error's position and length to a UTF-16 character selection range [start, end].
   */
  getErrorSelectionRange(error) {
    if (!error) return [0, 0];
    const pos = Math.max(0, error.position || 0);
    const len = Math.max(1, error.length || 1);
    const textLen = this.text.length;

    const start = Math.min(pos, textLen);
    const end = Math.min(start + len, textLen);
    return [start, end];
  }

  /**
   * Loads the recent queries list from storage.
   */
  getRecentQueries() {
    if (!this.storage) return [];
    try {
      const raw = this.storage.getItem(RECENT_QUERIES_STORAGE_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  /**
   * Adds a query to recent queries, deduplicates, and caps at MAX_RECENT_QUERIES.
   */
  addRecentQuery(query) {
    if (!query || !query.trim() || !this.storage) return;
    const clean = query.trim();

    let list = this.getRecentQueries();
    // Remove duplicate
    list = list.filter(q => q.toLowerCase() !== clean.toLowerCase());
    // Prepend newest
    list.unshift(clean);
    // Cap at 20
    if (list.length > MAX_RECENT_QUERIES) {
      list = list.slice(0, MAX_RECENT_QUERIES);
    }

    try {
      this.storage.setItem(RECENT_QUERIES_STORAGE_KEY, JSON.stringify(list));
    } catch {
      // Storage full or restricted
    }
  }

  /**
   * Clears recent queries from storage.
   */
  clearRecentQueries() {
    if (!this.storage) return;
    try {
      this.storage.removeItem(RECENT_QUERIES_STORAGE_KEY);
    } catch {}
  }
}

