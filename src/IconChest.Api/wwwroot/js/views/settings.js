import { el, clear } from '../lib/dom.js';
import { setRestartBannerContainer } from '../settings/helper.js';
import { renderLibrarySection } from '../settings/library.js';
import { renderAppearanceSection } from '../settings/appearance.js';
import { renderBehaviourSection } from '../settings/behaviour.js';
import { renderPerformanceSection } from '../settings/performance.js';
import { renderCleanupSection } from '../settings/cleanup.js';
import { renderLoggingSection } from '../settings/logging.js';
import { renderApiSection } from '../settings/api.js';
import { renderDataSection } from '../settings/data.js';
import { renderAppInfoSection } from '../settings/appinfo.js';

const SECTIONS = [
  { id: 'library', title: 'Library Folders', render: renderLibrarySection },
  { id: 'appearance', title: 'Appearance', render: renderAppearanceSection },
  { id: 'behaviour', title: 'Behaviour', render: renderBehaviourSection },
  { id: 'performance', title: 'Performance', render: renderPerformanceSection },
  { id: 'cleanup', title: 'Cleanup', render: renderCleanupSection },
  { id: 'logging', title: 'Logging', render: renderLoggingSection },
  { id: 'api', title: 'API & Network', render: renderApiSection },
  { id: 'data', title: 'Data & Backups', render: renderDataSection },
  { id: 'appinfo', title: 'App Info', render: renderAppInfoSection }
];

export function createSettingsView() {
  const container = el('div', { className: 'view-container settings-layout' });

  const nav = el('nav', { className: 'settings-nav', 'aria-label': 'Settings Sections' });
  const content = el('div', { className: 'settings-content' });
  const restartBannerContainer = el('div', { id: 'settings-restart-banner' });
  setRestartBannerContainer(restartBannerContainer);

  const sectionSlot = el('div', { id: 'settings-active-section' });

  content.appendChild(restartBannerContainer);
  content.appendChild(sectionSlot);

  container.appendChild(nav);
  container.appendChild(content);

  const navLinks = new Map();
  let currentSectionId = 'library';

  for (const sec of SECTIONS) {
    const link = el('a', {
      href: `#settings/${sec.id}`,
      className: 'settings-nav-item',
      id: `settings-nav-${sec.id}`,
      onClick: (e) => {
        e.preventDefault();
        window.location.hash = `#settings/${sec.id}`;
      }
    }, sec.title);

    nav.appendChild(link);
    navLinks.set(sec.id, link);
  }

  function showSection(sectionId) {
    const target = SECTIONS.find(s => s.id === sectionId) || SECTIONS[0];
    currentSectionId = target.id;

    for (const [id, link] of navLinks) {
      if (id === target.id) {
        link.classList.add('active');
        link.setAttribute('aria-current', 'page');
      } else {
        link.classList.remove('active');
        link.removeAttribute('aria-current');
      }
    }

    clear(sectionSlot);
    sectionSlot.appendChild(target.render());
  }

  return {
    element: container,
    showSection,
    getCurrentSection: () => currentSectionId
  };
}

