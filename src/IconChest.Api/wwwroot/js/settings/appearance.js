import { el } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { store } from '../lib/store.js';
import { saveSetting } from './helper.js';

export function renderAppearanceSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'Appearance'),
    el('p', {}, 'Customize the visual theme, layout style, and image preview preferences.')
  );
  container.appendChild(header);

  // 1. Theme Selection
  const currentTheme = store.get('theme') || 'dark';
  const themeSelect = el('select', { className: 'select', id: 'setting-theme' },
    el('option', { value: 'dark', selected: currentTheme === 'dark' }, 'Dark Theme'),
    el('option', { value: 'light', selected: currentTheme === 'light' }, 'Light Theme')
  );

  themeSelect.addEventListener('change', () => {
    const val = themeSelect.value;
    document.documentElement.setAttribute('data-theme', val);
    document.documentElement.style.colorScheme = val;
    store.set('theme', val);
    try { localStorage.setItem('iconchest_theme', val); } catch {}
    saveSetting({ theme: val, appearance: { theme: val } });
  });

  // 2. Grid Caption Selection
  const captionSelect = el('select', { className: 'select', id: 'setting-grid-caption' },
    el('option', { value: 'none' }, 'None'),
    el('option', { value: 'filename' }, 'Filename'),
    el('option', { value: 'title' }, 'Title')
  );

  captionSelect.addEventListener('change', () => {
    saveSetting({ gridCaption: captionSelect.value, appearance: { gridCaption: captionSelect.value } });
  });

  // 3. Checkerboard Mode Selection
  const checkerSelect = el('select', { className: 'select', id: 'setting-checkerboard' },
    el('option', { value: 'checker' }, 'Checkerboard Pattern'),
    el('option', { value: 'light' }, 'Solid Light Background'),
    el('option', { value: 'dark' }, 'Solid Dark Background')
  );

  checkerSelect.addEventListener('change', () => {
    try { localStorage.setItem('iconchest_grid_bg', checkerSelect.value); } catch {}
    saveSetting({ checkerboardMode: checkerSelect.value, appearance: { checkerboardMode: checkerSelect.value } });
  });

  // 4. Preview Info Overlay Default
  const overlayCheckbox = el('input', { type: 'checkbox', id: 'setting-preview-overlay' });

  overlayCheckbox.addEventListener('change', () => {
    saveSetting({ previewInfoOverlayDefault: overlayCheckbox.checked, appearance: { previewInfoOverlayDefault: overlayCheckbox.checked } });
  });

  const card = el('div', { className: 'card' },
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-theme' }, 'Application Theme'),
      themeSelect,
      el('div', { className: 'form-help' }, 'Select dark or light interface theme. Takes effect immediately.')
    ),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-grid-caption' }, 'Grid Thumbnail Caption'),
      captionSelect,
      el('div', { className: 'form-help' }, 'Information displayed under each icon thumbnail in the main browsing grid.')
    ),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-checkerboard' }, 'Transparency Background Mode'),
      checkerSelect,
      el('div', { className: 'form-help' }, 'Background style displayed behind transparent PNG and SVG graphics.')
    ),
    el('div', { className: 'form-group' },
      el('label', { className: 'checkbox-label' },
        overlayCheckbox,
        el('span', {}, 'Show image metadata info overlay in full-screen preview by default')
      )
    )
  );

  container.appendChild(card);

  // Load existing settings
  api.get('/api/settings').then(s => {
    const theme = (s.appearance?.theme || s.theme || 'dark').toLowerCase();
    themeSelect.value = theme;

    const caption = (s.appearance?.gridCaption || s.gridCaption || 'none').toLowerCase();
    captionSelect.value = caption;

    const checker = (s.appearance?.checkerboardMode || s.checkerboardMode || 'checker').toLowerCase();
    checkerSelect.value = checker;

    const overlay = s.appearance?.previewInfoOverlayDefault ?? s.previewInfoOverlayDefault ?? true;
    overlayCheckbox.checked = Boolean(overlay);
  }).catch(() => {});

  return container;
}

