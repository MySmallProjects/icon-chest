import { api, ApiError } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { store } from '../lib/store.js';

let restartBannerContainer = null;

export function setRestartBannerContainer(container) {
  restartBannerContainer = container;
}

export function showRestartBanner(message) {
  if (!restartBannerContainer) return;
  restartBannerContainer.innerHTML = '';
  const banner = document.createElement('div');
  banner.className = 'banner banner-warning';
  banner.textContent = `Restart Required: ${message}`;
  restartBannerContainer.appendChild(banner);
}

/**
 * Saves a setting change via PATCH /api/settings with debounce and error handling.
 */
export async function saveSetting(patchObject, errorElement = null, revertFn = null) {
  if (errorElement) {
    errorElement.textContent = '';
  }

  try {
    const res = await api.patch('/api/settings', patchObject);
    if (res?.restartRequired && (Array.isArray(res.restartRequired) ? res.restartRequired.length > 0 : true)) {
      showRestartBanner('One or more modified settings will take effect after restarting IconChest.');
    }
    // Update store cache
    if (res?.settings) {
      store.set('settings', res.settings);
    } else {
      const current = store.get('settings') || {};
      store.set('settings', { ...current, ...patchObject });
    }
    return res;
  } catch (err) {
    if (err instanceof ApiError) {
      const msg = err.errors?.settings || err.detail || 'Failed to save setting.';
      if (errorElement) {
        errorElement.textContent = msg;
      } else {
        toast.error(msg);
      }
    } else {
      toast.error('Network error saving setting.');
    }

    if (revertFn) {
      revertFn();
    }
    throw err;
  }
}

