/**
 * Central reactive application store.
 */

class Store {
  constructor() {
    this.state = {
      theme: localStorage.getItem('iconchest_theme') || 'dark',
      activeView: 'folders',
      activeSettingsSection: 'library',
      panelAWidth: parseInt(localStorage.getItem('iconchest_panel_a_width') || '280', 10),
      isPanelACollapsed: localStorage.getItem('iconchest_panel_a_collapsed') === 'true',
      isScanning: false,
      scanProgress: null,
      thumbnailProgress: null,
      connectionStatus: 'connecting',
      settings: null,
      systemInfo: null,
      isNativeBridgeAvailable: Boolean(window.external && typeof window.external.sendMessage === 'function')
    };

    this.listeners = new Map();
  }

  get(key) {
    return this.state[key];
  }

  set(key, value) {
    if (this.state[key] === value) return;

    const oldValue = this.state[key];
    this.state[key] = value;

    // Persist specific keys
    if (key === 'theme') {
      try { localStorage.setItem('iconchest_theme', value); } catch {}
    } else if (key === 'panelAWidth') {
      try { localStorage.setItem('iconchest_panel_a_width', String(value)); } catch {}
    } else if (key === 'isPanelACollapsed') {
      try { localStorage.setItem('iconchest_panel_a_collapsed', String(value)); } catch {}
    }

    this._notify(key, value, oldValue);
  }

  update(partial) {
    for (const [k, v] of Object.entries(partial)) {
      this.set(k, v);
    }
  }

  subscribe(keyOrListener, maybeListener) {
    let key = keyOrListener;
    let listener = maybeListener;

    if (typeof keyOrListener === 'function') {
      listener = keyOrListener;
      key = '*';
    }

    if (!listener || typeof listener !== 'function') {
      return () => {};
    }

    if (!this.listeners.has(key)) {
      this.listeners.set(key, new Set());
    }
    this.listeners.get(key).add(listener);

    // Initial call for specific key
    if (key !== '*') {
      try {
        listener(this.state[key], undefined);
      } catch (err) {
        console.error(`Error in initial store listener for ${key}:`, err);
      }
    }

    return () => this.listeners.get(key)?.delete(listener);
  }

  _notify(key, value, oldValue) {
    const subs = this.listeners.get(key);
    if (subs) {
      for (const cb of subs) {
        try {
          cb(value, oldValue);
        } catch (err) {
          console.error(`Error in store listener for ${key}:`, err);
        }
      }
    }
    const wildcardSubs = this.listeners.get('*');
    if (wildcardSubs) {
      for (const cb of wildcardSubs) {
        try {
          cb(key, value, oldValue);
        } catch (err) {
          console.error(`Error in wildcard store listener for ${key}:`, err);
        }
      }
    }
  }
}

export const store = new Store();

