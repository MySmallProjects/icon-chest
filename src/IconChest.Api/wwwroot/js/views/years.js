import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { TreeView } from '../components/tree.js';
import { createImageBrowser } from './image-browser.js';

const MONTH_FORMATTER = new Intl.DateTimeFormat('en-US', { month: 'long' });

export function createYearsView() {
  const browser = createImageBrowser({
    viewId: 'years',
    defaultTitle: 'All Years'
  });

  let dateField = localStorage.getItem('iconchest_years_date_field') || 'effective';
  let selectedNode = null;

  // Panel A
  const panelAContent = el('div', { className: 'panel-a-content years-nav' });

  // Date Field Selector
  const fieldSelect = el('select', {
    className: 'select years-field-select',
    'aria-label': 'Date field for grouping',
    onChange: (e) => {
      dateField = e.target.value;
      localStorage.setItem('iconchest_years_date_field', dateField);
      loadTree();
      applySelection();
    }
  }, [
    el('option', { value: 'effective' }, 'Effective Date'),
    el('option', { value: 'created' }, 'Created Date'),
    el('option', { value: 'modified' }, 'Modified Date'),
    el('option', { value: 'taken' }, 'Taken Date'),
    el('option', { value: 'added' }, 'Added Date')
  ]);
  fieldSelect.value = dateField;

  const allYearsBtn = el('button', {
    type: 'button',
    className: 'tree-root-btn',
    onClick: () => {
      selectedNode = null;
      if (treeView) treeView.select(null);
      browser.setFilter({}, 'All Years');
    }
  }, [
    el('span', { className: 'tree-root-label' }, 'All Images')
  ]);

  const treeContainer = el('div', { className: 'years-tree-wrap' });

  const treeView = new TreeView({
    roots: [],
    getId: node => node.id,
    getName: node => node.name,
    getChildren: node => node.children || [],
    onSelect: (id) => {
      if (!id) {
        selectedNode = null;
        browser.setFilter({}, 'All Years');
        return;
      }
      selectedNode = treeView.model.findNode(id);
      applySelection();
    }
  });

  treeContainer.appendChild(treeView.containerEl);

  panelAContent.appendChild(allYearsBtn);
  panelAContent.appendChild(el('div', { className: 'form-group' }, [
    el('label', { className: 'tree-field-label' }, 'Group by:'),
    fieldSelect
  ]));
  panelAContent.appendChild(treeContainer);

  function applySelection() {
    if (!selectedNode) {
      browser.setFilter({}, 'All Years');
      return;
    }

    if (selectedNode.isUnknown) {
      browser.setFilter({
        dateField,
        year: 'unknown'
      }, `Unknown Date (${dateField})`);
    } else if (selectedNode.month) {
      browser.setFilter({
        dateField,
        year: String(selectedNode.year),
        month: selectedNode.month
      }, `${selectedNode.name} ${selectedNode.year}`);
    } else if (selectedNode.year) {
      browser.setFilter({
        dateField,
        year: String(selectedNode.year)
      }, `${selectedNode.year}`);
    }
  }

  async function loadTree() {
    try {
      const res = await api.get(`/api/tree/years?field=${encodeURIComponent(dateField)}`);
      const yearsData = res.years || [];
      const unknownCount = res.unknownCount || 0;

      const roots = [];

      for (const y of yearsData) {
        const yearNode = {
          id: `y-${y.year}`,
          name: String(y.year),
          year: y.year,
          count: y.count,
          children: []
        };

        if (Array.isArray(y.months)) {
          for (const m of y.months) {
            const dateObj = new Date(2000, m.month - 1, 1);
            const monthName = MONTH_FORMATTER.format(dateObj);

            yearNode.children.push({
              id: `m-${y.year}-${m.month}`,
              name: monthName,
              year: y.year,
              month: m.month,
              count: m.count,
              children: []
            });
          }
        }

        roots.push(yearNode);
      }

      if (unknownCount > 0) {
        roots.push({
          id: 'unknown',
          name: 'Unknown',
          isUnknown: true,
          count: unknownCount,
          children: []
        });
      }

      treeView.setRoots(roots);
    } catch (err) {
      console.error('Failed to load years tree:', err);
    }
  }

  loadTree();

  return {
    element: browser.element,
    renderPanelA: () => {
      loadTree();
      return panelAContent;
    },
    onActivated: () => {
      if (typeof browser.onActivated === 'function') {
        browser.onActivated();
      }
    },
    refresh: () => {
      loadTree();
      browser.refresh();
    }
  };
}
