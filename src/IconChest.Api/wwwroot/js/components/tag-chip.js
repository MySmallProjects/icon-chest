import { el } from '../lib/dom.js';

/**
 * Creates an accessible tag chip element.
 */
export function createTagChip(tag, options = {}) {
  const isSelected = options.isSelected || false;
  const showCount = options.showCount !== false;
  const onClick = options.onClick || null;

  const style = [];
  if (tag.bgColor) style.push(`background-color: ${tag.bgColor}`);
  if (tag.fgColor) style.push(`color: ${tag.fgColor}`);

  const chip = el('button', {
    type: 'button',
    className: `tag-chip ${isSelected ? 'selected' : ''}`,
    style: style.join('; '),
    title: tag.name,
    'aria-pressed': isSelected ? 'true' : 'false'
  }, [
    el('span', { className: 'tag-chip-name' }, tag.name)
  ]);

  if (showCount && typeof tag.count === 'number') {
    chip.appendChild(el('span', { className: 'tag-chip-count' }, String(tag.count)));
  }

  if (onClick) {
    chip.addEventListener('click', (e) => {
      onClick(tag, e);
    });
  }

  return chip;
}

