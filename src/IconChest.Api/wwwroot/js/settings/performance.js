import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { dialog } from '../lib/dialog.js';
import { bridge } from '../lib/bridge.js';
import { saveSetting } from './helper.js';

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  if (!bytes || isNaN(bytes)) return '-';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

export function renderPerformanceSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Performance & Cache'),
    el('p', {}, 'Manage thumbnail generation, on-disk cache limits, and worker thread concurrency.')
  );
  container.appendChild(header);

  // 1. Thumbnail Cache Location & Limit
  const cachePathInput = el('input', { type: 'text', className: 'input', id: 'setting-cache-path' });
  const openFolderBtn = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        await api.post('/api/native/open-folder', { target: 'thumbnails' });
      } catch (err) {
        toast.show(err.message || 'Failed to open cache folder', 'error');
      }
    }
  }, 'Open Folder');

  const cachePathActions = [openFolderBtn];

  if (bridge.isAvailable()) {
    const browseBtn = el('button', {
      type: 'button',
      className: 'btn btn-secondary',
      onClick: async () => {
        const picked = await bridge.pickFolder('Select Thumbnail Cache Directory');
        if (picked) {
          cachePathInput.value = picked;
          saveSetting({
            thumbnailCachePath: picked,
            performance: { thumbnailCachePath: picked }
          }).catch(() => {});
        }
      }
    }, 'Browse...');
    cachePathActions.unshift(browseBtn);
  }

  cachePathInput.addEventListener('change', () => {
    const val = cachePathInput.value.trim();
    if (val) {
      saveSetting({
        thumbnailCachePath: val,
        performance: { thumbnailCachePath: val }
      }).catch(() => {});
    }
  });

  const cacheSizeInput = el('input', {
    type: 'number',
    className: 'input',
    id: 'setting-cache-max-mb',
    min: '64',
    max: '1048576',
    step: '64',
    style: { width: '140px' }
  });

  cacheSizeInput.addEventListener('change', () => {
    let val = parseInt(cacheSizeInput.value, 10);
    if (isNaN(val) || val < 64) val = 64;
    if (val > 1048576) val = 1048576;
    cacheSizeInput.value = val;
    saveSetting({
      thumbnailCacheMaxMb: val,
      performance: { thumbnailCacheMaxMb: val }
    }).catch(() => {});
  });

  const preset500Btn = el('button', {
    type: 'button',
    className: 'btn btn-sm btn-secondary',
    onClick: () => {
      cacheSizeInput.value = 500;
      cacheSizeInput.dispatchEvent(new Event('change'));
    }
  }, '500 MB');

  const preset2GbBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm btn-secondary',
    onClick: () => {
      cacheSizeInput.value = 2048;
      cacheSizeInput.dispatchEvent(new Event('change'));
    }
  }, '2 GB');

  const preset8GbBtn = el('button', {
    type: 'button',
    className: 'btn btn-sm btn-secondary',
    onClick: () => {
      cacheSizeInput.value = 8192;
      cacheSizeInput.dispatchEvent(new Event('change'));
    }
  }, '8 GB');

  const statsContainer = el('div', {
    className: 'cache-stats-box',
    style: {
      padding: '12px 16px',
      background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
      borderRadius: 'var(--radius-md, 6px)',
      marginTop: '12px',
      fontSize: '0.9rem'
    }
  }, 'Loading cache statistics...');

  // Cache Actions
  const btnClearCache = el('button', {
    type: 'button',
    className: 'btn btn-danger',
    onClick: async () => {
      const ok = await dialog.confirm({
        title: 'Clear Thumbnail Cache',
        message: 'Are you sure you want to delete all cached thumbnails? Icons will be re-rendered on demand.',
        confirmText: 'Clear Cache',
        isDanger: true
      });
      if (!ok) return;

      try {
        await api.post('/api/thumbnails/clear');
        toast.show('Thumbnail cache cleared.', 'success');
        refreshStats();
      } catch (err) {
        toast.show(err.message || 'Failed to clear thumbnail cache', 'error');
      }
    }
  }, 'Clear Cache');

  const btnVerify = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        toast.show('Verifying cache integrity...', 'info');
        const res = await api.post('/api/thumbnails/verify');
        toast.show(`Verification complete: ${res?.verified ?? 0} checked, ${res?.repaired ?? 0} repaired.`, 'success');
        refreshStats();
      } catch (err) {
        toast.show(err.message || 'Verification failed', 'error');
      }
    }
  }, 'Verify Cache');

  const btnRetryFailed = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        const res = await api.post('/api/thumbnails/retry-failed');
        toast.show(`Retried failed thumbnails (${res?.count ?? 0} queued).`, 'success');
        refreshStats();
      } catch (err) {
        toast.show(err.message || 'Retry failed', 'error');
      }
    }
  }, 'Retry Failed');

  const btnCleanOrphans = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        const res = await api.post('/api/thumbnails/cleanup-orphans');
        toast.show(`Orphans cleaned: ${res?.orphansRemoved ?? 0} removed (${formatBytes(res?.bytesReclaimed || 0)} reclaimed).`, 'success');
        refreshStats();
      } catch (err) {
        toast.show(err.message || 'Orphan cleanup failed', 'error');
      }
    }
  }, 'Clean Up Orphans');

  const cacheCard = el('div', { className: 'card' },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Thumbnail Storage'),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-cache-path' }, 'Cache Directory Path'),
      el('div', { style: { display: 'flex', gap: '8px' } },
        cachePathInput,
        ...cachePathActions
      ),
      el('div', { className: 'form-help' }, 'Directory where rendered WebP thumbnail files are stored.')
    ),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-cache-max-mb' }, 'Maximum Cache Size (MB)'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        cacheSizeInput,
        el('span', { style: { color: 'var(--text-muted)' } }, 'MB'),
        preset500Btn,
        preset2GbBtn,
        preset8GbBtn
      ),
      el('div', { className: 'form-help' }, 'Limits total disk usage. Least recently accessed thumbnails are evicted first (64 MB to 1,048,576 MB).')
    ),
    statsContainer,
    el('div', { style: { display: 'flex', gap: '8px', marginTop: '16px', flexWrap: 'wrap' } },
      btnClearCache,
      btnVerify,
      btnRetryFailed,
      btnCleanOrphans
    )
  );

  // 2. Concurrency & Hash
  const scanWorkersInput = el('input', {
    type: 'number',
    className: 'input',
    id: 'setting-scan-workers',
    min: '0',
    max: '32',
    style: { width: '100px' }
  });

  scanWorkersInput.addEventListener('change', () => {
    let val = parseInt(scanWorkersInput.value, 10);
    if (isNaN(val) || val < 0) val = 0;
    if (val > 32) val = 32;
    scanWorkersInput.value = val;
    saveSetting({
      scanWorkers: val,
      performance: { scanWorkers: val }
    }).catch(() => {});
  });

  const thumbWorkersInput = el('input', {
    type: 'number',
    className: 'input',
    id: 'setting-thumb-workers',
    min: '0',
    max: '32',
    style: { width: '100px' }
  });

  thumbWorkersInput.addEventListener('change', () => {
    let val = parseInt(thumbWorkersInput.value, 10);
    if (isNaN(val) || val < 0) val = 0;
    if (val > 32) val = 32;
    thumbWorkersInput.value = val;
    saveSetting({
      thumbnailWorkers: val,
      performance: { thumbnailWorkers: val }
    }).catch(() => {});
  });

  const qualityInput = el('input', {
    type: 'number',
    className: 'input',
    id: 'setting-thumb-quality',
    min: '1',
    max: '100',
    style: { width: '100px' }
  });

  qualityInput.addEventListener('change', () => {
    let val = parseInt(qualityInput.value, 10);
    if (isNaN(val) || val < 1) val = 80;
    if (val > 100) val = 100;
    qualityInput.value = val;
    saveSetting({
      thumbnailQuality: val,
      performance: { thumbnailQuality: val }
    }).catch(() => {});
  });

  const workerCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Workers & Algorithms'),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-scan-workers' }, 'Library Scanner Workers'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        scanWorkersInput,
        el('span', { style: { color: 'var(--text-muted)' } }, 'threads (0 = auto-detect CPU cores)')
      )
    ),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-thumb-workers' }, 'Thumbnail Generator Workers'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        thumbWorkersInput,
        el('span', { style: { color: 'var(--text-muted)' } }, 'threads (0 = auto-detect CPU cores)')
      )
    ),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-thumb-quality' }, 'Thumbnail Encoding Quality (1-100)'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        qualityInput,
        el('span', { style: { color: 'var(--text-muted)' } }, '% (default 80)')
      )
    ),
    el('div', { className: 'form-group' },
      el('label', {}, 'Image Hash Algorithm'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        el('span', { className: 'badge', style: { fontFamily: 'var(--font-mono)', fontWeight: 'bold' } }, 'xxh128'),
        el('span', { style: { color: 'var(--text-muted)', fontSize: '0.85rem' } }, 'Fast, 128-bit non-cryptographic content hashing')
      )
    )
  );

  container.appendChild(cacheCard);
  container.appendChild(workerCard);

  async function refreshStats() {
    try {
      const stats = await api.get('/api/thumbnails/status');
      clear(statsContainer);
      const usedBytes = stats?.cacheSizeBytes ?? stats?.totalBytes ?? 0;
      const fileCount = stats?.cachedCount ?? stats?.fileCount ?? 0;
      const maxMb = stats?.maxCacheMb ?? 500;
      statsContainer.appendChild(el('div', {},
        el('strong', {}, 'Cache Status: '),
        el('span', {}, `${formatBytes(usedBytes)} used (${fileCount.toLocaleString()} thumbnails) of ${maxMb.toLocaleString()} MB allocated limit.`)
      ));
    } catch {
      clear(statsContainer);
      statsContainer.textContent = 'Unable to fetch cache statistics.';
    }
  }

  // Load current settings
  api.get('/api/settings').then(s => {
    cachePathInput.value = s.performance?.thumbnailCachePath || s.thumbnailCachePath || '';
    cacheSizeInput.value = s.performance?.thumbnailCacheMaxMb || s.thumbnailCacheMaxMb || 500;
    scanWorkersInput.value = s.performance?.scanWorkers ?? s.scanWorkers ?? 0;
    thumbWorkersInput.value = s.performance?.thumbnailWorkers ?? s.thumbnailWorkers ?? 0;
    qualityInput.value = s.performance?.thumbnailQuality ?? s.thumbnailQuality ?? 80;
  });

  refreshStats();

  return container;
}
