import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { dialog } from '../lib/dialog.js';
import { bridge } from '../lib/bridge.js';
import { exportTags, promptImportTags } from '../lib/tag-io.js';

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  if (!bytes || isNaN(bytes)) return '-';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function formatDate(isoString) {
  if (!isoString) return '-';
  try {
    const d = new Date(isoString);
    return d.toLocaleString();
  } catch {
    return isoString;
  }
}

export function renderDataSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Data & Library Maintenance'),
    el('p', {}, 'Inspect database storage, create online backups, restore previous points, and manage tag taxonomy.')
  );
  container.appendChild(header);

  // 1. Database Info & Move Location
  const dbPathDisplay = el('input', {
    type: 'text',
    className: 'input',
    readOnly: true,
    style: { fontFamily: 'var(--font-mono)' },
    value: 'Loading...'
  });

  const dbStatsEl = el('div', {
    style: {
      marginTop: '12px',
      padding: '12px 16px',
      background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
      borderRadius: 'var(--radius-md, 6px)',
      fontSize: '0.9rem',
      display: 'grid',
      gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
      gap: '12px'
    }
  });

  const btnMoveDb = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: () => promptMoveDatabase()
  }, 'Change Location...');

  const dbCard = el('div', { className: 'card' },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Active Database'),
    el('div', { className: 'form-group' },
      el('label', {}, 'Database File Path'),
      el('div', { style: { display: 'flex', gap: '8px' } },
        dbPathDisplay,
        btnMoveDb
      ),
      el('div', { className: 'form-help' }, 'SQLite database containing all catalog metadata, index records, and tags.')
    ),
    dbStatsEl
  );

  // 2. Backup Controls
  const backupFolderDisplay = el('input', {
    type: 'text',
    className: 'input',
    readOnly: true,
    style: { fontFamily: 'var(--font-mono)' },
    value: 'Loading...'
  });

  const btnOpenBackupFolder = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        await api.post('/api/native/open-folder', { target: 'backups' });
      } catch (err) {
        toast.show(err.message || 'Failed to open backup folder', 'error');
      }
    }
  }, 'Open Backup Folder');

  const btnBackupNow = el('button', {
    type: 'button',
    className: 'btn btn-primary',
    onClick: async () => {
      try {
        toast.show('Creating consistent database backup...', 'info');
        const res = await api.post('/api/data/backup', {});
        const fileName = res.fileName || (res.path ? res.path.split(/[/\\]/).pop() : 'backup');
        toast.show(`Backup created: ${fileName} (${formatBytes(res.sizeBytes)})`, 'success');
        refreshDataInfo();
      } catch (err) {
        toast.show(err.message || 'Backup failed', 'error');
      }
    }
  }, 'Backup Now');

  const btnBackupTo = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      if (!bridge.isAvailable()) {
        toast.show('Custom backup paths are only available in desktop application mode.', 'info');
        return;
      }

      const defaultName = `iconchest-backup-${new Date().toISOString().replace(/[:.]/g, '-')}.db`;
      let chosenPath = await bridge.pickSaveFile('Select Backup File Location', defaultName);
      if (!chosenPath) return;

      const baseName = chosenPath.split(/[/\\]/).pop();
      if (!baseName.includes('.')) {
        chosenPath += '.db';
      }

      try {
        toast.show('Creating database backup...', 'info');
        const res = await api.post('/api/data/backup', { path: chosenPath });
        toast.show(`Backup saved to ${chosenPath} (${formatBytes(res.sizeBytes)})`, 'success');
        refreshDataInfo();
      } catch (err) {
        toast.show(err.message || 'Backup failed', 'error');
      }
    }
  }, 'Backup To...');

  const btnRestoreFromFile = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      if (!bridge.isAvailable()) {
        toast.show('Direct file restoration is supported in desktop mode. Use listed backups or upload for web.', 'info');
        return;
      }
      const pickedPath = await bridge.pickFile('Select IconChest Database Backup (.db)');
      if (pickedPath) {
        startRestoreWizard({ path: pickedPath });
      }
    }
  }, 'Restore From File...');

  const backupCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Backups'),
    el('div', { className: 'form-group' },
      el('label', {}, 'Default Backups Directory'),
      el('div', { style: { display: 'flex', gap: '8px' } },
        backupFolderDisplay,
        btnOpenBackupFolder
      )
    ),
    el('div', { style: { display: 'flex', gap: '8px', marginTop: '12px', flexWrap: 'wrap' } },
      btnBackupNow,
      btnBackupTo,
      btnRestoreFromFile
    )
  );

  // 3. Backups List
  const backupsListTable = el('div', { style: { marginTop: '16px' } });

  const backupsListCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '8px', fontSize: '1rem', fontWeight: '600' } }, 'Available Backups'),
    el('p', { style: { color: 'var(--text-secondary)', fontSize: '0.85rem', marginBottom: '12px' } },
      'Consistent snapshots saved in the default backup folder. Restoring automatically protects the current state in a pre-restore backup.'
    ),
    backupsListTable
  );

  // 4. Tag Export & Import
  const btnExportTags = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: () => exportTags()
  }, 'Export Tags (JSON)');

  const btnImportTags = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: () => promptImportTags()
  }, 'Import Tags (JSON)...');

  const tagsCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '8px', fontSize: '1rem', fontWeight: '600' } }, 'Tag Taxonomy Transfer'),
    el('p', { style: { color: 'var(--text-secondary)', fontSize: '0.85rem', marginBottom: '12px' } },
      'Export custom tags and color palettes to JSON or import from other machines without modifying image associations.'
    ),
    el('div', { style: { display: 'flex', gap: '8px' } },
      btnExportTags,
      btnImportTags
    )
  );

  container.appendChild(dbCard);
  container.appendChild(backupCard);
  container.appendChild(backupsListCard);
  container.appendChild(tagsCard);

  async function promptMoveDatabase() {
    let targetFolder = null;
    if (bridge.isAvailable()) {
      targetFolder = await bridge.pickFolder('Select Target Folder for Database');
    } else {
      targetFolder = prompt('Enter destination directory path:');
    }

    if (!targetFolder) return;

    const deleteOld = await dialog.confirm({
      title: 'Move Database',
      message: `Move database to "${targetFolder}"?\n\nWould you like to delete the old database file upon successful verification? (If cancelled, the old file is retained with an .old timestamp).`,
      confirmText: 'Move & Delete Old',
      cancelText: 'Move & Keep Old Backup'
    });

    try {
      toast.show('Moving and verifying database...', 'info');
      const res = await api.post('/api/data/move', {
        folder: targetFolder,
        deleteOld: deleteOld === true
      });
      toast.show(`Database moved successfully to ${res.newPath}`, 'success');
      refreshDataInfo();
    } catch (err) {
      toast.show(err.message || 'Failed to move database', 'error');
    }
  }

  // Restore Wizard
  async function startRestoreWizard(selector) {
    const dialogElem = el('dialog', { className: 'app-dialog' });
    document.body.appendChild(dialogElem);

    clear(dialogElem);
    const titleEl = el('div', { className: 'dialog-header' }, 'Database Restore Verification');
    const bodyEl = el('div', { className: 'dialog-body' }, 'Inspecting backup integrity...');
    const footerEl = el('div', { className: 'dialog-footer' });

    const cancelBtn = el('button', {
      type: 'button',
      className: 'btn',
      onClick: () => {
        dialogElem.close();
        document.body.removeChild(dialogElem);
      }
    }, 'Cancel');

    footerEl.appendChild(cancelBtn);
    dialogElem.appendChild(titleEl);
    dialogElem.appendChild(bodyEl);
    dialogElem.appendChild(footerEl);
    dialogElem.showModal();

    try {
      const inspect = await api.post('/api/data/restore/inspect', selector);

      clear(bodyEl);

      const warningItems = [];
      if (inspect.libraryFolders) {
        for (const f of inspect.libraryFolders) {
          const exists = f.existsOnDisk !== undefined ? f.existsOnDisk : (f.exists !== undefined ? f.exists : true);
          if (!exists) {
            warningItems.push(`Library folder not found on this machine: ${f.path}`);
          }
        }
      }

      const warningsEl = warningItems.length > 0
        ? el('div', { className: 'banner banner-warning', style: { marginBottom: '12px' } },
            el('strong', {}, 'Attention: '),
            warningItems.join('; ')
          )
        : el('div', { style: { color: 'var(--color-success, #10b981)', marginBottom: '12px', fontWeight: '500' } },
            '✓ Backup passed integrity check. All table structures and schema valid.'
          );

      const detailsList = el('div', {
        style: {
          display: 'grid',
          gridTemplateColumns: 'auto 1fr',
          gap: '6px 12px',
          fontSize: '0.9rem',
          background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
          padding: '12px',
          borderRadius: 'var(--radius-md, 6px)'
        }
      },
        el('div', { style: { fontWeight: '600' } }, 'Integrity Check:'), el('div', {}, inspect.integrityCheck || 'ok'),
        el('div', { style: { fontWeight: '600' } }, 'Schema Version:'), el('div', {}, String(inspect.schemaVersion)),
        el('div', { style: { fontWeight: '600' } }, 'Images:'), el('div', {}, (inspect.imageCount || 0).toLocaleString()),
        el('div', { style: { fontWeight: '600' } }, 'Folders:'), el('div', {}, (inspect.folderCount || 0).toLocaleString()),
        el('div', { style: { fontWeight: '600' } }, 'Tags:'), el('div', {}, (inspect.tagCount || 0).toLocaleString()),
        el('div', { style: { fontWeight: '600' } }, 'Backup Date:'), el('div', {}, formatDate(inspect.fileDateUtc || inspect.createdUtc))
      );

      const noticeEl = el('p', { style: { marginTop: '12px', fontSize: '0.85rem', color: 'var(--text-muted)' } },
        'Restoring will create a .pre-restore safety copy of your current database. In-flight operations will pause while the database is swapped.'
      );

      bodyEl.appendChild(warningsEl);
      bodyEl.appendChild(detailsList);
      bodyEl.appendChild(noticeEl);

      const confirmBtn = el('button', {
        type: 'button',
        className: 'btn btn-danger',
        onClick: async () => {
          confirmBtn.disabled = true;
          cancelBtn.disabled = true;
          bodyEl.textContent = 'Performing live database swap and migration...';

          try {
            const summary = await api.post('/api/data/restore', { ...selector, confirm: true });
            dialogElem.close();
            document.body.removeChild(dialogElem);

            await dialog.alert({
              title: 'Restore Completed',
              message: `Library successfully restored from ${summary.backupFile || 'backup'}.\n` +
                       `Images: ${summary.imageCount || 0}, Folders: ${summary.folderCount || 0}, Tags: ${summary.tagCount || 0}.\n` +
                       `Safety copy saved as: ${summary.preRestoreCopy || 'pre-restore'}`
            });

            // Trigger full reload
            refreshDataInfo();
            window.dispatchEvent(new CustomEvent('iconchest-data-reloaded'));
          } catch (restoreErr) {
            clear(bodyEl);
            bodyEl.appendChild(el('div', { className: 'banner banner-error' }, restoreErr.message || 'Restore failed and was rolled back.'));
            cancelBtn.disabled = false;
          }
        }
      }, 'Confirm Restore');

      footerEl.appendChild(confirmBtn);
    } catch (err) {
      clear(bodyEl);
      bodyEl.appendChild(el('div', { className: 'banner banner-error' }, err.message || 'Failed to inspect backup file.'));
    }
  }

  // Backup Info Dialog
  async function showBackupInfo(selector, backupName) {
    const dialogElem = el('dialog', { className: 'app-dialog' });
    document.body.appendChild(dialogElem);

    clear(dialogElem);
    const titleEl = el('div', { className: 'dialog-header' }, `Backup Details: ${backupName}`);
    const bodyEl = el('div', { className: 'dialog-body' }, 'Inspecting backup file details...');
    const footerEl = el('div', { className: 'dialog-footer' });

    const closeBtn = el('button', {
      type: 'button',
      className: 'btn',
      onClick: () => {
        dialogElem.close();
        document.body.removeChild(dialogElem);
      }
    }, 'Close');

    footerEl.appendChild(closeBtn);
    dialogElem.appendChild(titleEl);
    dialogElem.appendChild(bodyEl);
    dialogElem.appendChild(footerEl);
    dialogElem.showModal();

    try {
      const inspect = await api.post('/api/data/restore/inspect', selector);

      clear(bodyEl);

      const warningItems = [];
      const folderListItems = [];
      if (inspect.libraryFolders && inspect.libraryFolders.length > 0) {
        for (const f of inspect.libraryFolders) {
          const exists = f.existsOnDisk !== undefined ? f.existsOnDisk : (f.exists !== undefined ? f.exists : true);
          if (!exists) {
            warningItems.push(`Library folder not found on this machine: ${f.path}`);
          }
          folderListItems.push(
            el('div', { style: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '4px 0', borderBottom: '1px dashed var(--border-subtle)' } },
              el('span', { className: 'truncate', style: { fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }, title: f.path }, f.path),
              el('span', {
                style: {
                  fontSize: '0.75rem',
                  padding: '2px 6px',
                  borderRadius: '4px',
                  fontWeight: '600',
                  backgroundColor: exists ? 'rgba(16, 185, 129, 0.15)' : 'rgba(239, 68, 68, 0.15)',
                  color: exists ? 'var(--color-success, #10b981)' : 'var(--color-danger, #ef4444)'
                }
              }, exists ? 'Found' : 'Missing')
            )
          );
        }
      }

      const warningsEl = warningItems.length > 0
        ? el('div', { className: 'banner banner-warning', style: { marginBottom: '12px' } },
            el('strong', {}, 'Attention: '),
            warningItems.join('; ')
          )
        : el('div', { style: { color: 'var(--color-success, #10b981)', marginBottom: '12px', fontWeight: '500' } },
            '✓ Backup passed integrity check. All table structures and schema valid.'
          );

      const detailsList = el('div', {
        style: {
          display: 'grid',
          gridTemplateColumns: 'auto 1fr',
          gap: '6px 12px',
          fontSize: '0.9rem',
          background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
          padding: '12px',
          borderRadius: 'var(--radius-md, 6px)',
          marginBottom: '12px'
        }
      },
        el('div', { style: { fontWeight: '600' } }, 'Integrity Check:'), el('div', {}, inspect.integrityCheck || 'ok'),
        el('div', { style: { fontWeight: '600' } }, 'Schema Version:'), el('div', {}, String(inspect.schemaVersion)),
        el('div', { style: { fontWeight: '600' } }, 'Backup File Size:'), el('div', {}, formatBytes(inspect.fileSizeBytes || 0)),
        el('div', { style: { fontWeight: '600' } }, 'Images:'), el('div', {}, (inspect.imageCount || 0).toLocaleString()),
        el('div', { style: { fontWeight: '600' } }, 'Folders:'), el('div', {}, (inspect.folderCount || 0).toLocaleString()),
        el('div', { style: { fontWeight: '600' } }, 'Tags:'), el('div', {}, (inspect.tagCount || 0).toLocaleString()),
        el('div', { style: { fontWeight: '600' } }, 'Backup Date:'), el('div', {}, formatDate(inspect.fileDateUtc || inspect.createdUtc))
      );

      const foldersSection = el('div', { style: { marginTop: '8px' } },
        el('div', { style: { fontWeight: '600', marginBottom: '6px', fontSize: '0.9rem' } }, 'Library Folders:'),
        folderListItems.length > 0
          ? el('div', { style: { maxHeight: '120px', overflowY: 'auto' } }, folderListItems)
          : el('div', { style: { color: 'var(--text-muted)', fontSize: '0.85rem' } }, 'No library folders recorded in backup.')
      );

      bodyEl.appendChild(warningsEl);
      bodyEl.appendChild(detailsList);
      bodyEl.appendChild(foldersSection);

      const restoreBtn = el('button', {
        type: 'button',
        className: 'btn btn-secondary',
        style: { marginRight: '8px' },
        onClick: () => {
          dialogElem.close();
          document.body.removeChild(dialogElem);
          startRestoreWizard(selector);
        }
      }, 'Restore this Backup...');

      footerEl.insertBefore(restoreBtn, closeBtn);
    } catch (err) {
      clear(bodyEl);
      bodyEl.appendChild(el('div', { className: 'banner banner-error' }, `Failed to inspect backup: ${err.message}`));
    }
  }

  async function refreshDataInfo() {
    try {
      const info = await api.get('/api/data/info');
      dbPathDisplay.value = info.dbPath || 'Unknown';
      backupFolderDisplay.value = info.backupFolder || 'Unknown';

      clear(dbStatsEl);
      dbStatsEl.appendChild(el('div', {},
        el('div', { style: { color: 'var(--text-muted)', fontSize: '0.8rem' } }, 'Database Size'),
        el('div', { style: { fontWeight: '600', fontSize: '1.1rem' } }, formatBytes(info.dbSizeBytes ?? info.sizeBytes ?? 0))
      ));
      dbStatsEl.appendChild(el('div', {},
        el('div', { style: { color: 'var(--text-muted)', fontSize: '0.8rem' } }, 'WAL Journal Size'),
        el('div', { style: { fontWeight: '600', fontSize: '1.1rem' } }, formatBytes(info.walSizeBytes || 0))
      ));
      dbStatsEl.appendChild(el('div', {},
        el('div', { style: { color: 'var(--text-muted)', fontSize: '0.8rem' } }, 'Catalog Records'),
        el('div', { style: { fontWeight: '600', fontSize: '1.1rem' } }, `${(info.imageCount || 0).toLocaleString()} images`)
      ));
      dbStatsEl.appendChild(el('div', {},
        el('div', { style: { color: 'var(--text-muted)', fontSize: '0.8rem' } }, 'Schema Version'),
        el('div', { style: { fontWeight: '600', fontSize: '1.1rem' } }, `v${info.schemaVersion || 1}`)
      ));

      // Render Backups List
      clear(backupsListTable);
      const backups = info.backups || [];
      if (backups.length === 0) {
        backupsListTable.appendChild(el('div', { style: { color: 'var(--text-muted)', padding: '8px 0' } }, 'No backups found in the default backup directory.'));
      } else {
        const table = el('table', { className: 'table', style: { width: '100%', textAlign: 'left', borderCollapse: 'collapse' } },
          el('thead', {},
            el('tr', { style: { borderBottom: '1px solid var(--border-subtle)' } },
              el('th', { style: { padding: '8px' } }, 'Backup File'),
              el('th', { style: { padding: '8px' } }, 'Size'),
              el('th', { style: { padding: '8px' } }, 'Created'),
              el('th', { style: { padding: '8px', textAlign: 'right' } }, 'Actions')
            )
          )
        );

        const tbody = el('tbody', {});
        for (const b of backups) {
          const row = el('tr', { style: { borderBottom: '1px solid var(--border-subtle)' } },
            el('td', { style: { padding: '8px', fontFamily: 'var(--font-mono)', fontSize: '0.85rem' } }, b.name),
            el('td', { style: { padding: '8px' } }, formatBytes(b.sizeBytes || 0)),
            el('td', { style: { padding: '8px' } }, formatDate(b.createdUtc)),
            el('td', { style: { padding: '8px', textAlign: 'right' } },
              el('button', {
                type: 'button',
                className: 'btn btn-sm btn-secondary',
                style: { marginRight: '6px' },
                onClick: () => showBackupInfo({ name: b.name }, b.name)
              }, 'Info'),
              el('button', {
                type: 'button',
                className: 'btn btn-sm btn-secondary',
                style: { marginRight: '6px' },
                onClick: () => startRestoreWizard({ name: b.name })
              }, 'Restore'),
              el('a', {
                href: `/api/data/backups/${encodeURIComponent(b.name)}/download`,
                download: b.name,
                className: 'btn btn-sm btn-secondary'
              }, 'Download')
            )
          );
          tbody.appendChild(row);
        }
        table.appendChild(tbody);
        backupsListTable.appendChild(table);
      }
    } catch (err) {
      console.error('Failed to query data info:', err);
    }
  }

  refreshDataInfo();

  return container;
}
