/**
 * Help and Query Reference View.
 * Displays full keyboard shortcut documentation, interactive query language reference,
 * simple filter instructions, and searchable tips.
 */

import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { SHORTCUTS } from '../lib/shortcuts.js';
import { renderHelpContent } from '../lib/help-render.js';
import { store } from '../lib/store.js';

export function createHelpView() {
  const container = el('div', { className: 'view-container help-view-container' });

  // Sub-navigation bar
  const navShortcuts = el('a', {
    href: '#help/shortcuts',
    className: 'help-subnav-link active',
    onClick: (e) => { e.preventDefault(); scrollToSection('section-shortcuts'); }
  }, 'Shortcuts');

  const navQuery = el('a', {
    href: '#help/query',
    className: 'help-subnav-link',
    onClick: (e) => { e.preventDefault(); scrollToSection('section-query'); }
  }, 'Query Reference');

  const navFilters = el('a', {
    href: '#help/filters',
    className: 'help-subnav-link',
    onClick: (e) => { e.preventDefault(); scrollToSection('section-filters'); }
  }, 'Filters Guide');

  const navTips = el('a', {
    href: '#help/tips',
    className: 'help-subnav-link',
    onClick: (e) => { e.preventDefault(); scrollToSection('section-tips'); }
  }, 'Tips');

  const subNav = el('nav', {
    className: 'help-subnav',
    'aria-label': 'Help sections'
  }, [
    navShortcuts,
    navQuery,
    navFilters,
    navTips
  ]);

  const contentArea = el('div', { className: 'help-scroll-area' }, [
    el('div', { className: 'help-loading' }, 'Loading query reference documentation...')
  ]);

  container.appendChild(subNav);
  container.appendChild(contentArea);

  function scrollToSection(sectionId) {
    const targetHash = `#help/${sectionId.replace('section-', '')}`;
    if (window.location.hash !== targetHash) {
      window.location.hash = targetHash;
    }
    const secEl = container.querySelector(`#${sectionId}`);
    if (secEl) {
      secEl.scrollIntoView({ behavior: 'smooth' });
    }
    updateNavLinks(sectionId);
  }

  function showSection(sectionId) {
    const targetId = sectionId ? `section-${sectionId}` : 'section-shortcuts';
    scrollToSection(targetId);
  }

  function updateNavLinks(activeSectionId) {
    const links = [
      { id: 'section-shortcuts', el: navShortcuts },
      { id: 'section-query', el: navQuery },
      { id: 'section-filters', el: navFilters },
      { id: 'section-tips', el: navTips }
    ];

    for (const item of links) {
      if (item.id === activeSectionId) {
        item.el.classList.add('active');
        item.el.setAttribute('aria-current', 'true');
      } else {
        item.el.classList.remove('active');
        item.el.removeAttribute('aria-current');
      }
    }
  }

  async function loadData() {
    let queryHelp = {};
    try {
      queryHelp = await api.getQueryHelp();
    } catch (err) {
      console.warn('Could not load query help:', err);
    }

    clear(contentArea);

    const helpContent = renderHelpContent({
      shortcuts: SHORTCUTS,
      queryHelp,
      onTryQuery: (queryText) => {
        // Store query to run and navigate to folders
        store.set('preloadQuery', queryText);
        window.location.hash = '#folders';
      }
    });

    contentArea.appendChild(helpContent);

    // Check current hash for deep link
    handleCurrentHash();
  }

  function handleCurrentHash() {
    const hash = window.location.hash || '';
    if (hash.includes('#help/query')) {
      scrollToSection('section-query');
    } else if (hash.includes('#help/filters')) {
      scrollToSection('section-filters');
    } else if (hash.includes('#help/tips')) {
      scrollToSection('section-tips');
    } else {
      updateNavLinks('section-shortcuts');
    }
  }

  // Load documentation on init
  loadData();

  window.addEventListener('hashchange', () => {
    if (window.location.hash.startsWith('#help')) {
      handleCurrentHash();
    }
  });

  return {
    element: container,
    containerEl: container,
    showSection,
    refresh: () => loadData()
  };
}
