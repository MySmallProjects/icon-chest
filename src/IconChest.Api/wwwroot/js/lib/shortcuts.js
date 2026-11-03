/**
 * Central registry of keyboard shortcuts for IconChest.
 * Single source of truth for UI, key handlers, and help documentation.
 */

export const SHORTCUTS = [
  // Grid Navigation & Selection
  {
    key: 'Arrow keys',
    description: 'Move focus and select cell',
    context: 'grid'
  },
  {
    key: 'Shift + Arrow keys',
    description: 'Extend range selection from anchor',
    context: 'grid'
  },
  {
    key: 'Ctrl / Cmd + Arrow keys',
    description: 'Move focus without changing selection',
    context: 'grid'
  },
  {
    key: 'Space',
    description: 'Toggle selection of focused cell',
    context: 'grid'
  },
  {
    key: 'Home',
    description: 'First cell in current row (or library with Ctrl / Cmd)',
    context: 'grid'
  },
  {
    key: 'End',
    description: 'Last cell in current row (or library with Ctrl / Cmd)',
    context: 'grid'
  },
  {
    key: 'Page Up / Page Down',
    description: 'Scroll by one screen of rows',
    context: 'grid'
  },
  {
    key: 'Ctrl / Cmd + A',
    description: 'Select all images in current view',
    context: 'grid'
  },
  {
    key: 'Escape',
    description: 'Clear image selection',
    context: 'grid'
  },
  {
    key: 'Enter',
    description: 'Open full-window image preview',
    context: 'grid'
  },
  {
    key: 'I',
    description: 'Toggle details panel (with image selection)',
    context: 'grid'
  },

  // Preview Overlay
  {
    key: 'Arrow Left / Arrow Right',
    description: 'Navigate to previous / next image',
    context: 'preview'
  },
  {
    key: 'Home / End',
    description: 'Jump to first / last image in preview',
    context: 'preview'
  },
  {
    key: 'I',
    description: 'Toggle metadata info overlay',
    context: 'preview'
  },
  {
    key: 'Escape',
    description: 'Close preview and return to grid',
    context: 'preview'
  },

  // Trees (Panel A)
  {
    key: 'Arrow Up / Arrow Down',
    description: 'Move up / down visible tree nodes',
    context: 'tree'
  },
  {
    key: 'Arrow Right',
    description: 'Expand node or move to first child',
    context: 'tree'
  },
  {
    key: 'Arrow Left',
    description: 'Collapse node or move to parent node',
    context: 'tree'
  },
  {
    key: 'Enter / Space',
    description: 'Select highlighted tree node',
    context: 'tree'
  },
  {
    key: 'Type letter',
    description: 'Jump to next tree node matching letter',
    context: 'tree'
  },

  // Details & Editing (Phase 8)
  {
    key: 'Ctrl / Cmd + S',
    description: 'Save metadata edits in details panel',
    context: 'details'
  },

  // Tag Management (Phase 8)
  {
    key: 'F2',
    description: 'Rename focused tag',
    context: 'tags'
  },
  {
    key: 'Delete',
    description: 'Delete focused tag',
    context: 'tags'
  },
  {
    key: 'Shift + F10 / Menu',
    description: 'Open context menu for focused tag',
    context: 'tags'
  }
];

export function isInputFocused() {
  const active = document.activeElement;
  if (!active) return false;
  const tag = active.tagName.toLowerCase();
  return (
    tag === 'input' ||
    tag === 'textarea' ||
    tag === 'select' ||
    active.isContentEditable ||
    active.getAttribute('role') === 'textbox'
  );
}

export function isModalOpen() {
  const dialogs = document.querySelectorAll('dialog[open]');
  return dialogs.length > 0;
}

