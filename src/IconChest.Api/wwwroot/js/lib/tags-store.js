import { api } from './api.js';
import { eventStream } from './events.js';

/**
 * In-memory cache and state manager for tags (/api/tags).
 * Supports subscriber pattern, auto-refresh on library.changed SSE events,
 * and optimistic local mutations with rollback on failure.
 */
class TagsStore {
  constructor() {
    this._tags = [];
    this._listeners = new Set();
    this._isInitialized = false;
    this._fetchPromise = null;
  }

  init() {
    if (this._isInitialized) return;
    this._isInitialized = true;

    eventStream.on('library.changed', (data) => {
      // Refresh tags if library changed
      this.fetchTags().catch(() => {});
    });

    eventStream.on('resync', () => {
      this.fetchTags().catch(() => {});
    });
  }

  getTags() {
    return [...this._tags];
  }

  getTagById(id) {
    const numId = Number(id);
    return this._tags.find(t => Number(t.id) === numId) || null;
  }

  subscribe(listener) {
    this._listeners.add(listener);
    // Call immediately if tags already loaded
    if (this._tags.length > 0) {
      try {
        listener(this.getTags());
      } catch (err) {
        console.error('Error in initial tags listener', err);
      }
    }
    return () => this._listeners.delete(listener);
  }

  _notify() {
    const tags = this.getTags();
    for (const listener of this._listeners) {
      try {
        listener(tags);
      } catch (err) {
        console.error('Error in tags listener', err);
      }
    }
  }

  async fetchTags() {
    if (this._fetchPromise) return this._fetchPromise;

    this._fetchPromise = (async () => {
      try {
        const result = await api.get('/api/tags');
        this._tags = Array.isArray(result) ? result : [];
        this._notify();
        return this.getTags();
      } finally {
        this._fetchPromise = null;
      }
    })();

    return this._fetchPromise;
  }

  async createTag(name, color = null) {
    const payload = { name };
    if (color) {
      payload.color = color;
      payload.bgColor = color;
    }

    const newTag = await api.post('/api/tags', payload);
    // Add to local list and notify
    const existingIdx = this._tags.findIndex(t => Number(t.id) === Number(newTag.id));
    if (existingIdx !== -1) {
      this._tags[existingIdx] = newTag;
    } else {
      this._tags.push(newTag);
    }
    this._notify();
    return newTag;
  }

  async renameTag(id, newName) {
    const numId = Number(id);
    const tag = this._tags.find(t => Number(t.id) === numId);
    if (!tag) {
      // If not in cache, try direct API call
      return await api.patch(`/api/tags/${numId}`, { name: newName });
    }

    const oldName = tag.name;
    // Optimistic update
    tag.name = newName;
    this._notify();

    try {
      const updated = await api.patch(`/api/tags/${numId}`, { name: newName });
      if (updated) {
        Object.assign(tag, updated);
      }
      return tag;
    } catch (err) {
      // Rollback
      tag.name = oldName;
      this._notify();
      throw err;
    }
  }

  async recolorTag(id, newColor) {
    const numId = Number(id);
    const tag = this._tags.find(t => Number(t.id) === numId);
    if (!tag) {
      return await api.patch(`/api/tags/${numId}`, { color: newColor, bgColor: newColor });
    }

    const oldColor = tag.color || tag.bgColor;
    // Optimistic update
    tag.color = newColor;
    tag.bgColor = newColor;
    this._notify();

    try {
      const updated = await api.patch(`/api/tags/${numId}`, { color: newColor, bgColor: newColor });
      if (updated) {
        Object.assign(tag, updated);
      }
      return tag;
    } catch (err) {
      // Rollback
      tag.color = oldColor;
      tag.bgColor = oldColor;
      this._notify();
      throw err;
    }
  }

  async deleteTag(id) {
    const numId = Number(id);
    const idx = this._tags.findIndex(t => Number(t.id) === numId);
    let removed = null;
    if (idx !== -1) {
      removed = this._tags[idx];
      this._tags.splice(idx, 1);
      this._notify();
    }

    try {
      await api.delete(`/api/tags/${numId}`);
    } catch (err) {
      // Rollback if needed
      if (removed) {
        this._tags.splice(idx, 0, removed);
        this._notify();
      }
      throw err;
    }
  }
}

export const tagsStore = new TagsStore();

