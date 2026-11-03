import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { createTagChip } from '../components/tag-chip.js';
import { createImageBrowser } from './image-browser.js';
import { tagsStore } from '../lib/tags-store.js';

export function createTagsView() {
  const browser = createImageBrowser({
    viewId: 'tags',
    defaultTitle: 'All Tags'
  });

  const selectedTags = new Set();
  let tagMode = localStorage.getItem('iconchest_tag_mode') || 'any'; // 'any' | 'all'
  let tagsList = [];
  let filterText = '';

  // Panel A
  const panelAContent = el('div', { className: 'panel-a-content tags-nav' });

  // Mode Switch
  const modeSelect = el('select', {
    className: 'select tags-mode-select',
    'aria-label': 'Tag match mode',
    onChange: (e) => {
      tagMode = e.target.value;
      localStorage.setItem('iconchest_tag_mode', tagMode);
      applySelection();
    }
  }, [
    el('option', { value: 'any' }, 'Match Any Tag (OR)'),
    el('option', { value: 'all' }, 'Match All Tags (AND)')
  ]);
  modeSelect.value = tagMode;

  // Filter input
  const searchInput = el('input', {
    type: 'search',
    className: 'input tags-search-input',
    placeholder: 'Filter tags...',
    'aria-label': 'Filter tags',
    onInput: (e) => {
      filterText = e.target.value.toLowerCase().trim();
      renderTagChips();
    }
  });

  const allTagsBtn = el('button', {
    type: 'button',
    className: 'tree-root-btn',
    onClick: () => {
      selectedTags.clear();
      renderTagChips();
      browser.setFilter({}, 'All Tags');
    }
  }, [
    el('span', { className: 'tree-root-label' }, 'All Images')
  ]);

  const chipsContainer = el('div', { className: 'tags-chips-container' });

  panelAContent.appendChild(allTagsBtn);
  panelAContent.appendChild(el('div', { className: 'form-group' }, [
    el('label', { className: 'tree-field-label' }, 'Match Mode:'),
    modeSelect
  ]));
  panelAContent.appendChild(el('div', { className: 'form-group' }, [
    searchInput
  ]));
  panelAContent.appendChild(chipsContainer);

  function applySelection() {
    if (selectedTags.size === 0) {
      browser.setFilter({}, 'All Tags');
    } else {
      const tagArray = Array.from(selectedTags);
      const title = `${tagArray.join(tagMode === 'all' ? ' + ' : ' / ')}`;
      browser.setFilter({
        tag: tagArray,
        tagMode
      }, title);
    }
  }

  function renderTagChips() {
    clear(chipsContainer);

    const filtered = tagsList.filter(t => !filterText || t.name.toLowerCase().includes(filterText));

    if (filtered.length === 0) {
      chipsContainer.appendChild(el('div', { className: 'tree-empty-message' }, 'No tags found'));
      return;
    }

    for (const tag of filtered) {
      const isSelected = selectedTags.has(tag.name);
      const chip = createTagChip(tag, {
        selected: isSelected,
        onClick: () => {
          if (selectedTags.has(tag.name)) {
            selectedTags.delete(tag.name);
          } else {
            selectedTags.add(tag.name);
          }
          renderTagChips();
          applySelection();
        }
      });
      chipsContainer.appendChild(chip);
    }
  }

  tagsStore.init();
  tagsStore.subscribe((tags) => {
    tagsList = tags || [];
    renderTagChips();
  });

  async function loadTags() {
    try {
      await tagsStore.fetchTags();
    } catch (err) {
      console.error('Failed to load tags:', err);
    }
  }

  loadTags();

  return {
    element: browser.element,
    renderPanelA: () => {
      loadTags();
      return panelAContent;
    },
    onActivated: () => {
      if (typeof browser.onActivated === 'function') {
        browser.onActivated();
      }
    },
    refresh: () => {
      loadTags();
      browser.refresh();
    }
  };
}
