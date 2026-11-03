import { createImageBrowser } from './image-browser.js';

export function createFavouritesView() {
  const browser = createImageBrowser({
    viewId: 'favourites',
    defaultTitle: 'Favourites',
    initialFilter: {
      favourite: true
    }
  });

  return {
    element: browser.element,
    onActivated: () => {
      if (typeof browser.onActivated === 'function') {
        browser.onActivated();
      }
    },
    refresh: () => {
      browser.refresh();
    }
  };
}
