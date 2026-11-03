import { el, clear } from '../lib/dom.js';
import { api, ApiError } from '../lib/api.js';
import { eventStream } from '../lib/events.js';
import { dialog } from '../lib/dialog.js';
import { toast } from '../lib/toast.js';
import { store } from '../lib/store.js';
import { saveSetting } from './helper.js';

export function renderLibrarySection() {
  const container = el('div', { className: 'settings-section' });

  // Section Header
  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Library Folders'),
    el('p', {}, 'Manage folders containing your icon collections, configure scanning patterns, and run scans.')
  );
  container.appendChild(header);

  // 1. Add Folder Box
  const addFolderCard = el('div', { className: 'card form-group', style: { marginBottom: '24px' } });
  const folderInput = el('input', {
    type: 'text',
    className: 'input',
    placeholder: '/path/to/icon/folder',
    style: { flex: '1' }
  });
  const addError = el('div', { className: 'form-error' });

  const addBtn = el('button', {
    type: 'button',
    className: 'btn btn-primary',
    onClick: () => handleAddFolder()
  }, 'Add Folder');

  const addActions = [addBtn];

  // If native bridge exists, add Browse... button
  if (store.get('isNativeBridgeAvailable') || (window.external && typeof window.external.sendMessage === 'function')) {
    const browseBtn = el('button', {
      type: 'button',
      className: 'btn',
      onClick: async () => {
        if (window.external && typeof window.external.sendMessage === 'function') {
          try {
            const chosen = await new Promise((resolve) => {
              const handler = (e) => {
                window.removeEventListener('iconchest-folder-picked', handler);
                resolve(e.detail);
              };
              window.addEventListener('iconchest-folder-picked', handler);
              window.external.sendMessage(JSON.stringify({ type: 'pickFolder' }));
            });
            if (chosen) {
              folderInput.value = chosen;
              folderInput.dispatchEvent(new Event('input', { bubbles: true }));
              folderInput.focus();
            }
          } catch (err) {
            console.error('Folder picker error:', err);
          }
        }
      }
    }, 'Browse...');
    addActions.unshift(browseBtn);
  }

  const addRow = el('div', { style: { display: 'flex', gap: '8px', marginTop: '8px' } },
    folderInput,
    ...addActions
  );

  addFolderCard.appendChild(el('label', {}, 'Add Folder to Library'));
  addFolderCard.appendChild(addRow);
  addFolderCard.appendChild(addError);
  container.appendChild(addFolderCard);

  // 2. Folder List Header with Scan All button
  const listHeaderRow = el('div', {
    style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }
  },
    el('h3', { style: { fontSize: '1.1rem', fontWeight: '600' } }, 'Configured Folders'),
    el('button', {
      type: 'button',
      className: 'btn btn-primary',
      id: 'btn-scan-all',
      onClick: () => handleScanAll()
    }, 'Scan All Folders')
  );
  container.appendChild(listHeaderRow);

  // Folders container
  const foldersListContainer = el('div', { id: 'folders-list' });
  container.appendChild(foldersListContainer);

  // 3. Scanning Rules & Options
  const rulesHeader = el('div', { className: 'settings-header', style: { marginTop: '32px' } },
    el('h2', {}, 'Scanning Rules'),
    el('p', {}, 'Configure which files are indexed or excluded during scans.')
  );
  container.appendChild(rulesHeader);

  // Include patterns
  const includeInput = el('input', { type: 'text', className: 'input' });
  const includeError = el('div', { className: 'form-error' });
  const includeReset = el('a', {
    href: '#',
    style: { fontSize: '0.8rem', marginLeft: '8px' },
    onClick: (e) => {
      e.preventDefault();
      includeInput.value = '*.png;*.svg;*.jpg;*.jpeg;*.webp;*.ico;*.gif';
      includeInput.dispatchEvent(new Event('change'));
    }
  }, 'Reset to defaults');

  // Exclude names
  const excludeInput = el('input', { type: 'text', className: 'input' });
  const excludeError = el('div', { className: 'form-error' });
  const excludeReset = el('a', {
    href: '#',
    style: { fontSize: '0.8rem', marginLeft: '8px' },
    onClick: (e) => {
      e.preventDefault();
      excludeInput.value = '.git;node_modules;.DS_Store;Thumbs.db';
      excludeInput.dispatchEvent(new Event('change'));
    }
  }, 'Reset to defaults');

  // Scan on startup checkbox
  const startupCheckbox = el('input', { type: 'checkbox' });

  const rulesCard = el('div', { className: 'card' },
    el('div', { className: 'form-group' },
      el('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' } },
        el('label', {}, 'Include File Patterns (semicolon-separated)'),
        includeReset
      ),
      includeInput,
      includeError,
      el('div', { className: 'form-help' }, 'Wildcard patterns matching files to include in scans.')
    ),
    el('div', { className: 'form-group' },
      el('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' } },
        el('label', {}, 'Exclude Names (semicolon-separated)'),
        excludeReset
      ),
      excludeInput,
      excludeError,
      el('div', { className: 'form-help' }, 'Directory or file names ignored during scanning.')
    ),
    el('div', { className: 'form-group' },
      el('label', { className: 'checkbox-label' },
        startupCheckbox,
        el('span', {}, 'Scan all library folders automatically on startup')
      )
    )
  );
  container.appendChild(rulesCard);

  // --- Logic & Event Handlers ---
  let folders = [];
  const activeScans = new Map(); // folderId -> scanState

  async function loadFolders() {
    try {
      folders = await api.get('/api/folders');
      renderFoldersList();
      checkRunningScans();
    } catch (err) {
      toast.error('Failed to load library folders.');
    }
  }

  async function checkRunningScans() {
    try {
      const scans = await api.get('/api/scans');
      for (const s of scans) {
        if (s.state === 'running' || s.state === 'pending') {
          activeScans.set(s.folderId, {
            id: s.id,
            folderId: s.folderId,
            stage: s.stage || 'Scanning',
            filesFound: s.filesFound || 0,
            currentFile: s.currentFile || ''
          });
        }
      }
      renderFoldersList();
    } catch {}
  }

  function renderFoldersList() {
    clear(foldersListContainer);

    if (folders.length === 0) {
      foldersListContainer.appendChild(
        el('div', { className: 'card', style: { padding: '24px', textAlign: 'center', color: 'var(--text-muted)' } },
          'No folders in library yet. Add a folder above to start indexing your icons.'
        )
      );
      return;
    }

    for (const folder of folders) {
      const scanState = activeScans.get(folder.id);
      const isScanning = Boolean(scanState);

      const folderRow = el('div', { className: 'folder-item', id: `folder-row-${folder.id}` });

      // Enabled checkbox
      const enabledCheckbox = el('input', {
        type: 'checkbox',
        checked: folder.enabled,
        onChange: async () => {
          try {
            await api.patch(`/api/folders/${folder.id}`, { enabled: enabledCheckbox.checked });
            folder.enabled = enabledCheckbox.checked;
            toast.success(`Folder ${enabledCheckbox.checked ? 'enabled' : 'disabled'}.`);
          } catch {
            enabledCheckbox.checked = !enabledCheckbox.checked;
            toast.error('Failed to update folder state.');
          }
        }
      });

      const scanBtn = el('button', {
        type: 'button',
        className: 'btn btn-sm btn-primary',
        disabled: isScanning || !folder.enabled,
        onClick: () => handleScanFolder(folder.id)
      }, isScanning ? 'Scanning...' : 'Scan');

      const removeBtn = el('button', {
        type: 'button',
        className: 'btn btn-sm btn-danger',
        disabled: isScanning,
        onClick: () => handleRemoveFolder(folder)
      }, 'Remove');

      const actions = [scanBtn, removeBtn];

      if (isScanning && scanState?.id) {
        const cancelBtn = el('button', {
          type: 'button',
          className: 'btn btn-sm',
          onClick: async () => {
            try {
              await api.post(`/api/scans/${scanState.id}/cancel`);
              toast.info('Scan cancellation requested.');
            } catch (err) {
              toast.error('Failed to cancel scan.');
            }
          }
        }, 'Cancel');
        actions.unshift(cancelBtn);
      }

      const itemHeader = el('div', { className: 'folder-item-header' },
        el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
          enabledCheckbox,
          el('span', { className: 'folder-path' }, folder.path)
        ),
        el('div', { className: 'folder-actions' }, ...actions)
      );

      const lastScanText = folder.lastScanUtc
        ? new Date(folder.lastScanUtc).toLocaleString()
        : 'Never scanned';

      const metaRow = el('div', { className: 'folder-meta' },
        el('span', {}, `Images: ${folder.imageCount || 0}`),
        el('span', {}, `Last scan: ${lastScanText}`)
      );

      folderRow.appendChild(itemHeader);
      folderRow.appendChild(metaRow);

      // Active Scan Progress UI
      if (isScanning) {
        const progressContainer = el('div', { style: { marginTop: '12px' } });
        const stageLabel = el('div', { style: { fontSize: '0.8rem', color: 'var(--text-secondary)' } },
          `${scanState.stage || 'Scanning'}... (${scanState.filesFound || 0} files found)`
        );

        const currentFile = scanState.currentFile ? middleTruncate(scanState.currentFile, 50) : '';
        const currentFileLabel = el('div', {
          style: { fontSize: '0.75rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', marginTop: '2px' }
        }, currentFile);

        const progressBar = el('div', { className: 'progress-bar-container' },
          el('div', {
            className: 'progress-bar-fill',
            style: { width: scanState.percent != null ? `${scanState.percent}%` : '60%' }
          })
        );

        progressContainer.appendChild(stageLabel);
        if (currentFile) progressContainer.appendChild(currentFileLabel);
        progressContainer.appendChild(progressBar);
        folderRow.appendChild(progressContainer);
      }

      // Scan Summary UI if completed
      if (folder._scanSummary) {
        const s = folder._scanSummary;
        const summaryRow = el('div', {
          style: {
            marginTop: '10px',
            padding: '8px 12px',
            backgroundColor: 'var(--bg-surface-hover)',
            borderRadius: '4px',
            fontSize: '0.8rem',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center'
          }
        },
          el('span', {}, `Scan finished: +${s.filesNew || 0} new, ~${s.filesChanged || 0} changed, ${s.filesMoved || 0} moved, !${s.filesMissing || 0} missing, ${s.duplicatesFound || 0} duplicates.`),
          el('button', {
            type: 'button',
            className: 'btn btn-sm',
            onClick: () => {
              delete folder._scanSummary;
              renderFoldersList();
            }
          }, 'Dismiss')
        );
        folderRow.appendChild(summaryRow);
      }

      foldersListContainer.appendChild(folderRow);
    }
  }

  function middleTruncate(str, maxLength) {
    if (!str || str.length <= maxLength) return str;
    const half = Math.floor((maxLength - 3) / 2);
    return str.slice(0, half) + '...' + str.slice(-half);
  }

  async function handleAddFolder() {
    addError.textContent = '';
    const path = folderInput.value.trim();
    if (!path) {
      addError.textContent = 'Please enter a folder path.';
      return;
    }

    try {
      const created = await api.post('/api/folders', { path });
      folderInput.value = '';
      toast.success('Folder added to library.');
      await loadFolders();
    } catch (err) {
      if (err instanceof ApiError) {
        addError.textContent = err.errors?.path || err.detail || 'Could not add folder.';
      } else {
        addError.textContent = 'Network error adding folder.';
      }
    }
  }

  async function handleRemoveFolder(folder) {
    const confirmed = await dialog.confirm({
      title: 'Remove Folder from Library?',
      message: `Are you sure you want to remove "${folder.path}" from the library?\n\nThis will delete all image records, tags, and thumbnails associated with this folder from IconChest.\n\nNO FILES ON DISK WILL BE TOUCHED OR DELETED.`,
      confirmText: 'Remove Folder',
      isDanger: true
    });

    if (!confirmed) return;

    try {
      await api.delete(`/api/folders/${folder.id}`);
      toast.success('Folder removed from library.');
      await loadFolders();
    } catch (err) {
      toast.error('Failed to remove folder.');
    }
  }

  async function handleScanFolder(folderId) {
    try {
      const scanJob = await api.post('/api/scans', { folderId });
      activeScans.set(folderId, {
        id: scanJob.id,
        folderId: folderId,
        stage: 'Starting',
        filesFound: 0
      });
      renderFoldersList();
    } catch (err) {
      if (err.status === 409) {
        toast.info('A scan is already in progress for this folder.');
      } else {
        toast.error('Failed to start scan.');
      }
    }
  }

  async function handleScanAll() {
    try {
      await api.post('/api/scans', {});
      toast.info('Scan started for all enabled folders.');
      await checkRunningScans();
    } catch (err) {
      if (err.status === 409) {
        toast.info('A scan is already in progress.');
      } else {
        toast.error('Failed to start scan all.');
      }
    }
  }

  // SSE Subscriptions for Live Scan Progress
  eventStream.on('scan.progress', (data) => {
    if (!data?.folderId) return;
    activeScans.set(data.folderId, {
      id: data.scanId || activeScans.get(data.folderId)?.id,
      folderId: data.folderId,
      stage: data.stage || 'Scanning',
      filesFound: data.filesFound || 0,
      currentFile: data.currentFile || '',
      percent: data.percent
    });
    renderFoldersList();
  });

  eventStream.on('scan.completed', (data) => {
    if (!data?.folderId) return;
    activeScans.delete(data.folderId);
    const f = folders.find(x => x.id === data.folderId);
    if (f) {
      f._scanSummary = data;
    }
    loadFolders();
  });

  eventStream.on('scan.failed', (data) => {
    if (!data?.folderId) return;
    activeScans.delete(data.folderId);
    toast.error(`Scan failed: ${data.error || 'Unknown error'}`);
    loadFolders();
  });

  // Load Initial Settings for Scanning Rules
  async function loadSettings() {
    try {
      const s = await api.get('/api/settings');
      includeInput.value = (s.scanning?.includePatterns || []).join(';');
      excludeInput.value = (s.scanning?.excludeNames || []).join(';');
      startupCheckbox.checked = Boolean(s.scanning?.scanOnStartup);
    } catch {}
  }

  includeInput.addEventListener('change', () => {
    const list = includeInput.value.split(';').map(x => x.trim()).filter(Boolean);
    saveSetting({ scanning: { includePatterns: list } }, includeError);
  });

  excludeInput.addEventListener('change', () => {
    const list = excludeInput.value.split(';').map(x => x.trim()).filter(Boolean);
    saveSetting({ scanning: { excludeNames: list } }, excludeError);
  });

  startupCheckbox.addEventListener('change', () => {
    saveSetting({ scanning: { scanOnStartup: startupCheckbox.checked } });
  });

  loadFolders();
  loadSettings();

  return container;
}

