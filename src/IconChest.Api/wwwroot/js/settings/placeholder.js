import { el } from '../lib/dom.js';

export function createSettingsPlaceholder(title, description) {
  const container = el('div', { className: 'settings-section' },
    el('div', { className: 'settings-header' },
      el('h2', {}, title),
      el('p', {}, description)
    ),
    el('div', { className: 'card', style: { padding: '32px', textAlign: 'center' } },
      el('h3', { style: { fontSize: '1.2rem', marginBottom: '8px', color: 'var(--accent-primary)' } }, 'Coming in Phase 10'),
      el('p', { style: { color: 'var(--text-secondary)' } }, `The ${title} management controls will be introduced alongside system backups and database relocation in Phase 10.`)
    )
  );
  return container;
}

