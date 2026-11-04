/**
 * Pure safe DOM builder for the Help and Query Reference documentation.
 * Strictly avoids innerHTML for data; uses el(), createSvg(), and textContent.
 */

import { el, createSvg } from './dom.js';

const ICON_PLAY = '<svg viewBox="0 0 24 24" width="14" height="14"><path fill="currentColor" d="M8 5v14l11-7z"/></svg>';

/**
 * Renders the complete Help page DOM content.
 * @param {Object} data
 * @param {Array} data.shortcuts - List of shortcut objects from shortcuts.js
 * @param {Object} data.queryHelp - Help model from /api/query/help
 * @param {Function} data.onTryQuery - Callback when user clicks "Try it" on an example
 * @returns {HTMLElement} Container element with all sections
 */
export function renderHelpContent(data = {}) {
  const { shortcuts = [], queryHelp = {}, onTryQuery = () => {} } = data;

  const container = el('div', { className: 'help-content-sections' }, [
    renderShortcutsSection(shortcuts),
    renderQueryReferenceSection(queryHelp, onTryQuery),
    renderFiltersGuideSection(),
    renderTipsSection()
  ]);

  return container;
}

/**
 * 1. Keyboard Shortcuts Section
 */
export function renderShortcutsSection(shortcuts) {
  const section = el('section', { id: 'section-shortcuts', className: 'help-section' }, [
    el('h2', { className: 'help-section-title' }, 'Keyboard Shortcuts'),
    el('p', { className: 'help-section-intro' },
      'IconChest is designed for keyboard-first efficiency. Every core feature has a dedicated, accessible shortcut.')
  ]);

  // Group shortcuts by context
  const groups = [
    { title: 'Grid Navigation & Selection', context: 'grid' },
    { title: 'Image Preview Overlay', context: 'preview' },
    { title: 'Sidebar Trees (Panel A)', context: 'tree' },
    { title: 'Details & Editing', context: 'details' },
    { title: 'Tag Management', context: 'tags' },
    { title: 'Filter Bar & Search', context: 'filter' }
  ];

  for (const group of groups) {
    const items = shortcuts.filter(s => s.context === group.context);
    if (items.length === 0) continue;

    const table = el('table', { className: 'help-table shortcuts-table' }, [
      el('thead', {}, [
        el('tr', {}, [
          el('th', { style: 'width: 240px;' }, 'Shortcut'),
          el('th', {}, 'Action')
        ])
      ]),
      el('tbody', {}, items.map(item =>
        el('tr', {}, [
          el('td', {}, [
            el('kbd', { className: 'kbd-shortcut' }, item.key)
          ]),
          el('td', {}, item.description)
        ])
      ))
    ]);

    section.appendChild(el('h3', { className: 'help-subsection-title' }, group.title));
    section.appendChild(table);
  }

  return section;
}

/**
 * 2. Query Language Reference Section
 */
export function renderQueryReferenceSection(queryHelp, onTryQuery) {
  const section = el('section', { id: 'section-query', className: 'help-section' }, [
    el('h2', { className: 'help-section-title' }, 'Advanced Query Language'),
    el('p', { className: 'help-section-intro' },
      'The advanced query language gives you full Boolean power across all image metadata, tags, and file attributes. Queries run directly against SQLite with sub-millisecond response times.')
  ]);

  // A. Fields Table
  if (Array.isArray(queryHelp.fields) && queryHelp.fields.length > 0) {
    section.appendChild(el('h3', { className: 'help-subsection-title' }, 'Search Fields'));
    const table = el('table', { className: 'help-table fields-table' }, [
      el('thead', {}, [
        el('tr', {}, [
          el('th', { style: 'width: 140px;' }, 'Field'),
          el('th', { style: 'width: 100px;' }, 'Type'),
          el('th', { style: 'width: 180px;' }, 'Operators'),
          el('th', {}, 'Description & Examples')
        ])
      ]),
      el('tbody', {}, queryHelp.fields.map(f => {
        const ops = (f.supportedOperators || []).map(op => el('code', { className: 'help-code-pill' }, op));
        return el('tr', {}, [
          el('td', {}, [el('code', { className: 'help-field-name' }, f.name)]),
          el('td', {}, [el('span', { className: 'help-type-badge' }, f.type)]),
          el('td', {}, ops),
          el('td', {}, [
            el('div', { className: 'help-field-desc' }, f.description || ''),
            f.exampleQuery ? el('div', { className: 'help-field-example' }, [
              el('span', { className: 'help-label-dim' }, 'e.g. '),
              el('code', {}, f.exampleQuery)
            ]) : el('span')
          ])
        ]);
      }))
    ]);
    section.appendChild(table);
  }

  // B. Examples by Category with "Try it" buttons
  if (Array.isArray(queryHelp.examples) && queryHelp.examples.length > 0) {
    section.appendChild(el('h3', { className: 'help-subsection-title' }, 'Query Examples'));

    // Group examples by category
    const catMap = new Map();
    for (const ex of queryHelp.examples) {
      const cat = ex.category || 'General';
      if (!catMap.has(cat)) catMap.set(cat, []);
      catMap.get(cat).push(ex);
    }

    for (const [catName, examples] of catMap.entries()) {
      const catBlock = el('div', { className: 'help-example-category' }, [
        el('h4', { className: 'help-cat-title' }, catName),
        el('div', { className: 'help-example-grid' }, examples.map(ex => {
          const qText = ex.query || ex.text || '';
          const tryBtn = el('button', {
            type: 'button',
            className: 'btn btn-xs btn-try-query',
            title: `Run query: ${qText}`,
            onClick: () => onTryQuery(qText)
          }, [
            createSvg(ICON_PLAY),
            el('span', {}, 'Try it')
          ]);

          return el('div', { className: 'help-example-card' }, [
            el('div', { className: 'help-example-header' }, [
              el('code', { className: 'help-example-code' }, qText),
              tryBtn
            ]),
            el('p', { className: 'help-example-desc' }, ex.description || '')
          ]);
        }))
      ]);
      section.appendChild(catBlock);
    }
  }

  return section;
}

/**
 * 3. Simple Filters Guide Section
 */
export function renderFiltersGuideSection() {
  return el('section', { id: 'section-filters', className: 'help-section' }, [
    el('h2', { className: 'help-section-title' }, 'Simple Filters Guide'),
    el('p', { className: 'help-section-intro' },
      'The Simple Filter bar lets you quickly narrow images using dropdowns, presets, and text search without learning query syntax.'),
    el('div', { className: 'help-guide-cards' }, [
      el('div', { className: 'help-guide-card' }, [
        el('h4', {}, 'Text Search'),
        el('p', {}, 'Entering multiple words (e.g. "search button") finds images where every word matches in either the file name, title, or description.')
      ]),
      el('div', { className: 'help-guide-card' }, [
        el('h4', {}, 'Tag Filtering'),
        el('p', {}, 'Filter by multiple tags with "Any tag" (OR) or "All tags" (AND) mode. Use "Untagged" to find images needing categorization.')
      ]),
      el('div', { className: 'help-guide-card' }, [
        el('h4', {}, 'Date Ranges & Presets'),
        el('p', {}, 'Quickly choose presets like "Last 7 days" or "This month", or enter exact YYYY-MM-DD bounds. The date filter applies to your chosen date field (Effective, Created, Modified, Taken, or Added).')
      ]),
      el('div', { className: 'help-guide-card' }, [
        el('h4', {}, 'Convert to Query'),
        el('p', {}, 'Click "Convert to query" at any time to turn your simple filter settings into an editable Advanced Query.')
      ])
    ])
  ]);
}

/**
 * 4. Search Tips Section
 */
export function renderTipsSection() {
  return el('section', { id: 'section-tips', className: 'help-section' }, [
    el('h2', { className: 'help-section-title' }, 'Tips & Tricks'),
    el('ul', { className: 'help-tips-list' }, [
      el('li', {}, [
        el('strong', {}, 'Press / or Ctrl+F anywhere: '),
        el('span', {}, 'Toggles the filter section open or closed, and focuses the filter text box or query editor.')
      ]),
      el('li', {}, [
        el('strong', {}, 'Bare Search Terms: '),
        el('span', {}, 'Typing a bare word like '),
        el('code', {}, 'arrow'),
        el('span', {}, ' automatically searches filenames, titles, and descriptions.')
      ]),
      el('li', {}, [
        el('strong', {}, 'Combining with Folders and Years: '),
        el('span', {}, 'Advanced queries work seamlessly inside folders and calendar years! The scope and your query combine with AND.')
      ]),
      el('li', {}, [
        el('strong', {}, 'Parentheses & Logic: '),
        el('span', {}, 'Use parentheses to group complex logic, e.g. '),
        el('code', {}, '(type == svg or type == png) and rating >= 4')
      ]),
      el('li', {}, [
        el('strong', {}, 'Wildcards in filenames: '),
        el('span', {}, 'Use * for multiple characters and ? for single characters: '),
        el('code', {}, 'filename like "icon-*-24.png"')
      ])
    ])
  ]);
}

