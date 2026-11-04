/**
 * Desktop-style multi-selection model.
 * Pure logic decoupled from the DOM.
 */

export class SelectionModel {
  constructor(initialIds = []) {
    this.selectedIds = new Set(initialIds);
    this.anchorId = null;
    this.focusId = null;
    this._listeners = new Set();
  }

  onChange(listener) {
    this._listeners.add(listener);
    return () => this._listeners.delete(listener);
  }

  _notify() {
    for (const listener of this._listeners) {
      try {
        listener(this);
      } catch (err) {
        console.error('Error in selection listener', err);
      }
    }
  }

  isSelected(id) {
    return this.selectedIds.has(id);
  }

  get count() {
    return this.selectedIds.size;
  }

  getSelectedList() {
    return Array.from(this.selectedIds);
  }

  getSelectedArray() {
    return Array.from(this.selectedIds);
  }

  clear() {
    if (this.selectedIds.size === 0 && this.anchorId === null && this.focusId === null) return;
    this.selectedIds.clear();
    this.anchorId = null;
    this.focusId = null;
    this._notify();
  }

  selectAll(allIds) {
    this.selectedIds = new Set(allIds);
    if (allIds.length > 0) {
      if (this.anchorId === null) this.anchorId = allIds[0];
      this.focusId = allIds[allIds.length - 1];
    }
    this._notify();
  }

  handleClick(id, isCtrl, isShift, orderedIds = []) {
    if (id === undefined || id === null) {
      this.clear();
      return;
    }

    this.focusId = id;

    if (isShift && this.anchorId !== null && orderedIds.length > 0) {
      const anchorIdx = orderedIds.indexOf(this.anchorId);
      const targetIdx = orderedIds.indexOf(id);

      if (anchorIdx !== -1 && targetIdx !== -1) {
        const start = Math.min(anchorIdx, targetIdx);
        const end = Math.max(anchorIdx, targetIdx);
        const rangeIds = orderedIds.slice(start, end + 1);

        if (isCtrl) {
          // Ctrl+Shift: Add range to existing selection
          for (const rId of rangeIds) {
            this.selectedIds.add(rId);
          }
        } else {
          // Plain Shift: Replace selection with range
          this.selectedIds = new Set(rangeIds);
        }
        this._notify();
        return;
      }
    }

    if (isCtrl) {
      // Ctrl click: toggle item
      if (this.selectedIds.has(id)) {
        this.selectedIds.delete(id);
      } else {
        this.selectedIds.add(id);
      }
      this.anchorId = id;
      this._notify();
      return;
    }

    // Normal click: single select
    this.selectedIds = new Set([id]);
    this.anchorId = id;
    this._notify();
  }

  toggle(id) {
    if (id === undefined || id === null) return;
    if (this.selectedIds.has(id)) {
      this.selectedIds.delete(id);
    } else {
      this.selectedIds.add(id);
    }
    this.focusId = id;
    this.anchorId = id;
    this._notify();
  }

  addRange(ids) {
    if (!ids || ids.length === 0) return;
    let changed = false;
    for (const id of ids) {
      if (!this.selectedIds.has(id)) {
        this.selectedIds.add(id);
        changed = true;
      }
    }
    if (changed) this._notify();
  }

  replaceSelection(ids) {
    this.selectedIds = new Set(ids);
    this._notify();
  }

  retain(validIds) {
    const validSet = validIds instanceof Set ? validIds : new Set(validIds);
    let changed = false;
    for (const id of this.selectedIds) {
      if (!validSet.has(id)) {
        this.selectedIds.delete(id);
        changed = true;
      }
    }
    if (this.anchorId !== null && !validSet.has(this.anchorId)) {
      this.anchorId = null;
      changed = true;
    }
    if (this.focusId !== null && !validSet.has(this.focusId)) {
      this.focusId = null;
      changed = true;
    }
    if (changed) this._notify();
  }
}

