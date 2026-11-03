import { el } from '../lib/dom.js';
import { getBestTextColor, normalizeHex } from '../lib/contrast.js';
import { contextMenu } from './context-menu.js';
import { dialog } from '../lib/dialog.js';
import { tagsStore } from '../lib/tags-store.js';
import { toast } from '../lib/toast.js';

/**
 * Creates an accessible tag chip element with optional tri-state badges
 * and context menu (Rename, Recolour, Delete).
 */
export function createTagChip(tag, options = {}) {
  const isSelected = options.isSelected || options.selected || false;
  const showCount = options.showCount !== false;
  const triState = options.triState; // 'all' (1), 'some' (0.5), 'none' (0 / removal), or undefined
  const onClick = options.onClick || null;
  const onRemove = options.onRemove || null;
  const enableContextMenu = options.enableContextMenu !== false;
  const tagId = Number(tag.id || tag.tagId);

  const hex = normalizeHex(tag.color || tag.bgColor);
  const bg = hex || 'var(--color-bg-secondary, #334155)';
  const fg = hex ? getBestTextColor(hex) : 'inherit';

  let stateClass = '';
  let badgeIcon = '';

  if (triState === 'all' || triState === 1) {
    stateClass = 'state-all';
    badgeIcon = '✓';
  } else if (triState === 'some' || triState === 0.5) {
    stateClass = 'state-some';
    badgeIcon = '+';
  } else if (triState === 'none' || triState === 0) {
    stateClass = 'state-remove';
    badgeIcon = '×';
  }

  const chip = el('button', {
    type: 'button',
    className: `tag-chip ${isSelected ? 'selected' : ''} ${stateClass}`.trim(),
    style: {
      backgroundColor: bg,
      color: fg
    },
    title: tag.name,
    'aria-pressed': isSelected ? 'true' : 'false',
    dataset: { tagId: String(tag.id || tag.tagId) }
  });

  if (badgeIcon) {
    chip.appendChild(el('span', { className: 'tag-chip-state-icon' }, badgeIcon));
  }

  chip.appendChild(el('span', { className: 'tag-chip-name' }, tag.name));

  if (showCount && typeof tag.count === 'number') {
    chip.appendChild(el('span', { className: 'tag-chip-count' }, String(tag.count)));
  }

  if (onRemove && triState === undefined) {
    const removeBtn = el('span', {
      className: 'tag-chip-remove-btn',
      role: 'button',
      tabindex: '0',
      'aria-label': `Remove tag ${tag.name}`,
      title: 'Remove tag',
      onClick: (e) => {
        e.stopPropagation();
        onRemove(tag);
      }
    }, '×');
    chip.appendChild(removeBtn);
  }

  if (onClick) {
    chip.addEventListener('click', (e) => {
      onClick(tag, e);
    });
  }

  // Context Menu & Keyboard Management
  if (enableContextMenu) {
    const handleContextMenu = (e) => {
      e.preventDefault();
      e.stopPropagation();

      const x = e.clientX || chip.getBoundingClientRect().left;
      const y = e.clientY || chip.getBoundingClientRect().bottom;

      contextMenu.open({
        x,
        y,
        invoker: chip,
        items: [
          {
            id: 'rename',
            label: 'Rename',
            shortcut: 'F2',
            action: async () => {
              const newName = await dialog.promptTagRename({ currentName: tag.name });
              if (newName && newName !== tag.name) {
                try {
                  await tagsStore.renameTag(tagId, newName);
                  toast.success(`Renamed tag to "${newName}"`);
                } catch (err) {
                  toast.error(`Failed to rename tag: ${err.message || err}`);
                }
              }
            }
          },
          {
            id: 'recolor',
            label: 'Recolour',
            action: async () => {
              const newColor = await dialog.promptTagRecolor({
                currentColor: tag.color || tag.bgColor,
                tagName: tag.name
              });
              if (newColor && newColor !== (tag.color || tag.bgColor)) {
                try {
                  await tagsStore.recolorTag(tagId, newColor);
                  toast.success(`Recoloured tag "${tag.name}"`);
                } catch (err) {
                  toast.error(`Failed to recolour tag: ${err.message || err}`);
                }
              }
            }
          },
          {
            id: 'delete',
            label: 'Delete',
            shortcut: 'Del',
            danger: true,
            action: async () => {
              const result = await dialog.promptTagDelete({
                tagName: tag.name,
                imageCount: tag.count || 0
              });
              if (result.confirmed) {
                try {
                  await tagsStore.deleteTag(tagId);
                  toast.success(`Deleted tag "${tag.name}"`);
                } catch (err) {
                  toast.error(`Failed to delete tag: ${err.message || err}`);
                }
              }
            }
          }
        ]
      });
    };

    chip.addEventListener('contextmenu', handleContextMenu);

    chip.addEventListener('keydown', (e) => {
      if ((e.shiftKey && e.key === 'F10') || e.key === 'ContextMenu') {
        const rect = chip.getBoundingClientRect();
        handleContextMenu({
          preventDefault: () => e.preventDefault(),
          stopPropagation: () => e.stopPropagation(),
          clientX: rect.left,
          clientY: rect.bottom
        });
      } else if (e.key === 'F2') {
        e.preventDefault();
        (async () => {
          const newName = await dialog.promptTagRename({ currentName: tag.name });
          if (newName && newName !== tag.name) {
            try {
              await tagsStore.renameTag(tagId, newName);
              toast.success(`Renamed tag to "${newName}"`);
            } catch (err) {
              toast.error(`Failed to rename tag: ${err.message || err}`);
            }
          }
        })();
      } else if (e.key === 'Delete') {
        e.preventDefault();
        (async () => {
          const result = await dialog.promptTagDelete({
            tagName: tag.name,
            imageCount: tag.count || 0
          });
          if (result.confirmed) {
            try {
              await tagsStore.deleteTag(tagId);
              toast.success(`Deleted tag "${tag.name}"`);
            } catch (err) {
              toast.error(`Failed to delete tag: ${err.message || err}`);
            }
          }
        })();
      }
    });
  }

  return chip;
}
