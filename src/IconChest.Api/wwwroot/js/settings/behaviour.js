import { el } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { saveSetting } from './helper.js';

export function renderBehaviourSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Behaviour'),
    el('p', {}, 'Configure application startup actions, window state persistence, and confirmation prompts.')
  );
  container.appendChild(header);

  // 1. Default view on startup
  const viewSelect = el('select', { className: 'select', id: 'setting-default-view' },
    el('option', { value: 'folders' }, 'Folders'),
    el('option', { value: 'years' }, 'Years (Timeline)'),
    el('option', { value: 'tags' }, 'Tags'),
    el('option', { value: 'favourites' }, 'Favourites'),
    el('option', { value: 'duplicates' }, 'Duplicates'),
    el('option', { value: 'missing' }, 'Missing Files'),
    el('option', { value: 'help' }, 'Help'),
    el('option', { value: 'settings' }, 'Settings')
  );

  viewSelect.addEventListener('change', () => {
    saveSetting({ behaviour: { defaultView: viewSelect.value } });
  });

  // 2. Remember window state
  const rememberWindowCheckbox = el('input', { type: 'checkbox', id: 'setting-remember-window' });
  rememberWindowCheckbox.addEventListener('change', () => {
    saveSetting({ behaviour: { rememberWindowState: rememberWindowCheckbox.checked } });
  });

  // 3. Confirm before delete
  const confirmDeleteCheckbox = el('input', { type: 'checkbox', id: 'setting-confirm-delete' });
  confirmDeleteCheckbox.addEventListener('change', () => {
    saveSetting({ behaviour: { confirmBeforeDelete: confirmDeleteCheckbox.checked } });
  });

  const card = el('div', { className: 'card' },
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-default-view' }, 'Default View on Startup'),
      viewSelect,
      el('div', { className: 'form-help' }, 'The screen opened automatically when IconChest launches.')
    ),
    el('div', { className: 'form-group' },
      el('label', { className: 'checkbox-label' },
        rememberWindowCheckbox,
        el('span', {}, 'Remember desktop window size, position, and maximized state')
      )
    ),
    el('div', { className: 'form-group' },
      el('label', { className: 'checkbox-label' },
        confirmDeleteCheckbox,
        el('span', {}, 'Show confirmation dialog before removing folders or deleting items')
      )
    )
  );

  container.appendChild(card);

  // Load existing settings
  api.get('/api/settings').then(s => {
    if (s.behaviour) {
      if (s.behaviour.defaultView) {
        viewSelect.value = s.behaviour.defaultView.toLowerCase();
      }
      rememberWindowCheckbox.checked = s.behaviour.rememberWindowState !== false;
      confirmDeleteCheckbox.checked = s.behaviour.confirmBeforeDelete !== false;
    }
  }).catch(() => {});

  return container;
}

