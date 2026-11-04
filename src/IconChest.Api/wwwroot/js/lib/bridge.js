/**
 * Native bridge wrapper for Photino desktop shell communication.
 * Falls back gracefully when running in standard browser mode.
 */

class NativeBridge {
  constructor() {
    this.pending = new Map();
    this.reqId = 0;
    this._initialized = false;
  }

  isAvailable() {
    return Boolean(window.external && typeof window.external.sendMessage === 'function');
  }

  init() {
    if (this._initialized) return;
    this._initialized = true;

    if (window.external && typeof window.external.receiveMessage === 'function') {
      window.external.receiveMessage((msg) => {
        try {
          const data = typeof msg === 'string' ? JSON.parse(msg) : msg;
          if (!data || !data.type) return;

          // Dispatch standard events for compatibility
          if (data.type === 'folderPicked') {
            window.dispatchEvent(new CustomEvent('iconchest-folder-picked', { detail: data.path }));
          } else if (data.type === 'secret') {
            window.dispatchEvent(new CustomEvent('iconchest-secret', { detail: data.secret }));
          } else if (data.type === 'filePicked') {
            window.dispatchEvent(new CustomEvent('iconchest-file-picked', { detail: data.path }));
          } else if (data.type === 'saveFilePicked') {
            window.dispatchEvent(new CustomEvent('iconchest-save-file-picked', { detail: data.path }));
          } else if (data.type === 'fileSaved') {
            window.dispatchEvent(new CustomEvent('iconchest-file-saved', { detail: data.path }));
          }

          // Resolve pending promise if matching ID exists
          if (data.id && this.pending.has(data.id)) {
            const { resolve } = this.pending.get(data.id);
            this.pending.delete(data.id);
            resolve(data.path !== undefined ? data.path : data);
          }
        } catch (e) {
          console.warn('Bridge message error:', e, msg);
        }
      });
    }
  }

  _call(type, payload = {}) {
    if (!this.isAvailable()) return Promise.resolve(null);
    this.init();
    const id = 'req_' + (++this.reqId) + '_' + Date.now();
    return new Promise((resolve) => {
      this.pending.set(id, { resolve });
      try {
        window.external.sendMessage(JSON.stringify({ type, id, ...payload }));
      } catch (err) {
        this.pending.delete(id);
        resolve(null);
      }
    });
  }

  pickFolder(title = 'Select Folder') {
    return this._call('pickFolder', { title });
  }

  pickFile(title = 'Open File') {
    return this._call('pickFile', { title });
  }

  pickSaveFile(title = 'Save File', defaultName = '') {
    return this._call('pickSaveFile', { title, defaultName });
  }

  saveFile(content, defaultName = '', title = 'Save File') {
    return this._call('saveFile', { content, defaultName, title });
  }

  restartApi() {
    if (this.isAvailable()) {
      try {
        window.external.sendMessage(JSON.stringify({ type: 'restartApi' }));
      } catch (err) {
        console.error('Failed to trigger restartApi:', err);
      }
    }
  }
}

export const bridge = new NativeBridge();

