/**
 * Server-Sent Events (SSE) manager for IconChest `/api/events`.
 * Features automatic reconnect with exponential backoff, typed subscriptions,
 * connection status notifications, and resync dispatch upon reconnection.
 */

class EventStreamManager {
  constructor() {
    this.source = null;
    this.listeners = new Map();
    this.statusListeners = new Set();
    this.reconnectAttempts = 0;
    this.reconnectTimeout = null;
    this.isManualClose = false;
    this.hasConnectedOnce = false;
  }

  connect() {
    if (this.source) {
      return;
    }

    this.isManualClose = false;
    this._notifyStatus('connecting');

    try {
      this.source = new EventSource('/api/events');
    } catch (err) {
      this._scheduleReconnect();
      return;
    }

    this.source.onopen = () => {
      const wasReconnecting = this.hasConnectedOnce && this.reconnectAttempts > 0;
      this.reconnectAttempts = 0;
      this.hasConnectedOnce = true;
      this._notifyStatus('connected');

      if (wasReconnecting) {
        this.emit('resync', { timestamp: new Date().toISOString() });
      }
    };

    this.source.onerror = () => {
      if (this.source) {
        this.source.close();
        this.source = null;
      }
      this._notifyStatus('disconnected');
      if (!this.isManualClose) {
        this._scheduleReconnect();
      }
    };

    // Standard events from the API
    const knownEvents = [
      'scan.progress',
      'scan.completed',
      'scan.failed',
      'thumb.progress',
      'thumb.completed',
      'thumb.failed',
      'settings.changed',
      'library.folder_added',
      'library.folder_removed',
      'library.folder_updated'
    ];

    for (const evtName of knownEvents) {
      this.source.addEventListener(evtName, (event) => {
        try {
          const data = JSON.parse(event.data);
          this.emit(evtName, data);
        } catch {
          this.emit(evtName, event.data);
        }
      });
    }

    // Default message handler
    this.source.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        this.emit('message', data);
      } catch {
        this.emit('message', event.data);
      }
    };
  }

  close() {
    this.isManualClose = true;
    if (this.reconnectTimeout) {
      clearTimeout(this.reconnectTimeout);
      this.reconnectTimeout = null;
    }
    if (this.source) {
      this.source.close();
      this.source = null;
    }
    this._notifyStatus('disconnected');
  }

  on(eventName, callback) {
    if (!this.listeners.has(eventName)) {
      this.listeners.set(eventName, new Set());
    }
    this.listeners.get(eventName).add(callback);
    return () => this.off(eventName, callback);
  }

  off(eventName, callback) {
    const set = this.listeners.get(eventName);
    if (set) {
      set.delete(callback);
      if (set.size === 0) {
        this.listeners.delete(eventName);
      }
    }
  }

  onStatus(callback) {
    this.statusListeners.add(callback);
    return () => this.statusListeners.delete(callback);
  }

  emit(eventName, data) {
    const set = this.listeners.get(eventName);
    if (set) {
      for (const cb of set) {
        try {
          cb(data);
        } catch (err) {
          console.error(`Error in event listener for ${eventName}:`, err);
        }
      }
    }
  }

  _notifyStatus(status) {
    for (const cb of this.statusListeners) {
      try {
        cb(status);
      } catch (err) {
        console.error('Error in status listener:', err);
      }
    }
  }

  _scheduleReconnect() {
    if (this.reconnectTimeout) return;

    this.reconnectAttempts++;
    // Backoff: 1s, 2s, 4s, 8s, up to 15s max
    const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts - 1), 15000);

    this.reconnectTimeout = setTimeout(() => {
      this.reconnectTimeout = null;
      this.connect();
    }, delay);
  }
}

export const eventStream = new EventStreamManager();

