import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { dialog } from '../lib/dialog.js';
import { store } from '../lib/store.js';
import { saveSetting } from './helper.js';

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  if (!bytes || isNaN(bytes)) return '-';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

export function renderCleanupSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Cleanup & Maintenance'),
    el('p', {}, 'Manage missing files, hidden items, and reclaim disk space from orphaned thumbnails.')
  );
  container.appendChild(header);

  // 1. Missing Files Management
  const missingCountEl = el('span', { style: { fontWeight: '600' } }, 'Checking...');

  const btnViewMissing = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: () => {
      window.location.hash = '#missing';
    }
  }, 'View Missing Records');

  const btnDeleteAllMissing = el('button', {
    type: 'button',
    className: 'btn btn-danger',
    onClick: async () => {
      const ok = await dialog.confirm({
        title: 'Remove All Missing Records',
        message: 'This removes catalog records and cached thumbnails for all missing files. No files on disk will be deleted, moved or modified.',
        confirmText: 'Remove All Missing',
        isDanger: true
      });
      if (!ok) return;

      try {
        const res = await api.post('/api/missing/remove', {});
        toast.show(`Removed ${res?.removedCount ?? 0} missing records.`, 'success');
        // Run orphan thumbnail cleanup in background
        api.post('/api/thumbnails/cleanup-orphans').catch(() => {});
        refreshCounts();
      } catch (err) {
        toast.show(err.message || 'Failed to remove missing records', 'error');
      }
    }
  }, 'Delete All Missing Records');

  const autoRemoveCheckbox = el('input', { type: 'checkbox', id: 'setting-auto-remove-missing' });
  autoRemoveCheckbox.addEventListener('change', () => {
    saveSetting({
      autoRemoveMissing: autoRemoveCheckbox.checked,
      cleanup: { autoRemoveMissing: autoRemoveCheckbox.checked }
    }).catch(() => {});
  });

  const missingCard = el('div', { className: 'card' },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Missing Files'),
    el('p', { style: { marginBottom: '12px', color: 'var(--text-secondary)' } },
      'Files that were previously indexed but can no longer be located at their original paths on disk.'
    ),
    el('div', {
      style: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '12px 16px',
        background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
        borderRadius: 'var(--radius-md, 6px)',
        marginBottom: '16px'
      }
    },
      el('div', {},
        el('span', {}, 'Catalog status: '),
        missingCountEl
      ),
      el('div', { style: { display: 'flex', gap: '8px' } },
        btnViewMissing,
        btnDeleteAllMissing
      )
    ),
    el('div', { className: 'form-group' },
      el('label', { className: 'checkbox-label' },
        autoRemoveCheckbox,
        el('span', {}, 'Automatically remove missing records during library scans')
      ),
      el('div', { className: 'form-help' }, 'When disabled, missing records are kept so you can inspect what was deleted or moved.')
    )
  );

  // 2. Hidden Images Management
  const hiddenCountEl = el('span', { style: { fontWeight: '600' } }, 'Checking...');

  const btnViewHidden = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: () => {
      store.set('pendingFilterQuery', 'hidden == true');
      window.location.hash = '#folders';
    }
  }, 'View Hidden Images');

  const hiddenCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Hidden Images'),
    el('p', { style: { marginBottom: '12px', color: 'var(--text-secondary)' } },
      'Images you marked as hidden are filtered out of standard browsing views and duplicate calculations.'
    ),
    el('div', {
      style: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '12px 16px',
        background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
        borderRadius: 'var(--radius-md, 6px)'
      }
    },
      el('div', {},
        el('span', {}, 'Hidden records: '),
        hiddenCountEl
      ),
      btnViewHidden
    )
  );

  // 3. Orphan Thumbnail Reclaim
  const btnCleanOrphans = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        const res = await api.post('/api/thumbnails/cleanup-orphans');
        toast.show(`Orphans cleaned: ${res?.orphansRemoved ?? 0} files removed (${formatBytes(res?.bytesReclaimed || 0)} reclaimed).`, 'success');
      } catch (err) {
        toast.show(err.message || 'Orphan cleanup failed', 'error');
      }
    }
  }, 'Clean Up Orphaned Thumbnails');

  const orphansCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Orphaned Cache Files'),
    el('p', { style: { marginBottom: '12px', color: 'var(--text-secondary)' } },
      'Reclaim disk space occupied by cached thumbnails whose corresponding image records no longer exist in the catalog.'
    ),
    btnCleanOrphans
  );

  container.appendChild(missingCard);
  container.appendChild(hiddenCard);
  container.appendChild(orphansCard);

  async function refreshCounts() {
    try {
      const missingRes = await api.get('/api/missing?limit=1');
      const missingTotal = missingRes?.total ?? 0;
      missingCountEl.textContent = `${missingTotal} missing files found`;
      btnDeleteAllMissing.disabled = missingTotal === 0;
    } catch {
      missingCountEl.textContent = 'Error checking missing files';
    }

    try {
      const hiddenRes = await api.get('/api/images?q=hidden == true&limit=1');
      const hiddenTotal = hiddenRes?.total ?? 0;
      hiddenCountEl.textContent = `${hiddenTotal} images marked hidden`;
      btnViewHidden.disabled = false;
    } catch {
      hiddenCountEl.textContent = '0 images marked hidden';
      btnViewHidden.disabled = false;
    }
  }

  // Load existing settings
  api.get('/api/settings').then(s => {
    const autoRemove = s.cleanup?.autoRemoveMissing ?? s.autoRemoveMissing;
    autoRemoveCheckbox.checked = autoRemove === true;
  });

  refreshCounts();

  return container;
}
