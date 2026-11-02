import { el } from './dom.js';

class ToastManager {
  constructor() {
    this.container = null;
  }

  _getOrCreateContainer() {
    if (!this.container) {
      this.container = el('div', {
        id: 'toast-container',
        role: 'region',
        'aria-live': 'polite',
        'aria-label': 'Notifications'
      });
      document.body.appendChild(this.container);
    }
    return this.container;
  }

  show(message, type = 'info', duration = null) {
    const container = this._getOrCreateContainer();

    if (duration == null) {
      duration = type === 'error' ? 8000 : 4000;
    }

    const toast = el('div', {
      className: `toast toast-${type}`,
      role: 'alert'
    });

    const msgSpan = el('span', {}, message);
    const closeBtn = el('button', {
      className: 'toast-close',
      type: 'button',
      'aria-label': 'Dismiss',
      onClick: () => this._dismiss(toast)
    }, '×');

    toast.appendChild(msgSpan);
    toast.appendChild(closeBtn);
    container.appendChild(toast);

    if (duration > 0) {
      setTimeout(() => {
        this._dismiss(toast);
      }, duration);
    }

    return toast;
  }

  success(message, duration) {
    return this.show(message, 'success', duration);
  }

  error(message, duration) {
    return this.show(message, 'error', duration);
  }

  info(message, duration) {
    return this.show(message, 'info', duration);
  }

  _dismiss(toast) {
    if (toast.parentElement) {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(100%)';
      toast.style.transition = 'all 200ms ease';
      setTimeout(() => {
        if (toast.parentElement) {
          toast.parentElement.removeChild(toast);
        }
      }, 200);
    }
  }
}

export const toast = new ToastManager();

