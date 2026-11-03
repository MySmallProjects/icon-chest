/**
 * Edit Model for IconChest.
 * Pure logic module for single and multi-image edit state,
 * dirty field tracking, tri-state tag transitions, PATCH generation,
 * and validation.
 */

import { tagsStore } from './tags-store.js';

export function isLeapYear(year) {
  return (year % 4 === 0 && year % 100 !== 0) || (year % 400 === 0);
}

export function isValidDateString(dateStr) {
  if (!dateStr || typeof dateStr !== 'string') return false;
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateStr.trim());
  if (!match) return false;

  const year = parseInt(match[1], 10);
  const month = parseInt(match[2], 10);
  const day = parseInt(match[3], 10);

  if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31) {
    return false;
  }

  const daysInMonth = [0, 31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day <= daysInMonth[month];
}

export class EditModel {
  /**
   * Initializes edit model from a summary response or single image response.
   * @param {Object} summary - Aggregated summary or normalized single-image details.
   * @param {Array<string>} ids - Array of image IDs being edited.
   */
  constructor(summary = {}, ids = []) {
    this.ids = Array.isArray(ids) ? [...ids] : [];
    this.selectionCount = this.ids.length || summary.count || 1;

    // Build initial state
    this._initial = this._extractInitialState(summary);
    // Working state
    this._current = JSON.parse(JSON.stringify(this._initial));
    // Dirty flags
    this._dirty = {
      title: false,
      description: false,
      rating: false,
      takenOverrideDate: false,
      isHidden: false
    };

    // Tag states map: tagId -> { initial: 0|0.5|1, current: 0|0.5|1 }
    this._tagsState = new Map();
    this._initTagsState(summary);
  }

  _extractInitialState(summary) {
    // If single image details object
    const isSingle = (this.selectionCount === 1) || (summary.count === 1 && summary.id);

    const titleShared = typeof summary.titleShared === 'boolean'
      ? summary.titleShared
      : (typeof summary.hasMultipleTitles === 'boolean' ? !summary.hasMultipleTitles : isSingle);

    const descShared = typeof summary.descriptionShared === 'boolean'
      ? summary.descriptionShared
      : (typeof summary.hasMultipleDescriptions === 'boolean' ? !summary.hasMultipleDescriptions : isSingle);

    const ratingShared = typeof summary.ratingShared === 'boolean'
      ? summary.ratingShared
      : (typeof summary.hasMultipleRatings === 'boolean' ? !summary.hasMultipleRatings : isSingle);

    const dateShared = typeof summary.takenOverrideDateShared === 'boolean'
      ? summary.takenOverrideDateShared
      : (typeof summary.hasMultipleTakenOverrideDates === 'boolean' ? !summary.hasMultipleTakenOverrideDates : isSingle);

    let isHiddenVal = null;
    let isHiddenMultiple = false;
    if (typeof summary.hiddenCount === 'number') {
      if (summary.hiddenCount === this.selectionCount) {
        isHiddenVal = true;
      } else if (summary.hiddenCount === 0) {
        isHiddenVal = false;
      } else {
        isHiddenMultiple = true;
      }
    } else if (typeof summary.isHidden === 'boolean') {
      isHiddenVal = summary.isHidden;
    }

    return {
      title: {
        value: titleShared ? (summary.title ?? '') : '',
        isMultiple: !titleShared
      },
      description: {
        value: descShared ? (summary.description ?? '') : '',
        isMultiple: !descShared
      },
      rating: {
        value: ratingShared ? (summary.rating ?? 0) : null,
        isMultiple: !ratingShared
      },
      takenOverrideDate: {
        value: dateShared ? (summary.takenOverrideDate ?? null) : null,
        isMultiple: !dateShared
      },
      isHidden: {
        value: isHiddenVal,
        isMultiple: isHiddenMultiple
      }
    };
  }

  _initTagsState(summary) {
    this._tagsState.clear();

    if (Array.isArray(summary.tags)) {
      for (const t of summary.tags) {
        const tagId = Number(t.tagId || t.id);
        const count = typeof t.count === 'number' ? t.count : 1;

        let state = 0;
        if (count >= this.selectionCount) {
          state = 1; // all
        } else if (count > 0) {
          state = 0.5; // some
        }

        const storeTag = tagsStore.getTagById(tagId);
        const name = t.name || storeTag?.name || '';
        const color = t.color || t.bgColor || storeTag?.color || storeTag?.bgColor || null;

        this._tagsState.set(tagId, {
          id: tagId,
          tagId,
          name,
          color,
          bgColor: color,
          initial: state,
          current: state
        });
      }
    }
  }

  // --- Field Getters ---

  getField(name) {
    if (!this._current[name]) return null;
    return {
      value: this._current[name].value,
      isMultiple: this._current[name].isMultiple,
      isDirty: this._dirty[name]
    };
  }

  // --- Field Setters ---

  setTitle(val) {
    const v = val != null ? String(val) : '';
    this._current.title.value = v;
    this._current.title.isMultiple = false;
    this._dirty.title = (v !== this._initial.title.value) || this._initial.title.isMultiple;
  }

  setDescription(val) {
    const v = val != null ? String(val) : '';
    this._current.description.value = v;
    this._current.description.isMultiple = false;
    this._dirty.description = (v !== this._initial.description.value) || this._initial.description.isMultiple;
  }

  setRating(val) {
    const v = (val === null || val === undefined) ? 0 : Number(val);
    this._current.rating.value = v;
    this._current.rating.isMultiple = false;
    this._dirty.rating = (v !== this._initial.rating.value) || this._initial.rating.isMultiple;
  }

  setTakenOverrideDate(val) {
    const v = val ? String(val).trim() : null;
    this._current.takenOverrideDate.value = v;
    this._current.takenOverrideDate.isMultiple = false;
    this._dirty.takenOverrideDate = (v !== this._initial.takenOverrideDate.value) || this._initial.takenOverrideDate.isMultiple;
  }

  setIsHidden(val) {
    const v = Boolean(val);
    this._current.isHidden.value = v;
    this._current.isHidden.isMultiple = false;
    this._dirty.isHidden = (v !== this._initial.isHidden.value) || this._initial.isHidden.isMultiple;
  }

  // --- Tag Management ---

  getTagState(tagId) {
    const state = this._tagsState.get(Number(tagId));
    return state ? state.current : 0;
  }

  getAllTags() {
    return Array.from(this._tagsState.values());
  }

  /**
   * Adds or promotes a tag to 'all' (1).
   */
  addTag(tagId, tagMeta = {}) {
    const numId = Number(tagId);
    let state = this._tagsState.get(numId);
    const storeTag = tagsStore?.getTagById?.(numId);
    const name = tagMeta.name || storeTag?.name || '';
    const color = tagMeta.color || tagMeta.bgColor || storeTag?.color || storeTag?.bgColor || null;

    if (!state) {
      state = {
        id: numId,
        tagId: numId,
        name,
        color,
        bgColor: color,
        initial: 0,
        current: 1
      };
      this._tagsState.set(numId, state);
    } else {
      state.current = 1;
      if (name) state.name = name;
      if (color) {
        state.color = color;
        state.bgColor = color;
      }
    }
  }

  /**
   * Removes or marks tag for removal (0).
   */
  removeTag(tagId) {
    const state = this._tagsState.get(Number(tagId));
    if (state) {
      state.current = 0;
    }
  }

  /**
   * Cycles tag state:
   * Initial some (0.5) -> 1 ('all') -> 0 ('none' / remove) -> 0.5 ('some' / untouched).
   * Initial all (1)    -> 0 ('none' / remove) -> 1 ('all' / keep).
   * Initial none (0)   -> 1 ('all' / add)     -> 0 ('none' / remove).
   */
  cycleTag(tagId) {
    const state = this._tagsState.get(Number(tagId));
    if (!state) return;

    if (state.initial === 0.5) {
      if (state.current === 0.5) state.current = 1;
      else if (state.current === 1) state.current = 0;
      else state.current = 0.5;
    } else if (state.initial === 1) {
      state.current = state.current === 1 ? 0 : 1;
    } else {
      state.current = state.current === 1 ? 0 : 1;
    }
  }

  getTagDiff() {
    const addTagIds = [];
    const removeTagIds = [];

    for (const [tagId, state] of this._tagsState.entries()) {
      if (state.initial < 1 && state.current === 1) {
        addTagIds.push(tagId);
      } else if (state.initial > 0 && state.current === 0) {
        removeTagIds.push(tagId);
      }
    }

    return { addTagIds, removeTagIds };
  }

  // --- Dirty & Validation ---

  isDirty() {
    if (Object.values(this._dirty).some(Boolean)) return true;
    const { addTagIds, removeTagIds } = this.getTagDiff();
    return addTagIds.length > 0 || removeTagIds.length > 0;
  }

  validate() {
    const errors = {};

    if (this.selectionCount < 1 || this.selectionCount > 10000) {
      errors.selection = 'Selection size must be between 1 and 10,000 images';
    }

    if (this._dirty.rating) {
      const r = this._current.rating.value;
      if (typeof r !== 'number' || isNaN(r) || r < 0 || r > 5 || !Number.isInteger(r)) {
        errors.rating = 'Rating must be an integer between 0 and 5';
      }
    }

    if (this._dirty.takenOverrideDate) {
      const d = this._current.takenOverrideDate.value;
      if (d !== null && d !== '') {
        if (!isValidDateString(d)) {
          errors.takenOverrideDate = 'Date must be a valid calendar date in YYYY-MM-DD format';
        }
      }
    }

    return {
      valid: Object.keys(errors).length === 0,
      errors
    };
  }

  /**
   * Produces the exact PATCH /api/images body containing only dirty fields.
   */
  buildPatchBody() {
    const validation = this.validate();
    if (!validation.valid) {
      throw new Error(`Validation failed: ${JSON.stringify(validation.errors)}`);
    }

    const body = {
      ids: this.ids.map(id => Number(id))
    };

    if (this._dirty.title) {
      // Empty string means reset to default filename on server
      body.title = this._current.title.value;
    }

    if (this._dirty.description) {
      // Empty string or null means clear
      body.description = this._current.description.value.trim() === '' ? null : this._current.description.value;
    }

    if (this._dirty.rating) {
      body.rating = this._current.rating.value;
    }

    if (this._dirty.takenOverrideDate) {
      const d = this._current.takenOverrideDate.value;
      body.takenOverrideDate = (d === null || d.trim() === '') ? null : d.trim();
    }

    if (this._dirty.isHidden) {
      body.isHidden = Boolean(this._current.isHidden.value);
    }

    const { addTagIds, removeTagIds } = this.getTagDiff();
    if (addTagIds.length > 0) {
      body.addTagIds = addTagIds.map(id => Number(id));
    }
    if (removeTagIds.length > 0) {
      body.removeTagIds = removeTagIds.map(id => Number(id));
    }

    return body;
  }

  /**
   * Reverts all changes back to the initial state.
   */
  revert() {
    this._current = JSON.parse(JSON.stringify(this._initial));
    for (const key of Object.keys(this._dirty)) {
      this._dirty[key] = false;
    }
    for (const state of this._tagsState.values()) {
      state.current = state.initial;
    }
  }
}

