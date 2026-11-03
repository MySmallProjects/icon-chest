import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { createTagChip } from './tag-chip.js';
import { initResizer } from '../lib/resizer.js';

/**
 * Resizable details panel (read-only for Phase 7).
 */
export class DetailsPanel {
  constructor(options = {}) {
    this.containerEl = options.containerEl || el('aside', {
      id: 'panel-details',
      className: 'panel-details',
      'aria-label': 'Image Details'
    });

    this.isOpen = false;
    this.userClosed = false;
    this.selectedIds = [];
    this.backgroundMode = options.backgroundMode || 'checker';
    this.cachedDetails = new Map(); // id -> details

    this._buildStructure();
  }

  _buildStructure() {
    clear(this.containerEl);

    // Header
    this.headerEl = el('div', { className: 'panel-details-header' }, [
      el('h3', { className: 'panel-details-title' }, 'Details'),
      el('button', {
        type: 'button',
        className: 'panel-details-close-btn',
        'aria-label': 'Close details panel',
        title: 'Close details'
      }, [
        el('svg', { viewBox: '0 0 24 24', width: '18', height: '18' }, [
          el('path', { d: 'M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z' })
        ])
      ])
    ]);

    this.headerEl.querySelector('.panel-details-close-btn').addEventListener('click', () => {
      this.closeByUser();
    });

    // Content scroll body
    this.contentEl = el('div', { className: 'panel-details-content' });

    this.containerEl.appendChild(this.headerEl);
    this.containerEl.appendChild(this.contentEl);

    // Initial state: hidden
    this.containerEl.classList.add('collapsed');
  }

  setBackgroundMode(mode) {
    this.backgroundMode = mode;
    const thumbArea = this.contentEl.querySelector('.details-thumb-area');
    if (thumbArea) {
      thumbArea.className = `details-thumb-area bg-${mode}`;
    }
  }

  updateSelection(ids) {
    this.selectedIds = ids || [];

    if (this.selectedIds.length === 0) {
      this._slideOut();
      return;
    }

    if (!this.userClosed) {
      this._slideIn();
    }

    this._renderContent();
  }

  toggle() {
    if (this.isOpen) {
      this.closeByUser();
    } else {
      this.openByUser();
    }
  }

  openByUser() {
    this.userClosed = false;
    this._slideIn();
    if (this.selectedIds.length > 0) {
      this._renderContent();
    }
  }

  closeByUser() {
    this.userClosed = true;
    this._slideOut();
  }

  _slideIn() {
    this.isOpen = true;
    this.containerEl.classList.remove('collapsed');
  }

  _slideOut() {
    this.isOpen = false;
    this.containerEl.classList.add('collapsed');
  }

  async _renderContent() {
    clear(this.contentEl);

    if (this.selectedIds.length === 0) {
      this.contentEl.appendChild(el('p', { className: 'details-empty' }, 'No images selected.'));
      return;
    }

    if (this.selectedIds.length === 1) {
      await this._renderSingle(this.selectedIds[0]);
    } else {
      this._renderMultiple(this.selectedIds);
    }
  }

  async _renderSingle(id) {
    this.contentEl.appendChild(el('div', { className: 'details-skeleton' }, 'Loading details...'));

    let details = this.cachedDetails.get(id);
    if (!details) {
      try {
        details = await api.getImage(id);
        this.cachedDetails.set(id, details);
      } catch (err) {
        console.error('Failed to load details for id', id, err);
        clear(this.contentEl);
        this.contentEl.appendChild(el('div', { className: 'details-error' }, 'Failed to load details'));
        return;
      }
    }

    if (this.selectedIds.length !== 1 || this.selectedIds[0] !== id) return;
    clear(this.contentEl);

    // 1. 256 Thumbnail
    const fileName = details.fileName || details.name || 'Untitled';
    const thumbArea = el('div', { className: `details-thumb-area bg-${this.backgroundMode}` }, [
      el('img', {
        className: 'details-thumb-img',
        src: `/api/images/${id}/thumbnail?size=256`,
        alt: fileName
      })
    ]);
    this.contentEl.appendChild(thumbArea);

    // 2. Title & Filename
    const title = details.title || fileName;
    this.contentEl.appendChild(el('h4', { className: 'details-image-title' }, title));
    this.contentEl.appendChild(el('div', { className: 'details-filename' }, fileName));

    // 3. Full Path (clickable to copy)
    const filePath = details.path || details.directory || '';
    const pathBtn = el('button', {
      type: 'button',
      className: 'details-path-copy-btn',
      title: 'Click to copy full path'
    }, [
      el('span', { className: 'path-text' }, filePath),
      el('span', { className: 'copy-icon' }, '📋')
    ]);

    pathBtn.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(filePath);
        toast.info('Path copied to clipboard');
      } catch {
        toast.error('Could not copy to clipboard');
      }
    });
    this.contentEl.appendChild(pathBtn);

    // 4. Properties Grid
    const hash = details.hash || details.contentHash;
    const props = [
      ['Format', (details.format || '').toUpperCase()],
      ['Dimensions', (details.width && details.height) ? `${details.width} × ${details.height}` : 'Vector / Unknown'],
      ['File Size', this._formatBytes(details.sizeBytes ?? details.fileSizeBytes)],
      ['Effective Date', this._formatDate(details.effectiveDateUtc)],
      ['Created', this._formatDate(details.createdUtc)],
      ['Modified', this._formatDate(details.modifiedUtc)],
      ['Added', this._formatDate(details.addedUtc)],
      ['Hash', hash ? hash.substring(0, 12) + '…' : '—']
    ];

    if (details.exifDateUtc || details.takenOverrideUtc) {
      props.push(['Date Taken', this._formatDate(details.takenOverrideUtc || details.exifDateUtc)]);
    }

    const table = el('dl', { className: 'details-props-list' });
    for (const [key, val] of props) {
      table.appendChild(el('dt', {}, key));
      table.appendChild(el('dd', {}, val));
    }
    this.contentEl.appendChild(table);

    // 5. Rating Stars
    const ratingRow = el('div', { className: 'details-rating-row' }, [
      el('span', { className: 'rating-label' }, 'Rating:'),
      el('span', { className: 'rating-stars' }, this._renderStars(details.rating))
    ]);
    this.contentEl.appendChild(ratingRow);

    // 6. Badges (Favourite, Hidden, Missing, Duplicate)
    const badgesRow = el('div', { className: 'details-badges-row' });
    if (details.isFavourite) badgesRow.appendChild(el('span', { className: 'badge badge-favourite' }, '♥ Favourite'));
    if (details.isHidden) badgesRow.appendChild(el('span', { className: 'badge badge-hidden' }, 'Hidden'));
    if (details.isMissing) badgesRow.appendChild(el('span', { className: 'badge badge-missing' }, 'Missing'));
    if (details.isDuplicate) badgesRow.appendChild(el('span', { className: 'badge badge-duplicate' }, 'Duplicate'));
    if (badgesRow.childNodes.length > 0) {
      this.contentEl.appendChild(badgesRow);
    }

    // 7. Tags
    if (details.tags && details.tags.length > 0) {
      const tagsSection = el('div', { className: 'details-tags-section' }, [
        el('h5', {}, 'Tags'),
        el('div', { className: 'details-tags-list' },
          details.tags.map(t => createTagChip(t, { showCount: false }))
        )
      ]);
      this.contentEl.appendChild(tagsSection);
    }

    // 8. Phase 8 placeholder note
    this.contentEl.appendChild(el('div', { className: 'details-phase8-note' }, [
      el('small', {}, 'Metadata editing arriving in Phase 8.')
    ]));
  }

  _renderMultiple(ids) {
    clear(this.contentEl);

    this.contentEl.appendChild(el('h4', { className: 'details-image-title' }, `${ids.length} images selected`));

    // Calculate aggregated stats if available from cache
    let totalBytes = 0;
    const formatCounts = new Map();
    let knownCount = 0;

    for (const id of ids) {
      const d = this.cachedDetails.get(id);
      if (d) {
        totalBytes += d.fileSizeBytes || 0;
        const fmt = (d.format || 'unknown').toUpperCase();
        formatCounts.set(fmt, (formatCounts.get(fmt) || 0) + 1);
        knownCount++;
      }
    }

    const summaryList = el('ul', { className: 'details-multi-summary' });
    summaryList.appendChild(el('li', {}, `Count: ${ids.length}`));

    if (knownCount > 0) {
      summaryList.appendChild(el('li', {}, `Total size: ${this._formatBytes(totalBytes)}`));

      const breakdown = Array.from(formatCounts.entries())
        .map(([fmt, cnt]) => `${cnt} ${fmt}`)
        .join(', ');
      summaryList.appendChild(el('li', {}, `Formats: ${breakdown}`));
    }

    this.contentEl.appendChild(summaryList);
    this.contentEl.appendChild(el('div', { className: 'details-phase8-note' }, [
      el('small', {}, 'Batch tag and metadata editing arriving in Phase 8.')
    ]));
  }

  _formatBytes(bytes) {
    if (!bytes || bytes <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i];
  }

  _formatDate(dateStr) {
    if (!dateStr) return '—';
    return dateStr.substring(0, 10);
  }

  _renderStars(rating) {
    const r = Math.max(0, Math.min(5, rating || 0));
    return '★'.repeat(r) + '☆'.repeat(5 - r);
  }
}

