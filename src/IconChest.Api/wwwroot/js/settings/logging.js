import { el } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { saveSetting } from './helper.js';

export function renderLoggingSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Diagnostics & Logging'),
    el('p', {}, 'Configure application logging levels, locate diagnosis logs, and review retention.')
  );
  container.appendChild(header);

  // 1. Log Level Dropdown
  const levelSelect = el('select', { className: 'select', id: 'setting-log-level' },
    el('option', { value: 'Off' }, 'Off (Disabled)'),
    el('option', { value: 'Fatal' }, 'Fatal (Critical errors only)'),
    el('option', { value: 'Error' }, 'Error'),
    el('option', { value: 'Warn' }, 'Warn (Warnings and errors)'),
    el('option', { value: 'Information' }, 'Information (Recommended)'),
    el('option', { value: 'Debug' }, 'Debug (Detailed troubleshooting)'),
    el('option', { value: 'Trace' }, 'Trace (All events)')
  );

  levelSelect.addEventListener('change', () => {
    saveSetting({
      logging: { level: levelSelect.value }
    }).catch(() => {});
  });

  // 2. Log Directory Path
  const logPathDisplay = el('input', {
    type: 'text',
    className: 'input',
    readOnly: true,
    style: { fontFamily: 'var(--font-mono)' },
    value: 'Loading...'
  });

  const btnCopyPath = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        await navigator.clipboard.writeText(logPathDisplay.value);
        toast.show('Log path copied to clipboard', 'success');
      } catch {
        toast.show('Failed to copy to clipboard', 'error');
      }
    }
  }, 'Copy Path');

  const btnOpenFolder = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      try {
        await api.post('/api/native/open-folder', { target: 'logs' });
      } catch (err) {
        toast.show(err.message || 'Failed to open log folder', 'error');
      }
    }
  }, 'Open Log Folder');

  const card = el('div', { className: 'card' },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Logging Configuration'),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-log-level' }, 'Minimum Log Level'),
      levelSelect,
      el('div', { className: 'form-help' }, 'Controls verbosity of event messages written to disk.')
    ),
    el('div', { className: 'form-group' },
      el('label', {}, 'Log Files Location'),
      el('div', { style: { display: 'flex', gap: '8px' } },
        logPathDisplay,
        btnCopyPath,
        btnOpenFolder
      ),
      el('div', { className: 'form-help' }, 'Log files are rotated daily and automatically purged after 14 days.')
    )
  );

  container.appendChild(card);

  // Load existing settings and system info
  api.get('/api/settings').then(s => {
    const lvl = s.logging?.level || 'Information';
    levelSelect.value = lvl;
  });

  api.get('/api/system/info').then(info => {
    logPathDisplay.value = info.logFolder || info.logsFolder || info.paths?.logs || 'Not available';
  }).catch(() => {
    logPathDisplay.value = 'Unknown';
  });

  return container;
}
