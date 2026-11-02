import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { dialog } from '../lib/dialog.js';
import { toast } from '../lib/toast.js';

export function renderAppInfoSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Application Information'),
    el('p', {}, 'System diagnostics, environment specifications, local storage paths, and open-source licenses.')
  );
  container.appendChild(header);

  const infoTableBody = el('tbody');
  const infoTable = el('table', { className: 'info-table' }, infoTableBody);

  const card = el('div', { className: 'card' }, infoTable);
  container.appendChild(card);

  // License button
  const licenseBtn = el('button', {
    type: 'button',
    className: 'btn btn-primary',
    id: 'btn-licenses',
    style: { marginTop: '16px' },
    onClick: async () => {
      try {
        const text = await api.get('/api/system/licenses');
        dialog.alert({
          title: 'Third-Party Software Notices',
          message: text,
          isMono: true
        });
      } catch (err) {
        toast.error('Failed to load third-party license notices.');
      }
    }
  }, 'Third-Party Licences');

  container.appendChild(licenseBtn);

  function createCopyButton(textToCopy) {
    const btn = el('button', {
      type: 'button',
      className: 'copy-btn',
      title: 'Copy to clipboard',
      onClick: async () => {
        try {
          if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(textToCopy);
          } else {
            const ta = document.createElement('textarea');
            ta.value = textToCopy;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
          }
          btn.textContent = 'Copied!';
          setTimeout(() => { btn.textContent = 'Copy'; }, 1500);
        } catch {
          toast.error('Could not copy to clipboard.');
        }
      }
    }, 'Copy');
    return btn;
  }

  function addRow(label, value, isPath = false) {
    const tdContent = [String(value ?? 'Unknown')];
    if (isPath && value) {
      tdContent.push(createCopyButton(value));
    }

    const tr = el('tr', {},
      el('th', {}, label),
      el('td', {}, ...tdContent)
    );
    infoTableBody.appendChild(tr);
  }

  // Load from /api/system/info
  api.get('/api/system/info').then(info => {
    clear(infoTableBody);
    addRow('App Version', info.version);
    addRow('Build Date UTC', info.buildDateUtc);
    addRow('Platform', info.platform);
    addRow('API Port', info.apiPort);
    addRow('Executable Path', info.executablePath, true);
    addRow('Database Path', info.dbPath, true);
    addRow('Schema Version', info.schemaVersion);
    addRow('Log Folder', info.logFolder, true);
    addRow('Current Log File', info.logFilePath, true);
    addRow('Hash Algorithm', info.hashAlgorithm);
  }).catch(() => {
    clear(infoTableBody);
    addRow('Status', 'Failed to retrieve system diagnostics from API.');
  });

  return container;
}

