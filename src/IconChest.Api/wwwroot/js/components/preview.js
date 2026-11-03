import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { store } from '../lib/store.js';

const CLOSE_ICON = '<svg viewBox="0 0 24 24" width="24" height="24"><path fill="currentColor" d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>';
const PREV_ICON = '<svg viewBox="0 0 24 24" width="28" height="28"><path fill="currentColor" d="M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z"/></svg>';
const NEXT_ICON = '<svg viewBox="0 0 24 24" width="28" height="28"><path fill="currentColor" d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>';

/**
 * Full-window modal preview overlay.
 */
export class PreviewOverlay {
  constructor(options = {}) {
    this.onClose = options.onClose || (() => {});
    this.onSelectIndex = options.onSelectIndex || (() => {});

    this.isOpen = false;
    this.items = []; // array of IDs
    this.currentIndex = 0;
    this.imageMetadata = null; // metadata for current image
    this.showInfo = false;

    this._idleTimer = null;
    this._spinnerTimer = null;
    this._appRootEl = document.getElementById('app-root');

    this._buildDom();
    this._bindEvents();
  }

  _buildDom() {
    this.overlayEl = el('div', {
      role: 'dialog',
      'aria-modal': 'true',
      'aria-label': 'Image Preview',
      className: 'preview-overlay',
      tabindex: '-1',
      hidden: 'true'
    });

    // Content container
    this.stageEl = el('div', { className: 'preview-stage' });

    // Image element
    this.imageEl = el('img', {
      className: 'preview-image',
      alt: 'Preview image'
    });

    // Spinner
    this.spinnerEl = el('div', { className: 'preview-spinner', hidden: 'true' }, [
      el('span', { className: 'spinner-icon' })
    ]);

    // Error message
    this.errorEl = el('div', { className: 'preview-error', hidden: 'true' }, "Can't load this image");

    this.stageEl.appendChild(this.imageEl);
    this.stageEl.appendChild(this.spinnerEl);
    this.stageEl.appendChild(this.errorEl);

    // Controls container
    this.controlsEl = el('div', { className: 'preview-controls' });

    // Close button
    this.closeBtn = el('button', {
      type: 'button',
      className: 'preview-btn preview-close-btn',
      'aria-label': 'Close preview (Escape)',
      title: 'Close (Esc)'
    }, [createSvg(CLOSE_ICON)]);

    // Prev / Next buttons
    this.prevBtn = el('button', {
      type: 'button',
      className: 'preview-nav-btn preview-prev-btn',
      'aria-label': 'Previous image (Left arrow)',
      title: 'Previous (Left arrow)'
    }, [createSvg(PREV_ICON)]);

    this.nextBtn = el('button', {
      type: 'button',
      className: 'preview-nav-btn preview-next-btn',
      'aria-label': 'Next image (Right arrow)',
      title: 'Next (Right arrow)'
    }, [createSvg(NEXT_ICON)]);

    // Info overlay
    this.infoOverlayEl = el('div', {
      className: 'preview-info-overlay',
      hidden: 'true'
    });

    this.controlsEl.appendChild(this.closeBtn);
    this.controlsEl.appendChild(this.prevBtn);
    this.controlsEl.appendChild(this.nextBtn);

    this.overlayEl.appendChild(this.stageEl);
    this.overlayEl.appendChild(this.controlsEl);
    this.overlayEl.appendChild(this.infoOverlayEl);

    document.body.appendChild(this.overlayEl);
  }

  _bindEvents() {
    this.closeBtn.addEventListener('click', () => this.close());
    this.prevBtn.addEventListener('click', () => this.prev());
    this.nextBtn.addEventListener('click', () => this.next());

    // Mouse movement reveals controls, idles out after 2s
    this.overlayEl.addEventListener('mousemove', () => this._bumpIdle());

    this.overlayEl.addEventListener('keydown', (e) => {
      switch (e.key) {
        case 'Escape':
          e.preventDefault();
          this.close();
          break;
        case 'ArrowLeft':
          e.preventDefault();
          this.prev();
          break;
        case 'ArrowRight':
          e.preventDefault();
          this.next();
          break;
        case 'Home':
          e.preventDefault();
          this.goTo(0);
          break;
        case 'End':
          e.preventDefault();
          this.goTo(this.items.length - 1);
          break;
        case 'i':
        case 'I':
          e.preventDefault();
          this.toggleInfo();
          break;
      }
    });
  }

  _bumpIdle() {
    this.controlsEl.classList.remove('idle');
    if (this._idleTimer) clearTimeout(this._idleTimer);
    this._idleTimer = setTimeout(() => {
      if (this.isOpen) {
        this.controlsEl.classList.add('idle');
      }
    }, 2000);
  }

  open(items, startIndex = 0, backgroundMode = 'checker') {
    if (!items || items.length === 0) return;
    this.items = items;
    this.currentIndex = Math.max(0, Math.min(items.length - 1, startIndex));
    this.isOpen = true;

    // Apply background mode
    this.stageEl.className = `preview-stage bg-${backgroundMode}`;

    // Read initial info overlay setting
    const appSettings = store.get('settings') || {};
    this.showInfo = appSettings.previewInfoOverlayDefault ?? false;
    this.infoOverlayEl.hidden = !this.showInfo;

    // Show overlay and make rest of app inert
    this.overlayEl.hidden = false;
    if (this._appRootEl) {
      if ('inert' in this._appRootEl) {
        this._appRootEl.inert = true;
      } else {
        this._appRootEl.setAttribute('aria-hidden', 'true');
      }
    }

    this.overlayEl.focus();
    this._loadCurrent();
    this._bumpIdle();
  }

  close() {
    if (!this.isOpen) return;
    this.isOpen = false;
    this.overlayEl.hidden = true;

    if (this._idleTimer) clearTimeout(this._idleTimer);
    if (this._spinnerTimer) clearTimeout(this._spinnerTimer);

    if (this._appRootEl) {
      if ('inert' in this._appRootEl) {
        this._appRootEl.inert = false;
      } else {
        this._appRootEl.removeAttribute('aria-hidden');
      }
    }

    const lastId = this.items[this.currentIndex];
    this.onClose(lastId, this.currentIndex);
  }

  prev() {
    if (this.currentIndex > 0) {
      this.goTo(this.currentIndex - 1);
    }
  }

  next() {
    if (this.currentIndex < this.items.length - 1) {
      this.goTo(this.currentIndex + 1);
    }
  }

  goTo(index) {
    if (index < 0 || index >= this.items.length) return;
    this.currentIndex = index;
    this._loadCurrent();
    this._bumpIdle();
  }

  toggleInfo() {
    this.showInfo = !this.showInfo;
    this.infoOverlayEl.hidden = !this.showInfo;
    this._bumpIdle();
  }

  async _loadCurrent() {
    const id = this.items[this.currentIndex];
    this.prevBtn.disabled = this.currentIndex <= 0;
    this.nextBtn.disabled = this.currentIndex >= this.items.length - 1;

    // Reset state
    this.imageEl.hidden = true;
    this.errorEl.hidden = true;

    // Start 150ms spinner timer
    if (this._spinnerTimer) clearTimeout(this._spinnerTimer);
    this._spinnerTimer = setTimeout(() => {
      this.spinnerEl.hidden = false;
    }, 150);

    // Update metadata info overlay
    this._updateInfoOverlay(id);

    // Preload neighbours
    this._preloadNeighbours();

    // Load original image file
    const fileUrl = `/api/images/${id}/file`;
    const img = new Image();
    img.src = fileUrl;

    try {
      await img.decode();
      if (this.items[this.currentIndex] === id) {
        if (this._spinnerTimer) clearTimeout(this._spinnerTimer);
        this.spinnerEl.hidden = true;
        this.imageEl.src = fileUrl;
        this.imageEl.hidden = false;
      }
    } catch {
      if (this.items[this.currentIndex] === id) {
        if (this._spinnerTimer) clearTimeout(this._spinnerTimer);
        this.spinnerEl.hidden = true;
        this.errorEl.hidden = false;
      }
    }
  }

  _preloadNeighbours() {
    if (this.currentIndex > 0) {
      const prevId = this.items[this.currentIndex - 1];
      const prevImg = new Image();
      prevImg.src = `/api/images/${prevId}/file`;
    }
    if (this.currentIndex < this.items.length - 1) {
      const nextId = this.items[this.currentIndex + 1];
      const nextImg = new Image();
      nextImg.src = `/api/images/${nextId}/file`;
    }
  }

  async _updateInfoOverlay(id) {
    clear(this.infoOverlayEl);

    const posText = `${this.currentIndex + 1} of ${this.items.length}`;

    try {
      const details = await api.getImage(id);
      if (this.items[this.currentIndex] !== id) return;

      const title = details.title || details.fileName || 'Untitled';
      const sizeBytes = details.sizeBytes ?? details.fileSizeBytes ?? details.size_bytes ?? 0;
      const sizeKb = sizeBytes > 0 ? (sizeBytes / 1024).toFixed(1) + ' KB' : '—';
      const dim = (details.width && details.height) ? `${details.width} × ${details.height}` : 'Vector / Unknown';
      const fmt = (details.format || '').toUpperCase();
      const effDate = details.effectiveDateUtc ? details.effectiveDateUtc.substring(0, 10) : '—';

      this.infoOverlayEl.appendChild(el('div', { className: 'preview-info-title' }, title));
      this.infoOverlayEl.appendChild(el('div', { className: 'preview-info-details' }, [
        el('span', { className: 'preview-info-filename' }, details.fileName),
        el('span', { className: 'preview-info-dim' }, dim),
        el('span', { className: 'preview-info-size' }, `${sizeKb} (${fmt})`),
        el('span', { className: 'preview-info-date' }, effDate),
        el('span', { className: 'preview-info-pos' }, posText)
      ]));
    } catch {
      this.infoOverlayEl.appendChild(el('div', { className: 'preview-info-pos' }, posText));
    }
  }
}

