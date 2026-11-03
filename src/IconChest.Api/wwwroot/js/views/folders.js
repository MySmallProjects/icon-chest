import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { TreeView } from '../components/tree.js';
import { createImageBrowser } from './image-browser.js';

export function createFoldersView() {
  const browser = createImageBrowser({
    viewId: 'folders',
    defaultTitle: 'All Folders'
  });

  let includeSubfolders = localStorage.getItem('iconchest_include_subfolders') !== 'false';
  let folderTree = [];
  let selectedNode = null;

  // Panel A elements
  const panelAContent = el('div', { className: 'panel-a-content folders-nav' });

  // Options Toolbar
  const subfoldersCheckbox = el('input', {
    type: 'checkbox',
    id: 'cb-include-subfolders',
    checked: includeSubfolders,
    onChange: (e) => {
      includeSubfolders = e.target.checked;
      localStorage.setItem('iconchest_include_subfolders', String(includeSubfolders));
      applySelection();
    }
  });

  const subfoldersLabel = el('label', {
    htmlFor: 'cb-include-subfolders',
    className: 'tree-option-label'
  }, [
    subfoldersCheckbox,
    el('span', {}, 'Include subfolders')
  ]);

  const allFoldersBtn = el('button', {
    type: 'button',
    className: 'tree-root-btn',
    onClick: () => {
      selectedNode = null;
      if (treeView) treeView.select(null);
      browser.setFilter({}, 'All Folders');
    }
  }, [
    el('span', { className: 'tree-root-label' }, 'All Images')
  ]);

  const treeContainer = el('div', { className: 'folders-tree-wrap' });

  const treeView = new TreeView({
    roots: [],
    getId: node => node.path,
    getName: node => node.name,
    getChildren: node => node.children || [],
    onSelect: (path) => {
      if (!path) {
        selectedNode = null;
        browser.setFilter({}, 'All Folders');
        return;
      }
      selectedNode = treeView.model.findNode(path);
      applySelection();
    }
  });

  treeContainer.appendChild(treeView.containerEl);

  panelAContent.appendChild(allFoldersBtn);
  panelAContent.appendChild(subfoldersLabel);
  panelAContent.appendChild(treeContainer);

  function applySelection() {
    if (!selectedNode) {
      browser.setFilter({}, 'All Folders');
    } else {
      browser.setFilter({
        directory: selectedNode.path,
        includeSubfolders
      }, selectedNode.name || selectedNode.path);
    }
  }

  async function loadTree() {
    try {
      const data = await api.get('/api/tree/folders');
      folderTree = data || [];
      treeView.setRoots(folderTree);
    } catch (err) {
      console.error('Failed to load folder tree:', err);
    }
  }

  loadTree();

  return {
    element: browser.element,
    renderPanelA: () => {
      loadTree();
      return panelAContent;
    },
    refresh: () => {
      loadTree();
      browser.refresh();
    }
  };
}
