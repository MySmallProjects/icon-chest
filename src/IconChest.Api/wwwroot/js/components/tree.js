import { el, clear } from '../lib/dom.js';
import { TreeModel } from '../lib/tree-model.js';

/**
 * Accessible WAI-ARIA Tree component.
 */
export class TreeView {
  constructor(options = {}) {
    this.model = new TreeModel(options.roots || [], options);
    this.onSelect = options.onSelect || (() => {});
    this.focusedId = null;

    this.containerEl = el('div', {
      role: 'tree',
      className: 'wai-tree',
      tabindex: '0'
    });

    this._initEvents();
  }

  setRoots(roots) {
    this.model.setRoots(roots);
    this.render();
  }

  select(id) {
    this.model.selectedId = id;
    if (id !== null) {
      this.model.expandParents(id);
    }
    this.render();
    this.onSelect(id);
  }

  _initEvents() {
    this.containerEl.addEventListener('keydown', (e) => {
      const active = this.focusedId;
      if (!active) {
        const visible = this.model.getVisibleNodes();
        if (visible.length > 0) this.focusedId = visible[0].id;
      }

      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        if (this.focusedId) {
          const node = this.model.findNode(this.focusedId);
          if (node) {
            this.model.setExpanded(this.focusedId, true);
            this.select(this.focusedId);
          }
        }
        return;
      }

      const navResult = this.model.navigate(this.focusedId, e.key);
      if (navResult) {
        e.preventDefault();
        this.focusedId = navResult.targetId;
        this.render();
        this._focusItem(navResult.targetId);
        return;
      }

      if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
        const matchId = this.model.typeAhead(this.focusedId, e.key);
        if (matchId) {
          e.preventDefault();
          this.focusedId = matchId;
          this.render();
          this._focusItem(matchId);
        }
      }
    });
  }

  _focusItem(id) {
    const itemEl = this.containerEl.querySelector(`[data-id="${id}"]`);
    if (itemEl) {
      itemEl.focus();
    }
  }

  render() {
    clear(this.containerEl);
    const visible = this.model.getVisibleNodes();

    if (visible.length === 0) {
      this.containerEl.appendChild(el('div', { className: 'tree-empty-message' }, 'No items'));
      return;
    }

    if (!this.focusedId && visible.length > 0) {
      this.focusedId = this.model.selectedId || visible[0].id;
    }

    for (const item of visible) {
      const isSelected = this.model.selectedId === item.id;
      const isFocused = this.focusedId === item.id;

      const row = el('div', {
        role: 'treeitem',
        className: `tree-item level-${item.level} ${isSelected ? 'selected' : ''} ${isFocused ? 'focused' : ''}`,
        tabindex: isFocused ? '0' : '-1',
        'aria-selected': isSelected ? 'true' : 'false',
        'data-id': String(item.id),
        style: `padding-left: ${(item.level - 1) * 16 + 8}px;`
      });

      if (item.hasChildren) {
        row.setAttribute('aria-expanded', item.isExpanded ? 'true' : 'false');
      }

      // Chevron toggle button (toggles without selecting)
      if (item.hasChildren) {
        const chevron = el('button', {
          type: 'button',
          className: `tree-chevron ${item.isExpanded ? 'expanded' : ''}`,
          'aria-label': item.isExpanded ? 'Collapse' : 'Expand',
          tabindex: '-1'
        }, [
          el('svg', { viewBox: '0 0 24 24', width: '14', height: '14' }, [
            el('path', { d: 'M8.59 16.59L13.17 12 8.59 7.41 10 6l6 6-6 6-1.41-1.41z' })
          ])
        ]);

        chevron.addEventListener('click', (e) => {
          e.stopPropagation();
          this.model.toggleExpanded(item.id);
          this.focusedId = item.id;
          this.render();
          this._focusItem(item.id);
        });

        row.appendChild(chevron);
      } else {
        row.appendChild(el('span', { className: 'tree-spacer' }));
      }

      // Node Label (Click selects and expands)
      const label = el('span', {
        className: 'tree-label',
        title: item.node.path || item.name
      }, item.name);

      row.appendChild(label);

      // Node Count Badge
      const count = item.node.count ?? item.node.totalCount;
      if (typeof count === 'number') {
        row.appendChild(el('span', { className: 'tree-count' }, String(count)));
      }

      row.addEventListener('click', () => {
        this.focusedId = item.id;
        if (item.hasChildren) {
          this.model.setExpanded(item.id, true);
        }
        this.select(item.id);
      });

      this.containerEl.appendChild(row);
    }
  }
}

