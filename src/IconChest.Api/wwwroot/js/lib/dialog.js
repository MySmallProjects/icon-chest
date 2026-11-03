import { el, clear } from './dom.js';
import { PRESET_PAIRS, parseHex, normalizeHex, contrastRatio, getBestTextColor } from './contrast.js';
import { validateTagName } from './tag-combobox-model.js';
import { store } from './store.js';

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

  /**
   * Three-button dialog for unsaved changes guard (Save, Discard, Cancel).
   * Esc or Cancel button resolves 'cancel'.
   */
  confirmThree({
    title = 'Unsaved Changes',
    message = 'You have unsaved changes. Do you want to save them before proceeding?',
    saveText = 'Save',
    discardText = 'Discard',
    cancelText = 'Cancel'
  }) {
    return new Promise((resolve) => {
      const dialog = this._getOrCreateDialog();
      clear(dialog);

      const titleEl = el('div', { className: 'dialog-header' }, title);
      const bodyEl = el('div', { className: 'dialog-body' }, message);

      let handleCancel;
      const cleanup = () => {
        if (handleCancel) {
          dialog.removeEventListener('cancel', handleCancel);
        }
      };

      const cancelBtn = el('button', {
        className: 'btn',
        type: 'button',
        onClick: () => {
          cleanup();
          dialog.close();
          resolve('cancel');
        }
      }, cancelText);

      const discardBtn = el('button', {
        className: 'btn btn-danger',
        type: 'button',
        onClick: () => {
          cleanup();
          dialog.close();
          resolve('discard');
        }
      }, discardText);

      const saveBtn = el('button', {
        className: 'btn btn-primary',
        type: 'button',
        onClick: () => {
          cleanup();
          dialog.close();
          resolve('save');
        }
      }, saveText);

      const footerEl = el('div', { className: 'dialog-footer' }, cancelBtn, discardBtn, saveBtn);

      dialog.appendChild(titleEl);
      dialog.appendChild(bodyEl);
      dialog.appendChild(footerEl);

      handleCancel = (e) => {
        e.preventDefault();
        cleanup();
        dialog.close();
        resolve('cancel');
      };

      dialog.addEventListener('cancel', handleCancel, { once: true });
      dialog.showModal();
      saveBtn.focus();
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

  /**
   * Accessible Rename Tag dialog with inline validation.
   */
  promptTagRename({ currentName = '', onValidate = null }) {
    return new Promise((resolve) => {
      const dialog = this._getOrCreateDialog();
      clear(dialog);

      const titleEl = el('div', { className: 'dialog-header' }, 'Rename Tag');

      const input = el('input', {
        type: 'text',
        className: 'input text-input full-width',
        value: currentName,
        'aria-label': 'Tag name',
        maxlength: '50'
      });

      const errorEl = el('div', { className: 'dialog-error-text', style: { color: 'var(--color-danger, #ef4444)', fontSize: '0.85rem', marginTop: '6px', minHeight: '1.2em' } });

      const form = el('form', {
        onSubmit: (e) => {
          e.preventDefault();
          const val = input.value.trim();
          const validation = validateTagName(val);
          if (!validation.valid) {
            errorEl.textContent = validation.error;
            return;
          }
          if (typeof onValidate === 'function') {
            const customErr = onValidate(val);
            if (customErr) {
              errorEl.textContent = customErr;
              return;
            }
          }
          dialog.close();
          resolve(val);
        }
      }, [
        el('label', { className: 'form-label' }, 'Tag name:'),
        input,
        errorEl
      ]);

      const cancelBtn = el('button', {
        className: 'btn',
        type: 'button',
        onClick: () => {
          dialog.close();
          resolve(null);
        }
      }, 'Cancel');

      const submitBtn = el('button', {
        className: 'btn btn-primary',
        type: 'submit',
        onClick: () => {
          form.requestSubmit();
        }
      }, 'Rename');

      const footerEl = el('div', { className: 'dialog-footer' }, cancelBtn, submitBtn);

      dialog.appendChild(titleEl);
      dialog.appendChild(el('div', { className: 'dialog-body' }, form));
      dialog.appendChild(footerEl);

      dialog.addEventListener('cancel', (e) => {
        e.preventDefault();
        dialog.close();
        resolve(null);
      }, { once: true });

      dialog.showModal();
      input.focus();
      input.select();
    });
  }

  /**
   * Accessible Recolour Tag dialog with 16 preset swatches, hex input,
   * live contrast warning against dark/light modes, and instant preview.
   */
  promptTagRecolor({ currentColor = '#475569', tagName = 'Tag' }) {
    return new Promise((resolve) => {
      const dialog = this._getOrCreateDialog();
      clear(dialog);

      const titleEl = el('div', { className: 'dialog-header' }, `Recolour Tag: ${tagName}`);

      let selectedColor = normalizeHex(currentColor) || '#475569';

      // Preview chip
      const previewChip = el('span', {
        className: 'tag-chip tag-preview-chip',
        style: {
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          padding: '4px 10px',
          borderRadius: '12px',
          backgroundColor: selectedColor,
          color: getBestTextColor(selectedColor),
          fontSize: '0.9rem',
          fontWeight: '500'
        }
      }, tagName);

      const previewRow = el('div', {
        style: { display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '14px' }
      }, [
        el('span', {}, 'Preview:'),
        previewChip
      ]);

      // Hex input & contrast warning
      const hexInput = el('input', {
        type: 'text',
        className: 'input text-input',
        value: selectedColor,
        style: { width: '120px' },
        'aria-label': 'Hex color code'
      });

      const warningEl = el('div', {
        className: 'contrast-warning',
        style: { color: 'var(--color-warning, #f59e0b)', fontSize: '0.85rem', marginTop: '6px', minHeight: '1.2em' }
      });

      function updatePreview(hex) {
        const norm = normalizeHex(hex);
        if (norm) {
          selectedColor = norm;
          previewChip.style.backgroundColor = norm;
          previewChip.style.color = getBestTextColor(norm);

          // Contrast warnings against dark (#1f2937) and light (#ffffff) backgrounds
          const ratioLight = contrastRatio(norm, '#ffffff');
          const ratioDark = contrastRatio(norm, '#1f2937');
          const warnings = [];
          if (ratioLight < 3.0) {
            warnings.push(`Low contrast against light background (${ratioLight.toFixed(1)}:1)`);
          }
          if (ratioDark < 3.0) {
            warnings.push(`Low contrast against dark background (${ratioDark.toFixed(1)}:1)`);
          }

          if (warnings.length > 0) {
            warningEl.textContent = `⚠ ${warnings.join('; ')}`;
          } else {
            warningEl.textContent = '';
          }
        }
      }

      hexInput.addEventListener('input', () => {
        updatePreview(hexInput.value);
      });

      // 16 Swatches
      const swatchesGrid = el('div', {
        className: 'color-swatches-grid',
        role: 'radiogroup',
        'aria-label': 'Preset colors',
        style: {
          display: 'grid',
          gridTemplateColumns: 'repeat(8, 1fr)',
          gap: '8px',
          margin: '12px 0'
        }
      });

      for (const preset of PRESET_PAIRS) {
        const swatchBtn = el('button', {
          type: 'button',
          className: 'swatch-btn',
          role: 'radio',
          'aria-label': preset.name,
          title: `${preset.name} (${preset.bg})`,
          style: {
            width: '28px',
            height: '28px',
            borderRadius: '4px',
            backgroundColor: preset.bg,
            border: selectedColor.toLowerCase() === preset.bg.toLowerCase() ? '2px solid var(--color-primary, #3b82f6)' : '1px solid rgba(0,0,0,0.2)',
            cursor: 'pointer'
          },
          onClick: () => {
            selectedColor = preset.bg;
            hexInput.value = preset.bg;
            updatePreview(preset.bg);
            // Highlight selected swatch
            swatchesGrid.querySelectorAll('.swatch-btn').forEach(btn => {
              btn.style.border = '1px solid rgba(0,0,0,0.2)';
            });
            swatchBtn.style.border = '2px solid var(--color-primary, #3b82f6)';
          }
        });
        swatchesGrid.appendChild(swatchBtn);
      }

      const bodyEl = el('div', { className: 'dialog-body' }, [
        previewRow,
        el('label', { className: 'form-label' }, 'Preset swatches:'),
        swatchesGrid,
        el('div', { style: { marginTop: '12px' } }, [
          el('label', { className: 'form-label' }, 'Hex code:'),
          hexInput,
          warningEl
        ])
      ]);

      const cancelBtn = el('button', {
        className: 'btn',
        type: 'button',
        onClick: () => {
          dialog.close();
          resolve(null);
        }
      }, 'Cancel');

      const saveBtn = el('button', {
        className: 'btn btn-primary',
        type: 'button',
        onClick: () => {
          const norm = normalizeHex(selectedColor);
          dialog.close();
          resolve(norm || selectedColor);
        }
      }, 'Save');

      const footerEl = el('div', { className: 'dialog-footer' }, cancelBtn, saveBtn);

      dialog.appendChild(titleEl);
      dialog.appendChild(bodyEl);
      dialog.appendChild(footerEl);

      dialog.addEventListener('cancel', (e) => {
        e.preventDefault();
        dialog.close();
        resolve(null);
      }, { once: true });

      dialog.showModal();
      hexInput.focus();
      updatePreview(selectedColor);
    });
  }

  /**
   * Accessible Delete Tag confirmation dialog.
   * Shows image count, checkbox "Don't ask again", Enter confirms, Esc cancels.
   */
  promptTagDelete({ tagName, imageCount = 0 }) {
    const settings = store.get('settings') || {};
    // If confirmBeforeDelete is false, don't ask
    if (settings?.general?.confirmBeforeDelete === false) {
      return Promise.resolve({ confirmed: true, dontAskAgain: false });
    }

    return new Promise((resolve) => {
      const dialog = this._getOrCreateDialog();
      clear(dialog);

      const titleEl = el('div', { className: 'dialog-header' }, 'Delete Tag');

      const countMsg = imageCount === 1
        ? '1 image is tagged with this tag.'
        : `${imageCount} images are tagged with this tag.`;

      const dontAskCheckbox = el('input', {
        type: 'checkbox',
        id: 'dont-ask-delete-tag',
        style: { marginRight: '6px' }
      });

      const bodyEl = el('div', { className: 'dialog-body' }, [
        el('p', {}, `Are you sure you want to delete tag "${tagName}"?`),
        el('p', { style: { color: 'var(--color-text-muted, #64748b)', fontSize: '0.9rem' } }, `${countMsg} It will be removed from all images.`),
        el('div', { style: { marginTop: '12px', display: 'flex', alignItems: 'center' } }, [
          dontAskCheckbox,
          el('label', { htmlFor: 'dont-ask-delete-tag', style: { cursor: 'pointer', fontSize: '0.9rem' } }, "Don't ask again")
        ])
      ]);

      const cancelBtn = el('button', {
        className: 'btn',
        type: 'button',
        onClick: () => {
          dialog.close();
          resolve({ confirmed: false, dontAskAgain: false });
        }
      }, 'Cancel');

      const deleteBtn = el('button', {
        className: 'btn btn-danger',
        type: 'button',
        onClick: () => {
          const dontAsk = dontAskCheckbox.checked;
          if (dontAsk) {
            const cur = store.get('settings') || {};
            if (!cur.general) cur.general = {};
            cur.general.confirmBeforeDelete = false;
            store.set('settings', cur);
          }
          dialog.close();
          resolve({ confirmed: true, dontAskAgain: dontAsk });
        }
      }, 'Delete');

      const footerEl = el('div', { className: 'dialog-footer' }, cancelBtn, deleteBtn);

      dialog.appendChild(titleEl);
      dialog.appendChild(bodyEl);
      dialog.appendChild(footerEl);

      dialog.addEventListener('cancel', (e) => {
        e.preventDefault();
        dialog.close();
        resolve({ confirmed: false, dontAskAgain: false });
      }, { once: true });

      dialog.showModal();
      deleteBtn.focus();
    });
  }
}

export const dialog = new DialogService();
