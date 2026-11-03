/**
 * Thumbnail size selection and retry logic.
 */

export const STANDARD_THUMBNAIL_SIZES = [64, 128, 256];
export const RETRY_DELAYS_MS = [1000, 2000, 4000, 8000, 8000, 8000];

export function pickThumbnailSize(cellSize, dpr = 1) {
  const targetPx = cellSize * (dpr > 0 ? dpr : 1);
  if (targetPx <= 64) return 64;
  if (targetPx <= 128) return 128;
  return 256;
}

export function getRetryDelay(attempt) {
  if (attempt < 0) return RETRY_DELAYS_MS[0];
  if (attempt >= RETRY_DELAYS_MS.length) return RETRY_DELAYS_MS[RETRY_DELAYS_MS.length - 1];
  return RETRY_DELAYS_MS[attempt];
}

export class ThumbnailManager {
  constructor() {
    this._activeRetries = new Map(); // key -> timeoutId
    this._pendingImages = new Set(); // imageId
  }

  cancel(key) {
    if (this._activeRetries.has(key)) {
      clearTimeout(this._activeRetries.get(key));
      this._activeRetries.delete(key);
    }
  }

  cancelAll() {
    for (const timeoutId of this._activeRetries.values()) {
      clearTimeout(timeoutId);
    }
    this._activeRetries.clear();
    this._pendingImages.clear();
  }

  trackPending(imageId) {
    this._pendingImages.add(imageId);
  }

  isPending(imageId) {
    return this._pendingImages.has(imageId);
  }

  clearPending(imageId) {
    this._pendingImages.delete(imageId);
  }

  scheduleRetry(key, attempt, callback) {
    this.cancel(key);
    if (attempt >= RETRY_DELAYS_MS.length) return false;

    const delay = getRetryDelay(attempt);
    const timeoutId = setTimeout(() => {
      this._activeRetries.delete(key);
      callback();
    }, delay);

    this._activeRetries.set(key, timeoutId);
    return true;
  }
}

