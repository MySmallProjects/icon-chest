import { el, clear } from './dom.js';

/**
 * Accessible Modal Dialog using HTML5 <dialog>
 */
class DialogService {
  constructor() {
    this.dialogElem = null;
  }

  _getOrCreateDialog() {
    if (!this.dialogElem) {
      this.dialogElem = el('dialog', { className: 'app-dialog', id: 'app-global-dialog' });
      document.body.appendChild(this.dialogElem);
    }
    return this.dialogElem;
  }

  confirm({ title = 'Confirm', message, confirmText = 'Confirm', cancelText = 'Cancel', isDanger = false }) {
    return new Promise((resolve) => {
      const dialog = this._getOrCreateDialog();
      clear(dialog);

      const titleEl = el('div', { className: 'dialog-header' }, title);
      const bodyEl = el('div', { className: 'dialog-body' }, message);

      const cancelBtn = el('button', {
        className: 'btn',
        type: 'button',
        onClick: () => {
          dialog.close();
          resolve(false);
        }
      }, cancelText);

      const confirmBtn = el('button', {
        className: isDanger ? 'btn btn-danger' : 'btn btn-primary',
        type: 'button',
        onClick: () => {
          dialog.close();
          resolve(true);
        }
      }, confirmText);

      const footerEl = el('div', { className: 'dialog-footer' }, cancelBtn, confirmBtn);

      dialog.appendChild(titleEl);
      dialog.appendChild(bodyEl);
      dialog.appendChild(footerEl);

      const handleCancel = (e) => {
        e.preventDefault();
        dialog.close();
        resolve(false);
      };

      dialog.addEventListener('cancel', handleCancel, { once: true });
      dialog.showModal();
      confirmBtn.focus();
    });
  }

  alert({ title = 'Information', message, isMono = false, okText = 'Close' }) {
    return new Promise((resolve) => {
      const dialog = this._getOrCreateDialog();
      clear(dialog);

      const titleEl = el('div', { className: 'dialog-header' }, title);
      const bodyEl = el('div', { className: isMono ? 'dialog-body mono' : 'dialog-body' }, message);

      const okBtn = el('button', {
        className: 'btn btn-primary',
        type: 'button',
        onClick: () => {
          dialog.close();
          resolve(true);
        }
      }, okText);

      const footerEl = el('div', { className: 'dialog-footer' }, okBtn);

      dialog.appendChild(titleEl);
      dialog.appendChild(bodyEl);
      dialog.appendChild(footerEl);

      dialog.addEventListener('cancel', () => resolve(true), { once: true });
      dialog.showModal();
      okBtn.focus();
    });
  }
}

export const dialog = new DialogService();

