/**
 * Active Filters and Scope Bar Component.
 * Displays active filter chips with remove buttons, and an optional scope badge.
 */

import { el, clear, createSvg } from '../lib/dom.js';

const ICON_CLOSE = '<svg viewBox="0 0 24 24" width="12" height="12"><path fill="currentColor" d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>';

export class ActiveFiltersBar {
  /**
   * @param {Object} options
   * @param {Function} options.onRemoveChip - callback (chip) => void
   * @param {Function} options.onClearAll - callback () => void
   */
  constructor(options = {}) {
    this.onRemoveChip = options.onRemoveChip || (() => {});
    this.onClearAll = options.onClearAll || (() => {});

    this.containerEl = el('div', {
      className: 'active-filters-bar hidden',
      'aria-label': 'Active filters'
    });

    this.chipsListEl = el('div', { className: 'active-filters-list' });
    this.scopeBadgeEl = el('span', { className: 'scope-badge hidden' });

    this.clearAllBtn = el('button', {
      type: 'button',
      className: 'btn-clear-all-filters',
      title: 'Clear all active filters',
      onClick: () => this.onClearAll()
    }, 'Clear all');

    this.containerEl.appendChild(this.scopeBadgeEl);
    this.containerEl.appendChild(this.chipsListEl);
    this.containerEl.appendChild(this.clearAllBtn);
  }

  /**
   * Updates rendered chips and scope.
   * @param {Array} chips - Array of { id, label, remove }
   * @param {string|null} scopeText - Optional scope label (e.g. "Folder: Pictures")
   */
  render(chips = [], scopeText = null) {
    clear(this.chipsListEl);

    if (scopeText) {
      this.scopeBadgeEl.textContent = scopeText;
      this.scopeBadgeEl.classList.remove('hidden');
    } else {
      this.scopeBadgeEl.classList.add('hidden');
    }

    if (chips.length === 0 && !scopeText) {
      this.containerEl.classList.add('hidden');
      return;
    }

    this.containerEl.classList.remove('hidden');

    for (const chip of chips) {
      const closeBtn = el('button', {
        type: 'button',
        className: 'chip-remove-btn',
        'aria-label': `Remove ${chip.label}`,
        onClick: (e) => {
          e.stopPropagation();
          this.onRemoveChip(chip);
        }
      }, [createSvg(ICON_CLOSE)]);

      const chipEl = el('div', { className: 'active-filter-chip' }, [
        el('span', { className: 'chip-label' }, chip.label),
        closeBtn
      ]);

      this.chipsListEl.appendChild(chipEl);
    }

    if (chips.length > 0) {
      this.clearAllBtn.classList.remove('hidden');
    } else {
      this.clearAllBtn.classList.add('hidden');
    }
  }
}

