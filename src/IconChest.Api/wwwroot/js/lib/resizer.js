import { store } from './store.js';

export const MIN_PANEL_A_WIDTH = 180;
export const MAX_PANEL_A_WIDTH = 600;
export const DEFAULT_PANEL_A_WIDTH = 280;

export function clampWidth(targetWidth, windowWidth = window.innerWidth) {
  const maxAllowed = Math.min(MAX_PANEL_A_WIDTH, Math.floor(windowWidth * 0.5));
  const minAllowed = Math.min(MIN_PANEL_A_WIDTH, maxAllowed);
  return Math.max(minAllowed, Math.min(targetWidth, maxAllowed));
}

export function initResizer(resizerEl, panelAEl) {
  let isDragging = false;
  let startX = 0;
  let startWidth = DEFAULT_PANEL_A_WIDTH;

  function applyWidth(width) {
    const clamped = clampWidth(width);
    panelAEl.style.width = `${clamped}px`;
    resizerEl.setAttribute('aria-valuenow', String(clamped));
    store.set('panelAWidth', clamped);
    return clamped;
  }

  // Initial setup
  const initialWidth = clampWidth(store.get('panelAWidth') || DEFAULT_PANEL_A_WIDTH);
  panelAEl.style.width = `${initialWidth}px`;
  resizerEl.setAttribute('role', 'separator');
  resizerEl.setAttribute('tabindex', '0');
  resizerEl.setAttribute('aria-orientation', 'vertical');
  resizerEl.setAttribute('aria-label', 'Resize navigation pane');
  resizerEl.setAttribute('aria-valuemin', String(MIN_PANEL_A_WIDTH));
  resizerEl.setAttribute('aria-valuemax', String(MAX_PANEL_A_WIDTH));
  resizerEl.setAttribute('aria-valuenow', String(initialWidth));

  // Pointer drag
  resizerEl.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    isDragging = true;
    startX = e.clientX;
    startWidth = panelAEl.getBoundingClientRect().width;
    resizerEl.classList.add('is-dragging');
    resizerEl.setPointerCapture(e.pointerId);
    e.preventDefault();
  });

  resizerEl.addEventListener('pointermove', (e) => {
    if (!isDragging) return;
    const deltaX = e.clientX - startX;
    applyWidth(startWidth + deltaX);
  });

  const stopDrag = (e) => {
    if (!isDragging) return;
    isDragging = false;
    resizerEl.classList.remove('is-dragging');
    try {
      resizerEl.releasePointerCapture(e.pointerId);
    } catch {}
  };

  resizerEl.addEventListener('pointerup', stopDrag);
  resizerEl.addEventListener('pointercancel', stopDrag);

  // Double click resets to default
  resizerEl.addEventListener('dblclick', () => {
    applyWidth(DEFAULT_PANEL_A_WIDTH);
  });

  // Keyboard navigation
  resizerEl.addEventListener('keydown', (e) => {
    const current = panelAEl.getBoundingClientRect().width;
    const step = e.shiftKey ? 40 : 10;

    if (e.key === 'ArrowLeft') {
      applyWidth(current - step);
      e.preventDefault();
    } else if (e.key === 'ArrowRight') {
      applyWidth(current + step);
      e.preventDefault();
    } else if (e.key === 'Home') {
      applyWidth(MIN_PANEL_A_WIDTH);
      e.preventDefault();
    } else if (e.key === 'End') {
      applyWidth(MAX_PANEL_A_WIDTH);
      e.preventDefault();
    }
  });

  // Window resize check
  window.addEventListener('resize', () => {
    const current = panelAEl.getBoundingClientRect().width;
    const clamped = clampWidth(current);
    if (clamped !== current) {
      applyWidth(clamped);
    }
  });
}

