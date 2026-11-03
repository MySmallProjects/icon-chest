/**
 * WCAG 2.1 Contrast & Color Utility.
 * Pure logic module for relative luminance, contrast calculation, hex parsing,
 * and accessible preset pairs.
 */

/**
 * Parses a hex color string into { r, g, b } (0-255).
 * Supports #RGB, #RRGGBB, RGB, RRGGBB.
 */
export function parseHex(hex) {
  if (!hex || typeof hex !== 'string') return null;
  let str = hex.trim();
  if (str.startsWith('#')) {
    str = str.slice(1);
  }

  if (str.length === 3) {
    const r = parseInt(str[0] + str[0], 16);
    const g = parseInt(str[1] + str[1], 16);
    const b = parseInt(str[2] + str[2], 16);
    if (isNaN(r) || isNaN(g) || isNaN(b)) return null;
    return { r, g, b };
  }

  if (str.length === 6) {
    const r = parseInt(str.slice(0, 2), 16);
    const g = parseInt(str.slice(2, 4), 16);
    const b = parseInt(str.slice(4, 6), 16);
    if (isNaN(r) || isNaN(g) || isNaN(b)) return null;
    return { r, g, b };
  }

  return null;
}

/**
 * Normalizes hex to lowercase #rrggbb format.
 */
export function normalizeHex(hex) {
  const parsed = parseHex(hex);
  if (!parsed) return null;
  const to2 = (n) => n.toString(16).padStart(2, '0');
  return `#${to2(parsed.r)}${to2(parsed.g)}${to2(parsed.b)}`.toLowerCase();
}

/**
 * Computes WCAG 2.1 relative luminance for given RGB values (0-255).
 */
export function relativeLuminance(r, g, b) {
  const srgb = [r, g, b].map(val => {
    const s = val / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * srgb[0] + 0.7152 * srgb[1] + 0.0722 * srgb[2];
}

/**
 * Computes WCAG 2.1 contrast ratio between two hex colors (1 to 21).
 */
export function contrastRatio(hex1, hex2) {
  const c1 = parseHex(hex1);
  const c2 = parseHex(hex2);
  if (!c1 || !c2) return 1;

  const l1 = relativeLuminance(c1.r, c1.g, c1.b);
  const l2 = relativeLuminance(c2.r, c2.g, c2.b);

  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);

  return (lighter + 0.05) / (darker + 0.05);
}

/**
 * Checks if contrast meets WCAG 2.1 requirements (default 4.5:1 for normal text).
 */
export function isAccessible(hex1, hex2, minRatio = 4.5) {
  return contrastRatio(hex1, hex2) >= minRatio;
}

/**
 * Picks the best text color (#ffffff or #111827) for a given background color.
 */
export function getBestTextColor(bgHex) {
  const onWhite = contrastRatio(bgHex, '#ffffff');
  const onDark = contrastRatio(bgHex, '#111827');
  return onWhite >= onDark ? '#ffffff' : '#111827';
}

/**
 * 16 Accessible Preset Color Pairs (>= 4.5:1 contrast against their text color).
 */
export const PRESET_PAIRS = [
  { name: 'Red', bg: '#b91c1c', fg: '#ffffff' },
  { name: 'Dark Red', bg: '#991b1b', fg: '#ffffff' },
  { name: 'Amber', bg: '#92400e', fg: '#ffffff' },
  { name: 'Brown', bg: '#78350f', fg: '#ffffff' },
  { name: 'Yellow', bg: '#fef08a', fg: '#1f2937' },
  { name: 'Green', bg: '#15803d', fg: '#ffffff' },
  { name: 'Dark Green', bg: '#166534', fg: '#ffffff' },
  { name: 'Emerald', bg: '#047857', fg: '#ffffff' },
  { name: 'Teal', bg: '#0f766e', fg: '#ffffff' },
  { name: 'Cyan', bg: '#a5f3fc', fg: '#0f172a' },
  { name: 'Blue', bg: '#1d4ed8', fg: '#ffffff' },
  { name: 'Indigo', bg: '#4338ca', fg: '#ffffff' },
  { name: 'Violet', bg: '#6d28d9', fg: '#ffffff' },
  { name: 'Purple', bg: '#7e22ce', fg: '#ffffff' },
  { name: 'Pink', bg: '#be185d', fg: '#ffffff' },
  { name: 'Slate', bg: '#475569', fg: '#ffffff' }
];

