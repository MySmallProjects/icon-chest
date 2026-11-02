import { el } from '../lib/dom.js';

export function createPlaceholderView(title, phaseText, description) {
  const container = el('div', { className: 'view-container' },
    el('div', { className: 'placeholder-view' },
      el('h2', {}, title),
      el('p', { style: { color: 'var(--accent-primary)', fontWeight: '500', marginBottom: '8px' } }, phaseText),
      el('p', {}, description)
    )
  );
  return {
    element: container,
    renderPanelA: () => el('div', { className: 'panel-a-content' },
      el('p', { style: { color: 'var(--text-muted)', fontSize: '0.8rem' } }, `${title} tree will appear here.`)
    )
  };
}

