import { el, clear, createSvg } from './lib/dom.js';
import { api, ApiError } from './lib/api.js';
import { store } from './lib/store.js';
import { dialog } from './lib/dialog.js';
import { eventStream } from './lib/events.js';
import { initResizer } from './lib/resizer.js';
import { toast } from './lib/toast.js';
import { bridge } from './lib/bridge.js';

import { createFoldersView } from './views/folders.js';
import { createYearsView } from './views/years.js';
import { createTagsView } from './views/tags.js';
import { createFavouritesView } from './views/favourites.js';
import { createDuplicatesView } from './views/duplicates.js';
import { createMissingView } from './views/missing.js';
import { createHelpView } from './views/help.js';
import { createSettingsView } from './views/settings.js';

// SVG Icons for the Icon Bar
const ICONS = {
  folders: '<svg viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>',
  years: '<svg viewBox="0 0 24 24"><path d="M19 3h-1V1h-2v2H8V1H6v2H5c-1.11 0-1.99.9-1.99 2L3 19c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V8h14v11zM7 10h5v5H7z"/></svg>',
  tags: '<svg viewBox="0 0 24 24"><path d="M21.41 11.58l-9-9C12.05 2.22 11.55 2 11 2H4c-1.1 0-2 .9-2 2v7c0 .55.22 1.05.59 1.42l9 9c.36.36.86.58 1.41.58.55 0 1.05-.22 1.41-.59l7-7c.37-.36.59-.86.59-1.41 0-.55-.23-1.06-.59-1.42zM5.5 7C4.67 7 4 6.33 4 5.5S4.67 4 5.5 4 7 4.67 7 5.5 6.33 7 5.5 7z"/></svg>',
  favourites: '<svg viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>',
  duplicates: '<svg viewBox="0 0 24 24"><path d="M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z"/></svg>',
  missing: '<svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>',
  help: '<svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 16h-2v-2h2v2zm1.07-7.75l-.9.92C12.45 11.9 12 12.5 12 14h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H7c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.04-.42 1.99-1.07 2.75z"/></svg>',
  settings: '<svg viewBox="0 0 24 24"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>'
};

// Icon Bar View Registry
const VIEW_REGISTRY = [
  { id: 'folders', title: 'Folders', icon: ICONS.folders, group: 'top', hasPanelA: true, createView: createFoldersView },
  { id: 'years', title: 'Years', icon: ICONS.years, group: 'top', hasPanelA: true, createView: createYearsView },
  { id: 'tags', title: 'Tags', icon: ICONS.tags, group: 'top', hasPanelA: true, createView: createTagsView },
  { id: 'favourites', title: 'Favourites', icon: ICONS.favourites, group: 'top', hasPanelA: false, createView: createFavouritesView },
  { id: 'duplicates', title: 'Duplicates', icon: ICONS.duplicates, group: 'top', hasPanelA: false, createView: createDuplicatesView },
  { id: 'missing', title: 'Missing', icon: ICONS.missing, group: 'top', hasPanelA: false, createView: createMissingView },
  { id: 'help', title: 'Help', icon: ICONS.help, group: 'bottom', hasPanelA: false, createView: createHelpView },
  { id: 'settings', title: 'Settings', icon: ICONS.settings, group: 'bottom', hasPanelA: false, createView: createSettingsView }
];

class IconChestApp {
  constructor() {
    this.rootEl = null;
    this.panelAEl = null;
    this.resizerEl = null;
    this.panelBEl = null;
    this.views = new Map(); // id -> viewInstance
    this.iconButtons = new Map(); // id -> buttonEl
  }

  async start() {
    this._preventFileDropNavigation();
    this._initNativeBridge();

    // Check session authentication
    const authSuccess = await this._authenticate();
    if (!authSuccess) {
      this._renderLoginScreen();
      return;
    }

    await this._loadInitialState();
    this._buildLayout();
    this._initEvents();

    window.addEventListener('hashchange', () => this._handleRoute());
    this._handleRoute();
  }

  _initNativeBridge() {
    bridge.init();
    store.set('isNativeBridgeAvailable', bridge.isAvailable());
  }

  _preventFileDropNavigation() {
    window.addEventListener('dragover', (e) => e.preventDefault(), false);
    window.addEventListener('drop', (e) => e.preventDefault(), false);
  }

  async _authenticate() {
    // 1. Check for Browser / Window Handoff Fragment (#handoff=<secret>)
    let handoffSecret = null;
    const hash = window.location.hash;
    if (hash.includes('handoff=')) {
      const match = hash.match(/handoff=([^&]+)/);
      if (match && match[1]) {
        handoffSecret = decodeURIComponent(match[1]);
        // Clear fragment immediately from URL and history so secret is not exposed and routing is not corrupted
        history.replaceState(null, '', window.location.pathname + window.location.search);
      }
    }

    // 2. Session check
    const session = await api.checkSession();
    if (session.authenticated) {
      return true;
    }

    // 3. Login with handoff secret if present
    if (handoffSecret) {
      try {
        await api.loginWithSecret(handoffSecret);
        return true;
      } catch {}
    }

    // 4. Desktop Photino Message Bridge
    if (window.external && typeof window.external.sendMessage === 'function') {
      try {
        const secret = await new Promise((resolve, reject) => {
          const timeout = setTimeout(() => reject(new Error('Bridge timeout')), 3000);

          if (typeof window.external.receiveMessage === 'function') {
            window.external.receiveMessage((msg) => {
              try {
                const data = JSON.parse(msg);
                if (data.type === 'secret') {
                  clearTimeout(timeout);
                  resolve(data.secret);
                }
              } catch {
                if (msg && typeof msg === 'string') {
                  clearTimeout(timeout);
                  resolve(msg);
                }
              }
            });
          }

          const handler = (event) => {
            window.removeEventListener('iconchest-secret', handler);
            clearTimeout(timeout);
            resolve(event.detail);
          };
          window.addEventListener('iconchest-secret', handler);
          window.external.sendMessage(JSON.stringify({ type: 'getSecret' }));
        });

        if (secret) {
          await api.loginWithSecret(secret);
          return true;
        }
      } catch {}
    }

    return false;
  }

  _renderLoginScreen() {
    const root = document.getElementById('app-root') || document.body;
    clear(root);

    const passwordInput = el('input', {
      type: 'password',
      className: 'input',
      placeholder: 'Enter application password',
      id: 'login-password',
      autocomplete: 'current-password'
    });

    const errorDiv = el('div', { className: 'form-error', id: 'login-error' });
    const throttleDiv = el('div', { className: 'form-help', style: { color: 'var(--status-warning)' } });

    const submitBtn = el('button', {
      type: 'button',
      className: 'btn btn-primary',
      style: { width: '100%', marginTop: '16px' },
      onClick: async () => {
        errorDiv.textContent = '';
        throttleDiv.textContent = '';
        submitBtn.disabled = true;

        const password = passwordInput.value;
        if (!password) {
          errorDiv.textContent = 'Password is required.';
          submitBtn.disabled = false;
          return;
        }

        try {
          await api.loginWithPassword(password);
          this.start();
        } catch (err) {
          submitBtn.disabled = false;
          if (err instanceof ApiError) {
            if (err.status === 429) {
              errorDiv.textContent = 'Too many failed login attempts.';
              throttleDiv.textContent = err.detail || 'Please wait before trying again.';
            } else {
              errorDiv.textContent = err.detail || 'Invalid password.';
            }
          } else {
            errorDiv.textContent = 'Network error attempting login.';
          }
        }
      }
    }, 'Sign In');

    passwordInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') submitBtn.click();
    });

    const loginCard = el('div', {
      className: 'card',
      style: { maxWidth: '380px', margin: '15vh auto', padding: '32px' }
    },
      el('h1', { style: { fontSize: '1.4rem', fontWeight: '600', marginBottom: '8px' } }, 'IconChest'),
      el('p', { style: { color: 'var(--text-secondary)', fontSize: '0.85rem', marginBottom: '24px' } },
        'Please authenticate to access your local image library.'),
      el('div', { className: 'form-group' },
        el('label', { htmlFor: 'login-password' }, 'Password'),
        passwordInput,
        errorDiv,
        throttleDiv
      ),
      submitBtn
    );

    root.appendChild(loginCard);
    passwordInput.focus();
  }

  async _loadInitialState() {
    try {
      const [settings, systemInfo] = await Promise.all([
        api.get('/api/settings'),
        api.get('/api/system/info')
      ]);

      store.set('settings', settings);
      store.set('systemInfo', systemInfo);

      const theme = (settings?.appearance?.theme || settings?.theme || 'dark').toLowerCase();
      document.documentElement.setAttribute('data-theme', theme);
      document.documentElement.style.colorScheme = theme;
      store.set('theme', theme);
      try { localStorage.setItem('iconchest_theme', theme); } catch {}
    } catch (err) {
      console.warn('Could not load initial settings/diagnostics:', err);
    }
  }

  _buildLayout() {
    const root = document.getElementById('app-root');
    clear(root);

    // 1. Icon Bar
    const iconBar = el('nav', {
      id: 'icon-bar',
      role: 'toolbar',
      'aria-orientation': 'vertical',
      'aria-label': 'Main Views'
    });

    const topGroup = el('div', { className: 'icon-bar-group' });
    const bottomGroup = el('div', { className: 'icon-bar-group' });

    for (const item of VIEW_REGISTRY) {
      const btn = el('button', {
        className: 'icon-bar-btn',
        id: `nav-btn-${item.id}`,
        type: 'button',
        title: item.title,
        'aria-label': item.title,
        onClick: () => this._onIconClick(item.id)
      });
      btn.appendChild(createSvg(item.icon));

      if (item.id === 'missing') {
        const badge = el('span', { className: 'nav-badge hidden', id: 'nav-badge-missing' });
        btn.appendChild(badge);
      }

      this.iconButtons.set(item.id, btn);
      if (item.group === 'bottom') {
        bottomGroup.appendChild(btn);
      } else {
        topGroup.appendChild(btn);
      }
    }

    iconBar.appendChild(topGroup);
    iconBar.appendChild(bottomGroup);

    // Arrow Key Navigation in Icon Bar
    iconBar.addEventListener('keydown', (e) => {
      const allButtons = Array.from(this.iconButtons.values());
      const currentIndex = allButtons.indexOf(document.activeElement);
      if (currentIndex === -1) return;

      if (e.key === 'ArrowDown') {
        const next = allButtons[(currentIndex + 1) % allButtons.length];
        next.focus();
        e.preventDefault();
      } else if (e.key === 'ArrowUp') {
        const prev = allButtons[(currentIndex - 1 + allButtons.length) % allButtons.length];
        prev.focus();
        e.preventDefault();
      }
    });

    // 2. Working Area
    const workArea = el('div', { id: 'work-area' });

    this.panelAEl = el('aside', { id: 'panel-a', 'aria-label': 'Navigation sidebar' });
    this.resizerEl = el('div', { id: 'pane-resizer' });
    this.panelBEl = el('main', { id: 'panel-b' });

    workArea.appendChild(this.panelAEl);
    workArea.appendChild(this.resizerEl);
    workArea.appendChild(this.panelBEl);

    initResizer(this.resizerEl, this.panelAEl);

    // 3. Status Bar
    const statusBar = el('footer', { id: 'status-bar' });
    const statusLeft = el('div', { className: 'status-left', id: 'status-left' },
      el('span', { className: 'status-indicator', id: 'sse-indicator', title: 'SSE Connection' }),
      el('span', { id: 'status-scan-text' }, 'Ready')
    );
    const statusRight = el('div', { className: 'status-right', id: 'status-right' },
      el('span', { id: 'status-thumb-text', style: { display: 'none' } })
    );
    statusBar.appendChild(statusLeft);
    statusBar.appendChild(statusRight);

    // Main layout assembly
    const mainContainer = el('div', { id: 'main-container' });
    mainContainer.appendChild(iconBar);
    mainContainer.appendChild(workArea);

    root.appendChild(mainContainer);
    root.appendChild(statusBar);

    // Maintenance overlay
    const maintenanceOverlay = el('div', {
      id: 'maintenance-overlay',
      className: 'maintenance-overlay hidden',
      role: 'alert',
      'aria-live': 'assertive'
    },
      el('div', { className: 'maintenance-card' },
        el('div', { className: 'spinner-icon' }),
        el('p', { className: 'maintenance-title' }, 'Updating the library, one moment…')
      )
    );
    root.appendChild(maintenanceOverlay);

    api.onMaintenance = (active) => {
      maintenanceOverlay.classList.toggle('hidden', !active);
    };

    this.updateMissingBadge();
  }

  _initEvents() {
    eventStream.connect();

    const indicator = document.getElementById('sse-indicator');
    const scanText = document.getElementById('status-scan-text');
    const thumbText = document.getElementById('status-thumb-text');

    eventStream.onStatus((status) => {
      if (!indicator) return;
      indicator.className = `status-indicator ${status !== 'connected' ? status : ''}`;
      indicator.title = `SSE Connection: ${status}`;
      store.set('connectionStatus', status);
    });

    eventStream.on('scan.progress', (data) => {
      if (scanText) {
        scanText.textContent = `Scanning: ${data.filesFound || 0} files found`;
      }
    });

    eventStream.on('scan.completed', () => {
      if (scanText) {
        scanText.textContent = 'Scan completed.';
        setTimeout(() => { scanText.textContent = 'Ready'; }, 4000);
      }
      this.updateMissingBadge();
      this._reloadActiveView();
    });

    eventStream.on('thumb.progress', (data) => {
      if (thumbText) {
        thumbText.style.display = 'inline';
        thumbText.textContent = `Thumbnails: ${data.completed || 0} of ${data.total || 0}`;
      }
    });

    eventStream.on('thumb.completed', () => {
      if (thumbText) {
        thumbText.style.display = 'none';
      }
    });

    eventStream.on('data.reloaded', () => {
      window.dispatchEvent(new CustomEvent('iconchest-data-reloaded'));
      this.updateMissingBadge();
      this._reloadActiveView();
    });

    eventStream.on('library.changed', () => {
      window.dispatchEvent(new CustomEvent('iconchest-library-changed'));
      this.updateMissingBadge();
      this._reloadActiveView();
    });

    window.addEventListener('iconchest-data-reloaded', () => {
      this.updateMissingBadge();
      this._reloadActiveView();
    });
  }

  async updateMissingBadge() {
    try {
      const res = await api.get('/api/missing?limit=1');
      const count = res?.total ?? 0;
      const badge = document.getElementById('nav-badge-missing');
      if (badge) {
        if (count > 0) {
          badge.textContent = count > 99 ? '99+' : String(count);
          badge.classList.remove('hidden');
        } else {
          badge.classList.add('hidden');
        }
      }
    } catch {}
  }

  _reloadActiveView() {
    const currentView = store.get('activeView');
    const viewInstance = this.views.get(currentView);
    if (typeof viewInstance?.reload === 'function') {
      viewInstance.reload();
    } else if (typeof viewInstance?.refresh === 'function') {
      viewInstance.refresh();
    } else if (typeof viewInstance?.onActivated === 'function') {
      viewInstance.onActivated();
    }
  }

  async _onIconClick(viewId) {
    const currentView = store.get('activeView');
    const curInst = this.views.get(currentView);

    if (curInst?.hasUnsavedChanges?.()) {
      const action = await dialog.confirmThree({
        title: 'Unsaved Changes',
        message: 'You have unsaved changes. Do you want to save them before leaving this view?',
        saveText: 'Save',
        discardText: 'Discard',
        cancelText: 'Cancel'
      });
      if (action === 'save') {
        const detailsPanel = curInst.getDetailsPanel?.();
        if (detailsPanel) {
          const ok = await detailsPanel.save();
          if (!ok) return;
        }
      } else if (action === 'discard') {
        const detailsPanel = curInst.getDetailsPanel?.();
        if (detailsPanel) detailsPanel.revert();
      } else {
        return; // Cancel navigation
      }
    }

    const reg = VIEW_REGISTRY.find(v => v.id === viewId);

    if (currentView === viewId && reg?.hasPanelA) {
      // Toggle Panel A collapse on active icon click
      const collapsed = !store.get('isPanelACollapsed');
      store.set('isPanelACollapsed', collapsed);
      this._updatePanelAVisibility(reg);
      return;
    }

    window.location.hash = `#${viewId}`;
  }

  _handleRoute() {
    try {
      let hash = window.location.hash.replace(/^#/, '');
      if (!hash || hash.startsWith('handoff=')) {
        const settings = store.get('settings');
        const defaultView = (settings?.behaviour?.defaultView || settings?.defaultView || 'folders').toLowerCase();
        hash = defaultView;
        window.location.hash = `#${defaultView}`;
      }

      const [viewId, subParam] = hash.split('/');
      const reg = VIEW_REGISTRY.find(v => v.id === viewId) || VIEW_REGISTRY[0];

      store.set('activeView', reg.id);

      // Update active icon bar button
      for (const [id, btn] of this.iconButtons) {
        if (id === reg.id) {
          btn.classList.add('active');
          btn.setAttribute('aria-current', 'page');
        } else {
          btn.classList.remove('active');
          btn.removeAttribute('aria-current');
        }
      }

      // Mount or activate view in Panel B
      let viewInstance = this.views.get(reg.id);
      if (!viewInstance) {
        viewInstance = reg.createView();
        this.views.set(reg.id, viewInstance);
        const viewEl = viewInstance.element || viewInstance.containerEl;
        if (viewEl) {
          this.panelBEl.appendChild(viewEl);
        }
      }

      // Hide other views, show active view
      for (const [id, inst] of this.views) {
        const viewEl = inst?.element || inst?.containerEl;
        if (viewEl) {
          if (id === reg.id) {
            viewEl.removeAttribute('hidden');
          } else {
            viewEl.setAttribute('hidden', '');
          }
        }
      }

      // If settings view, activate specific sub-section
      if (reg.id === 'settings' && viewInstance.showSection) {
        viewInstance.showSection(subParam || 'library');
      }

      // If help view, activate specific sub-section
      if (reg.id === 'help' && viewInstance.showSection) {
        viewInstance.showSection(subParam || 'shortcuts');
      }

      if (typeof viewInstance.onActivated === 'function') {
        viewInstance.onActivated();
      }

      // Update Panel A content and visibility
      this._updatePanelAVisibility(reg, viewInstance);
    } catch (err) {
      console.error('Routing error in IconChest:', err);
      if (typeof window.logClientError === 'function') {
        window.logClientError(`Routing error in IconChest: ${err.message}`, err.stack);
      }
    }
  }

  _updatePanelAVisibility(reg, viewInstance = null) {
    if (!viewInstance) {
      viewInstance = this.views.get(reg.id);
    }

    const isCollapsed = store.get('isPanelACollapsed');

    if (reg.hasPanelA && !isCollapsed) {
      this.panelAEl.removeAttribute('hidden');
      this.resizerEl.removeAttribute('hidden');

      if (viewInstance?.renderPanelA) {
        clear(this.panelAEl);
        const header = el('div', { className: 'panel-a-header' }, reg.title);
        this.panelAEl.appendChild(header);
        this.panelAEl.appendChild(viewInstance.renderPanelA());
      }
    } else {
      this.panelAEl.setAttribute('hidden', '');
      this.resizerEl.setAttribute('hidden', '');
    }
  }
}

// Boot application
const app = new IconChestApp();
document.addEventListener('DOMContentLoaded', () => {
  app.start();
});

