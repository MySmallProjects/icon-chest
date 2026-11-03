import { el, clear } from '../lib/dom.js';

/**
 * Accessible Context Menu Component.
 * Implements WAI-ARIA role="menu" pattern with keyboard navigation,
 * type-ahead, Escape dismiss, and focus return.
 */
export class ContextMenu {
  constructor() {
    this.menuEl = null;
    this.invokerEl = null;
    this.isOpen = false;
    this.items = [];
    this._keydownHandler = this._handleKeydown.bind(this);
    this._outsideClickHandler = this._handleOutsideClick.bind(this);
  }

  _getOrCreateMenu() {
    if (!this.menuEl) {
      this.menuEl = el('div', {
        className: 'context-menu',
        role: 'menu',
        tabindex: '-1',
        style: {
          display: 'none',
          position: 'fixed',
          zIndex: '1000'
        }
      });
      document.body.appendChild(this.menuEl);
    }
    return this.menuEl;
  }

  /**
   * Opens the context menu.
   * @param {Object} options
   * @param {number} options.x - Client X coordinate.
   * @param {number} options.y - Client Y coordinate.
   * @param {HTMLElement} [options.invoker] - Element that triggered the menu (for focus return).
   * @param {Array<Object>} options.items - Array of { id, label, icon, shortcut, action, danger }
   */
  open({ x, y, invoker = null, items = [] }) {
    this.close();

    this.invokerEl = invoker || document.activeElement;
    this.items = items.filter(it => !it.hidden);
    if (this.items.length === 0) return;

    const menu = this._getOrCreateMenu();
    clear(menu);

    for (let i = 0; i < this.items.length; i++) {
      const item = this.items[i];
      if (item.separator) {
        menu.appendChild(el('div', { className: 'context-menu-separator', role: 'separator' }));
        continue;
      }

      const itemEl = el('button', {
        type: 'button',
        className: `context-menu-item${item.danger ? ' danger' : ''}`,
        role: 'menuitem',
        tabindex: '-1',
        dataset: { index: String(i) },
        onClick: (e) => {
          e.stopPropagation();
          this.close();
          if (typeof item.action === 'function') {
            item.action();
          }
        }
      }, [
        item.icon ? el('span', { className: 'menu-item-icon' }, item.icon) : null,
        el('span', { className: 'menu-item-label' }, item.label),
        item.shortcut ? el('span', { className: 'menu-item-shortcut' }, item.shortcut) : null
      ]);

      menu.appendChild(itemEl);
    }

    menu.style.display = 'block';
    this.isOpen = true;

    // Viewport boundary clamping
    const rect = menu.getBoundingClientRect();
    const vw = window.innerWidth;
    const vh = window.innerHeight;

    let posX = x;
    let posY = y;

    if (posX + rect.width > vw - 8) {
      posX = Math.max(8, vw - rect.width - 8);
    }
    if (posY + rect.height > vh - 8) {
      posY = Math.max(8, vh - rect.height - 8);
    }

    menu.style.left = `${posX}px`;
    menu.style.top = `${posY}px`;

    // Listeners
    window.addEventListener('keydown', this._keydownHandler, true);
    window.addEventListener('pointerdown', this._outsideClickHandler, true);

    // Focus first item
    const firstBtn = menu.querySelector('button[role="menuitem"]');
    if (firstBtn) {
      firstBtn.focus();
    }
  }

  close() {
    if (!this.isOpen) return;
    this.isOpen = false;

    if (this.menuEl) {
      this.menuEl.style.display = 'none';
      clear(this.menuEl);
    }

    window.removeEventListener('keydown', this._keydownHandler, true);
    window.removeEventListener('pointerdown', this._outsideClickHandler, true);

    // Return focus
    if (this.invokerEl && typeof this.invokerEl.focus === 'function') {
      try {
        this.invokerEl.focus();
      } catch {}
    }
    this.invokerEl = null;
  }

  _handleOutsideClick(e) {
    if (this.menuEl && !this.menuEl.contains(e.target)) {
      this.close();
    }
  }

  _handleKeydown(e) {
    if (!this.isOpen || !this.menuEl) return;

    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      this.close();
      return;
    }

    const buttons = Array.from(this.menuEl.querySelectorAll('button[role="menuitem"]'));
    if (buttons.length === 0) return;

    const currentIndex = buttons.indexOf(document.activeElement);

    if (e.key === 'ArrowDown') {
      e.preventDefault();
      e.stopPropagation();
      const nextIdx = (currentIndex + 1) % buttons.length;
      buttons[nextIdx].focus();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      e.stopPropagation();
      const prevIdx = (currentIndex - 1 + buttons.length) % buttons.length;
      buttons[prevIdx].focus();
    } else if (e.key === 'Home') {
      e.preventDefault();
      buttons[0].focus();
    } else if (e.key === 'End') {
      e.preventDefault();
      buttons[buttons.length - 1].focus();
    } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      // Type-ahead by first letter
      const char = e.key.toLowerCase();
      const match = buttons.find(b => {
        const text = (b.textContent || '').trim().toLowerCase();
        return text.startsWith(char);
      });
      if (match) {
        match.focus();
      }
    }
  }
}

export const contextMenu = new ContextMenu();

