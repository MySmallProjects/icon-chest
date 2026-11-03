/**
 * Safe DOM creation and manipulation helpers.
 * Never constructs HTML from string concatenation of server data.
 */

export function el(tag, props = {}, ...children) {
  const elem = document.createElement(tag);

  for (const [key, value] of Object.entries(props)) {
    if (value == null || value === false) {
      continue;
    }

    if (key.startsWith('on') && typeof value === 'function') {
      const eventName = key.slice(2).toLowerCase();
      elem.addEventListener(eventName, value);
    } else if (key === 'className' || key === 'class') {
      elem.className = value;
    } else if (key === 'dataset' && typeof value === 'object') {
      for (const [dataKey, dataVal] of Object.entries(value)) {
        elem.dataset[dataKey] = dataVal;
      }
    } else if (key === 'style' && typeof value === 'object') {
      Object.assign(elem.style, value);
    } else if (key === 'disabled' || key === 'checked' || key === 'hidden' || key === 'readOnly') {
      elem[key] = Boolean(value);
    } else {
      elem.setAttribute(key, String(value));
    }
  }

  for (const child of children.flat(Infinity)) {
    if (child == null || child === false) {
      continue;
    }
    if (typeof child === 'string' || typeof child === 'number') {
      elem.appendChild(document.createTextNode(String(child)));
    } else if (child instanceof Node) {
      elem.appendChild(child);
    }
  }

  return elem;
}

export function clear(element) {
  while (element.firstChild) {
    element.removeChild(element.firstChild);
  }
  return element;
}

/**
 * Creates SVG element safely from trusted inline SVG markup (no server data).
 */
export function createSvg(svgMarkup) {
  const template = document.createElement('template');
  template.innerHTML = svgMarkup.trim();
  const el = template.content.firstElementChild;
  if (el && el.namespaceURI === 'http://www.w3.org/2000/svg') {
    return el.cloneNode(true);
  }
  const withNs = svgMarkup.includes('xmlns=')
    ? svgMarkup
    : svgMarkup.replace(/<svg\b/i, '<svg xmlns="http://www.w3.org/2000/svg"');
  const parser = new DOMParser();
  const doc = parser.parseFromString(withNs, 'image/svg+xml');
  return document.importNode(doc.documentElement, true);
}

