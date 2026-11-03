/**
 * Advanced Query Box Component.
 * Features live validation, clickable error diagnostics selecting syntax spans,
 * Recent Queries and Examples dropdowns, and keyboard accessibility.
 */

import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { QueryBoxModel } from '../lib/query-box-model.js';

const ICON_CHECK = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>';
const ICON_WARN = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z"/></svg>';
const ICON_HELP = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 16h-2v-2h2v2zm1.07-7.75l-.9.92C12.45 11.9 12 12.5 12 14h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H7c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.04-.42 1.99-1.07 2.75z"/></svg>';

export class QueryBox {
  /**
   * @param {Object} options
   * @param {Function} options.onRunQuery - callback (queryText) => void
   * @param {Function} options.onClear - callback () => void
   * @param {Function} [options.onNavigateHelp] - callback () => void
   */
  constructor(options = {}) {
    this.onRunQuery = options.onRunQuery || (() => {});
    this.onClear = options.onClear || (() => {});
    this.onNavigateHelp = options.onNavigateHelp || (() => { window.location.hash = '#help/query'; });

    this.examples = [];

    // Pure logic model
    this.model = new QueryBoxModel({
      validateFn: async (text) => {
        return await api.validateQuery(text);
      },
      onStateChange: (state) => this._renderState(state)
    });

    this._buildUI();
    this._loadHelpExamples();
  }

  _buildUI() {
    this.textarea = el('textarea', {
      className: 'query-box-textarea',
      rows: '2',
      placeholder: 'e.g. (type == png or type == svg) and rating >= 4  (Press Ctrl+Enter to run)',
      'aria-label': 'Advanced query input',
      onInput: (e) => {
        this.model.setText(e.target.value);
      },
      onKeyDown: (e) => {
        if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
          e.preventDefault();
          this.run();
        } else if (e.key === 'Escape') {
          if (this.textarea.value.trim().length > 0) {
            e.preventDefault();
            this.clear();
          } else {
            this.textarea.blur();
          }
        }
      }
    });

    // Validation status row
    this.statusContainer = el('div', { className: 'query-box-status', 'aria-live': 'polite' });

    // Dropdowns: Examples & Recent
    this.examplesSelect = el('select', {
      className: 'select select-xs query-select',
      'aria-label': 'Choose a query example',
      onChange: (e) => {
        if (e.target.value) {
          this.setQuery(e.target.value);
          e.target.value = '';
        }
      }
    }, [
      el('option', { value: '' }, 'Examples...')
    ]);

    this.recentSelect = el('select', {
      className: 'select select-xs query-select',
      'aria-label': 'Recent queries',
      onChange: (e) => {
        if (e.target.value === '__clear__') {
          this.model.clearRecentQueries();
          this._updateRecentDropdown();
          e.target.value = '';
        } else if (e.target.value) {
          this.setQuery(e.target.value);
          e.target.value = '';
        }
      }
    }, [
      el('option', { value: '' }, 'Recent queries...')
    ]);

    // Action buttons
    this.runBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm btn-primary query-run-btn',
      title: 'Run query (Ctrl+Enter)',
      onClick: () => this.run()
    }, 'Run');

    this.clearBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm query-clear-btn',
      title: 'Clear query',
      onClick: () => this.clear()
    }, 'Clear');

    this.helpBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm query-help-btn',
      title: 'Open Query Reference in Help',
      onClick: () => this.onNavigateHelp()
    }, [
      createSvg(ICON_HELP),
      el('span', {}, 'Help')
    ]);

    const toolbarLeft = el('div', { className: 'query-toolbar-left' }, [
      this.examplesSelect,
      this.recentSelect,
      this.helpBtn
    ]);

    const toolbarRight = el('div', { className: 'query-toolbar-right' }, [
      this.clearBtn,
      this.runBtn
    ]);

    const toolbar = el('div', { className: 'query-box-toolbar' }, [
      toolbarLeft,
      toolbarRight
    ]);

    this.containerEl = el('div', { className: 'query-box-container' }, [
      this.textarea,
      this.statusContainer,
      toolbar
    ]);

    this._updateRecentDropdown();
  }

  async _loadHelpExamples() {
    try {
      const help = await api.getQueryHelp();
      if (Array.isArray(help?.examples)) {
        this.examples = help.examples;
        clear(this.examplesSelect);
        this.examplesSelect.appendChild(el('option', { value: '' }, 'Query Examples...'));
        for (const ex of this.examples) {
          const qText = ex.query || ex.text || '';
          const desc = ex.description || '';
          const label = desc ? `${qText} — ${desc}` : qText;
          const opt = el('option', { value: qText }, label);
          this.examplesSelect.appendChild(opt);
        }
      }
    } catch {
      // Offline or network error
    }
  }

  _updateRecentDropdown() {
    clear(this.recentSelect);
    this.recentSelect.appendChild(el('option', { value: '' }, 'Recent queries...'));
    const recents = this.model.getRecentQueries();
    for (const q of recents) {
      const truncated = q.length > 50 ? q.substring(0, 47) + '...' : q;
      this.recentSelect.appendChild(el('option', { value: q }, truncated));
    }
    if (recents.length > 0) {
      this.recentSelect.appendChild(el('option', { value: '__clear__' }, '── Clear history ──'));
    }
  }

  _renderState(state) {
    clear(this.statusContainer);

    if (state.isValidating) {
      const spinner = el('span', { className: 'query-status-loading' }, 'Validating...');
      this.statusContainer.appendChild(spinner);
      return;
    }

    if (!state.text.trim()) {
      this.runBtn.disabled = true;
      return;
    }

    if (state.isValid) {
      this.runBtn.disabled = false;
      const validBadge = el('span', { className: 'query-status-valid' }, [
        createSvg(ICON_CHECK),
        el('span', {}, 'Valid query')
      ]);
      this.statusContainer.appendChild(validBadge);

      if (state.mentionsHidden) {
        this.statusContainer.appendChild(
          el('span', { className: 'query-status-notice' }, 'Notice: Includes hidden images')
        );
      }
      if (state.mentionsMissing) {
        this.statusContainer.appendChild(
          el('span', { className: 'query-status-notice' }, 'Notice: Includes missing images')
        );
      }
    } else {
      this.runBtn.disabled = true;
      const errorList = el('div', { className: 'query-status-errors' });

      for (const err of state.errors) {
        const [start, end] = this.model.getErrorSelectionRange(err);

        const pill = el('button', {
          type: 'button',
          className: 'query-error-pill',
          title: 'Click to select error in text',
          onClick: () => {
            this.textarea.focus();
            this.textarea.setSelectionRange(start, end);
          }
        }, [
          createSvg(ICON_WARN),
          el('span', {}, `${err.message || 'Syntax error'} at ${start + 1}`)
        ]);

        errorList.appendChild(pill);
      }

      this.statusContainer.appendChild(errorList);
    }
  }

  setQuery(text) {
    this.textarea.value = text || '';
    this.model.setText(text || '', true);
  }

  getQuery() {
    return this.textarea.value.trim();
  }

  focus() {
    this.textarea.focus();
    this.textarea.select();
  }

  run() {
    const text = this.getQuery();
    if (!text || !this.model.isValid) return;
    this.model.addRecentQuery(text);
    this._updateRecentDropdown();
    this.onRunQuery(text);
  }

  clear() {
    this.textarea.value = '';
    this.model.setText('');
    this.onClear();
  }
}

