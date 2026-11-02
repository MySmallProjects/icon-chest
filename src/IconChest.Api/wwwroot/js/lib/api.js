/**
 * IconChest HTTP API client wrapper.
 * Handles problem-details errors, cookie-based sessions, desktop bridge handoff,
 * and 401 mid-session re-authentication without losing state.
 */

export class ApiError extends Error {
  constructor(status, title, detail, errors = {}, traceId = null) {
    super(detail || title || `HTTP Error ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.errors = errors;
    this.traceId = traceId;
  }
}

class ApiClient {
  constructor() {
    this.onAuthRequired = null;
    this.isReauthenticating = false;
    this._authPendingQueue = [];
  }

  async request(path, options = {}) {
    const url = path.startsWith('/') ? path : `/${path}`;
    const headers = {
      'Accept': 'application/json',
      ...(options.headers || {})
    };

    if (options.body && typeof options.body === 'object' && !(options.body instanceof FormData)) {
      headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(options.body);
    }

    const fetchOptions = {
      ...options,
      headers,
      credentials: 'same-origin'
    };

    let response;
    try {
      response = await fetch(url, fetchOptions);
    } catch (err) {
      if (err.name === 'AbortError') {
        throw err;
      }
      throw new ApiError(0, 'Network Error', 'Could not communicate with the IconChest API server.');
    }

    if (response.status === 401) {
      // Mid-session re-auth
      if (!url.startsWith('/api/session')) {
        return this._handle401(path, options);
      }
    }

    if (!response.ok) {
      let problem = null;
      try {
        problem = await response.json();
      } catch {
        // Not a JSON Problem Details response
      }

      const status = response.status;
      const title = problem?.title || response.statusText;
      const detail = problem?.detail || problem?.error || `Request failed with status ${status}`;
      const errors = problem?.errors || problem?.queryErrors || {};
      const traceId = problem?.traceId || null;

      throw new ApiError(status, title, detail, errors, traceId);
    }

    if (response.status === 204) {
      return null;
    }

    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      return await response.json();
    }
    return await response.text();
  }

  get(path, options = {}) {
    return this.request(path, { ...options, method: 'GET' });
  }

  post(path, body = null, options = {}) {
    return this.request(path, { ...options, method: 'POST', body });
  }

  patch(path, body = null, options = {}) {
    return this.request(path, { ...options, method: 'PATCH', body });
  }

  delete(path, options = {}) {
    return this.request(path, { ...options, method: 'DELETE' });
  }

  async checkSession() {
    try {
      return await this.get('/api/session');
    } catch {
      return { authenticated: false };
    }
  }

  async loginWithSecret(secret) {
    return await this.post('/api/session', { secret });
  }

  async loginWithPassword(password) {
    return await this.post('/api/session', { password });
  }

  async logout() {
    return await this.delete('/api/session');
  }

  async _handle401(path, options) {
    if (this.isReauthenticating) {
      return new Promise((resolve, reject) => {
        this._authPendingQueue.push({ path, options, resolve, reject });
      });
    }

    this.isReauthenticating = true;

    try {
      const reauthenticated = await this._tryAutoLogin();
      if (!reauthenticated && this.onAuthRequired) {
        await this.onAuthRequired();
      }

      this.isReauthenticating = false;

      // Retry original request
      const originalResult = await this.request(path, options);

      // Drain queued requests
      const queue = this._authPendingQueue.slice();
      this._authPendingQueue = [];
      for (const item of queue) {
        this.request(item.path, item.options).then(item.resolve).catch(item.reject);
      }

      return originalResult;
    } catch (err) {
      this.isReauthenticating = false;
      const queue = this._authPendingQueue.slice();
      this._authPendingQueue = [];
      for (const item of queue) {
        item.reject(err);
      }
      throw err;
    }
  }

  async _tryAutoLogin() {
    // 1. Check if Photino desktop bridge exists
    if (window.external && typeof window.external.sendMessage === 'function') {
      try {
        const secret = await new Promise((resolve, reject) => {
          const timeout = setTimeout(() => reject(new Error('Bridge timeout')), 3000);
          const handler = (event) => {
            window.removeEventListener('iconchest-secret', handler);
            clearTimeout(timeout);
            resolve(event.detail);
          };
          window.addEventListener('iconchest-secret', handler);
          window.external.sendMessage(JSON.stringify({ type: 'getSecret' }));
        });

        if (secret) {
          await this.loginWithSecret(secret);
          return true;
        }
      } catch {
        // Bridge call failed or timed out
      }
    }

    return false;
  }
}

export const api = new ApiClient();

