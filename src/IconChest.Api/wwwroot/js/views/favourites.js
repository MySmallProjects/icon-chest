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
    refresh: () => {
      browser.refresh();
    }
  };
}
