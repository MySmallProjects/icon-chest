import { el, clear, createSvg } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { store } from '../lib/store.js';
import { toast } from '../lib/toast.js';
import { dialog } from '../lib/dialog.js';
import { createTagChip } from './tag-chip.js';
import { tagsStore } from '../lib/tags-store.js';
import { filterAndRankTags, evaluateCreateOption } from '../lib/tag-combobox-model.js';
import { EditModel } from '../lib/edit-model.js';

const HEART_SVG = '<svg viewBox="0 0 24 24" width="18" height="18"><path fill="currentColor" d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>';

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
    this._previousSelectedIds = [];
    this.backgroundMode = options.backgroundMode || 'checker';
    this.cachedDetails = new Map(); // id -> details
    this.onRestoreSelection = options.onRestoreSelection || null;
    this.onSelectImage = options.onSelectImage || null;
    this.onSaveSuccess = options.onSaveSuccess || null;
    this.onFavouriteToggled = options.onFavouriteToggled || null;
    this.onVisibilityChange = options.onVisibilityChange || null;
    this.onTransitionEnd = options.onTransitionEnd || null;

    this.editModel = null;
    this._currentData = null; // raw details or summary
    this._tagsUnsubscribe = null;
    this._isRestoringSelection = false;

    this._buildStructure();
    this._initTagsListener();
  }

  _initTagsListener() {
    tagsStore.init();
    this._tagsUnsubscribe = tagsStore.subscribe(() => {
      if (this.isOpen && typeof this._refreshTagChips === 'function') {
        this._refreshTagChips();
      }
    });
  }

  _buildStructure() {
    clear(this.containerEl);

    // Header
    this.titleTextEl = el('span', { className: 'panel-details-title-text' }, 'Details');
    this.dirtyDotEl = el('span', { className: 'details-dirty-dot', style: { display: 'none' } });

    this.headerEl = el('div', { className: 'panel-details-header' }, [
      el('h3', { className: 'panel-details-title' }, [
        this.titleTextEl,
        this.dirtyDotEl
      ]),
      el('button', {
        type: 'button',
        className: 'panel-details-close-btn',
        'aria-label': 'Close details panel',
        title: 'Close details'
      }, [
        el('svg', { viewBox: '0 0 24 24', width: '18', height: '18' }, [
          el('path', { fill: 'currentColor', d: 'M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z' })
        ])
      ])
    ]);

    this.headerEl.querySelector('.panel-details-close-btn').addEventListener('click', () => {
      this.closeByUser();
    });

    // Content scroll body
    this.contentEl = el('div', { className: 'panel-details-content' });

    // Sticky Footer
    this.revertBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm details-revert-btn',
      disabled: true,
      onClick: () => this.revert()
    }, 'Revert');

    this.saveBtn = el('button', {
      type: 'button',
      className: 'btn btn-sm btn-primary details-save-btn',
      disabled: true,
      onClick: () => this.save()
    }, 'Save');

    this.footerEl = el('div', { className: 'panel-details-footer' }, [
      this.revertBtn,
      this.saveBtn
    ]);

    this.containerEl.appendChild(this.headerEl);
    this.containerEl.appendChild(this.contentEl);
    this.containerEl.appendChild(this.footerEl);

    // Initial state: hidden
    this.containerEl.classList.add('collapsed');
    this.containerEl.classList.add('not-animated');
    this.containerEl.setAttribute('aria-hidden', 'true');
    this.containerEl.setAttribute('inert', '');
    setTimeout(() => {
      this.containerEl.classList.remove('not-animated');
    }, 100);

    this.containerEl.addEventListener('transitionend', (e) => {
      if (e.propertyName === 'margin-right' && typeof this.onTransitionEnd === 'function') {
        this.onTransitionEnd();
      }
    });

    // Ctrl+S / Cmd+S handler
    this.containerEl.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        e.stopPropagation();
        if (this.isDirty()) {
          this.save();
        }
      }
    });
  }

  isDirty() {
    return Boolean(this.editModel && this.editModel.isDirty());
  }

  _updateDirtyState() {
    const dirty = this.isDirty();
    this.dirtyDotEl.style.display = dirty ? 'inline-block' : 'none';
    this.revertBtn.disabled = !dirty;
    this.saveBtn.disabled = !dirty;
  }

  setBackgroundMode(mode) {
    this.backgroundMode = mode;
    const thumbArea = this.contentEl.querySelector('.details-thumb-area');
    if (thumbArea) {
      thumbArea.className = `details-thumb-area bg-${mode}`;
    }
  }

  updateFavourite(id, isFav) {
    const numId = Number(id);
    if (this._currentData && (Number(this._currentData.id) === numId || (this.selectedIds.length === 1 && Number(this.selectedIds[0]) === numId))) {
      this._currentData.isFavourite = isFav;
      this._currentData.is_favourite = isFav;
      const favBtn = this.contentEl.querySelector('.details-fav-btn');
      if (favBtn) {
        favBtn.className = `details-fav-btn ${isFav ? 'active' : ''}`;
        favBtn.style.color = isFav ? '#ef4444' : '#ffffff';
        favBtn.title = isFav ? 'Remove from favourites' : 'Add to favourites';
      }
    }
  }

  async updateSelection(ids) {
    if (this._isRestoringSelection) return;
    const newIds = ids || [];

    const isSame = newIds.length === this.selectedIds.length &&
      newIds.every((id, idx) => id === this.selectedIds[idx]);
    if (isSame) return;

    // Check unsaved changes guard
    if (this.isDirty()) {
      const action = await dialog.confirmThree({
        title: 'Unsaved Changes',
        message: 'You have unsaved changes. Do you want to save them before changing selection?',
        saveText: 'Save',
        discardText: 'Discard',
        cancelText: 'Cancel'
      });

      if (action === 'save') {
        const success = await this.save();
        if (!success) {
          this._abortSelectionChange();
          return;
        }
      } else if (action === 'discard') {
        this.revert();
      } else {
        // Cancel
        this._abortSelectionChange();
        return;
      }
    }

    this._previousSelectedIds = [...this.selectedIds];
    this.selectedIds = newIds;

    if (this.selectedIds.length === 0) {
      this._slideOut();
      this.editModel = null;
      this._updateDirtyState();
      return;
    }

    if (!this.userClosed) {
      this._slideIn();
    }

    this._renderContent();
  }

  _abortSelectionChange() {
    if (this.onRestoreSelection && this.selectedIds) {
      this._isRestoringSelection = true;
      try {
        this.onRestoreSelection([...this.selectedIds]);
      } finally {
        this._isRestoringSelection = false;
      }
    }
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
    this._renderContent();
  }

  async closeByUser() {
    if (this.isDirty()) {
      const action = await dialog.confirmThree({
        title: 'Unsaved Changes',
        message: 'You have unsaved changes. Do you want to save them before closing the details drawer?',
        saveText: 'Save',
        discardText: 'Discard',
        cancelText: 'Cancel'
      });

      if (action === 'save') {
        const success = await this.save();
        if (!success) return;
      } else if (action === 'discard') {
        this.revert();
      } else {
        return;
      }
    }

    this.userClosed = true;
    this._slideOut();
  }

  _slideIn() {
    this.isOpen = true;
    this.containerEl.removeAttribute('aria-hidden');
    this.containerEl.removeAttribute('inert');
    this.containerEl.classList.remove('collapsed');
    if (typeof this.onVisibilityChange === 'function') {
      this.onVisibilityChange(true);
    }
  }

  _slideOut() {
    this.isOpen = false;
    this.containerEl.setAttribute('aria-hidden', 'true');
    this.containerEl.setAttribute('inert', '');
    this.containerEl.classList.add('collapsed');
    if (typeof this.onVisibilityChange === 'function') {
      this.onVisibilityChange(false);
    }
  }

  revert() {
    if (this.editModel) {
      this.editModel.revert();
      this._renderContent();
    }
  }

  async save() {
    if (!this.editModel || !this.isDirty()) return true;

    const validation = this.editModel.validate();
    if (!validation.valid) {
      const firstErr = Object.values(validation.errors)[0];
      toast.error(firstErr);
      return false;
    }

    try {
      const patchBody = this.editModel.buildPatchBody();
      await api.patch('/api/images', patchBody);

      toast.success('Changes saved');

      // Clear cached details for modified ids
      for (const id of this.selectedIds) {
        this.cachedDetails.delete(id);
      }

      if (typeof this.onSaveSuccess === 'function') {
        this.onSaveSuccess();
      }

      // Re-fetch current selection to reset clean edit model
      await this._renderContent();
      return true;
    } catch (err) {
      console.error('Failed to save metadata edits:', err);
      toast.error(`Save failed: ${err.message || err}`);
      return false;
    }
  }

  async _renderContent() {
    clear(this.contentEl);

    if (this.selectedIds.length === 0) {
      this.titleTextEl.textContent = 'Details';
      this.contentEl.appendChild(el('p', { className: 'details-empty' }, 'No images selected.'));
      this.editModel = null;
      this._updateDirtyState();
      return;
    }

    if (this.selectedIds.length === 1) {
      await this._renderSingle(this.selectedIds[0]);
    } else {
      await this._renderMultiple(this.selectedIds);
    }
  }

  async _renderSingle(id) {
    this.titleTextEl.textContent = '1 image selected';
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

    this._currentData = details;
    this.editModel = new EditModel(details, [id]);
    this._updateDirtyState();

    const fileName = details.fileName || details.name || 'Untitled';

    // 1. Thumbnail Area with Favourite Heart Button
    const isFav = Boolean(details.isFavourite ?? details.is_favourite);
    const favBtn = el('button', {
      type: 'button',
      className: `details-fav-btn ${isFav ? 'active' : ''}`,
      title: isFav ? 'Remove from favourites' : 'Add to favourites',
      'aria-label': 'Favourite toggle',
      style: {
        position: 'absolute',
        top: '8px',
        right: '8px',
        background: 'rgba(0,0,0,0.5)',
        border: 'none',
        borderRadius: '50%',
        padding: '6px',
        cursor: 'pointer',
        color: isFav ? '#ef4444' : '#ffffff',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      },
      onClick: async () => {
        const cur = Boolean(details.isFavourite ?? details.is_favourite);
        const next = !cur;
        try {
          await api.patch('/api/images', {
            ids: [Number(id)],
            isFavourite: next,
            favourite: next
          });
          details.isFavourite = next;
          details.is_favourite = next;
          favBtn.className = `details-fav-btn ${next ? 'active' : ''}`;
          favBtn.style.color = next ? '#ef4444' : '#ffffff';
          favBtn.title = next ? 'Remove from favourites' : 'Add to favourites';
          toast.info(next ? 'Added to favourites' : 'Removed from favourites');
          if (typeof this.onFavouriteToggled === 'function') {
            this.onFavouriteToggled(Number(id), next);
          }
        } catch (err) {
          toast.error('Failed to update favourite status');
        }
      }
    }, [createSvg(HEART_SVG)]);

    const thumbImg = el('img', {
      className: 'details-thumb-img',
      src: `/api/images/${id}/thumbnail?size=256`,
      alt: fileName
    });

    let thumbAttempt = 0;
    thumbImg.onerror = () => {
      if (thumbAttempt === 0) {
        thumbAttempt++;
        // Fallback to size 128 (already generated by grid)
        thumbImg.src = `/api/images/${id}/thumbnail?size=128`;
        return;
      }
      if (thumbAttempt === 1) {
        thumbAttempt++;
        // Fallback to size 64
        thumbImg.src = `/api/images/${id}/thumbnail?size=64`;
        return;
      }
      if (thumbAttempt === 2) {
        thumbAttempt++;
        // Fallback to original image file directly
        thumbImg.src = `/api/images/${id}/file`;
        return;
      }
    };

    const thumbArea = el('div', {
      className: `details-thumb-area bg-${this.backgroundMode}`,
      style: { position: 'relative' }
    }, [
      thumbImg,
      favBtn
    ]);
    this.contentEl.appendChild(thumbArea);

    // 2. Title Field with "Reset to file name" button
    const titleVal = this.editModel.getField('title').value;
    const titleInput = el('input', {
      type: 'text',
      className: 'input text-input full-width',
      value: titleVal,
      placeholder: fileName,
      'aria-label': 'Image title',
      onInput: (e) => {
        this.editModel.setTitle(e.target.value);
        this._updateDirtyState();
      }
    });

    const resetTitleBtn = el('button', {
      type: 'button',
      className: 'btn btn-xs',
      title: 'Reset title to original filename',
      onClick: () => {
        titleInput.value = '';
        this.editModel.setTitle('');
        this._updateDirtyState();
      }
    }, 'Reset');

    const titleRow = el('div', { className: 'details-field-row' }, [
      el('div', { style: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' } }, [
        el('label', { className: 'details-field-label' }, 'Title'),
        resetTitleBtn
      ]),
      titleInput
    ]);
    this.contentEl.appendChild(titleRow);

    // 3. Description Field
    const descVal = this.editModel.getField('description').value || '';
    const descInput = el('textarea', {
      className: 'input text-input full-width',
      rows: '3',
      placeholder: 'Add description...',
      'aria-label': 'Image description',
      value: descVal,
      onInput: (e) => {
        this.editModel.setDescription(e.target.value);
        this._updateDirtyState();
      }
    });
    descInput.value = descVal;
    const descRow = el('div', { className: 'details-field-row' }, [
      el('label', { className: 'details-field-label' }, 'Description'),
      descInput
    ]);
    this.contentEl.appendChild(descRow);

    // 4. Tags Section with Combobox
    this._renderTagsSection();

    // 5. Rating Picker (0 to 5 stars)
    this._renderRatingPicker();

    // 6. Taken Date Field with Quick Fill Buttons
    this._renderDateField(details);

    // 7. Hidden Checkbox
    this._renderHiddenCheckbox();

    // 8. Read-only Section (File details, Path copy, Open button, EXIF, Copies)
    await this._renderReadOnlySection(details, id);
  }

  async _renderMultiple(ids) {
    this.titleTextEl.textContent = `${ids.length} images selected`;
    this.contentEl.appendChild(el('div', { className: 'details-skeleton' }, 'Loading summary...'));

    let summary;
    try {
      summary = await api.post('/api/images/summary', { ids });
    } catch (err) {
      console.error('Failed to load summary for selection', err);
      clear(this.contentEl);
      this.contentEl.appendChild(el('div', { className: 'details-error' }, 'Failed to load summary'));
      return;
    }

    if (this.selectedIds.length !== ids.length) return;
    clear(this.contentEl);

    this._currentData = summary;
    this.editModel = new EditModel(summary, ids);
    this._updateDirtyState();

    // Multi-edit summary stats card
    const summaryCard = el('div', {
      className: 'card',
      style: { padding: '10px 14px', marginBottom: '8px', fontSize: '0.85rem' }
    }, [
      el('div', {}, `Count: ${summary.count.toLocaleString()} images`),
      el('div', {}, `Total size: ${this._formatBytes(summary.totalSizeBytes)}`),
      el('div', {}, `Formats: ${Object.entries(summary.formats || {}).map(([f, c]) => `${c} ${f}`).join(', ') || '—'}`),
      el('div', {}, `Dimensions: ${summary.minWidth || 0}×${summary.minHeight || 0} to ${summary.maxWidth || 0}×${summary.maxHeight || 0}`)
    ]);
    this.contentEl.appendChild(summaryCard);

    // Title Field (Multiple values placeholder)
    const titleField = this.editModel.getField('title');
    const titleInput = el('input', {
      type: 'text',
      className: 'input text-input full-width',
      placeholder: titleField.isMultiple ? 'Multiple values' : 'Title',
      value: titleField.isMultiple ? '' : titleField.value,
      'aria-label': 'Batch title',
      onInput: (e) => {
        this.editModel.setTitle(e.target.value);
        this._updateDirtyState();
      }
    });
    this.contentEl.appendChild(el('div', { className: 'details-field-row' }, [
      el('label', { className: 'details-field-label' }, 'Title'),
      titleInput
    ]));

    // Description Field (Multiple values placeholder)
    const descField = this.editModel.getField('description');
    const descInput = el('textarea', {
      className: 'input text-input full-width',
      rows: '3',
      placeholder: descField.isMultiple ? 'Multiple values' : 'Add description...',
      value: descField.isMultiple ? '' : descField.value,
      'aria-label': 'Batch description',
      onInput: (e) => {
        this.editModel.setDescription(e.target.value);
        this._updateDirtyState();
      }
    });
    descInput.value = descField.isMultiple ? '' : (descField.value || '');
    this.contentEl.appendChild(el('div', { className: 'details-field-row' }, [
      el('label', { className: 'details-field-label' }, 'Description'),
      descInput
    ]));

    // Tags Section
    this._renderTagsSection();

    // Rating Picker
    this._renderRatingPicker();

    // Taken Date Field
    this._renderDateField(summary);

    // Hidden Checkbox (Tri-state support)
    this._renderHiddenCheckbox();
  }

  _renderTagsSection() {
    const tagsWrapper = el('div', { className: 'details-field-row' });
    const tagsHeader = el('label', { className: 'details-field-label' }, 'Tags');
    tagsWrapper.appendChild(tagsHeader);

    // Active Tag Chips Container
    const chipsContainer = el('div', {
      className: 'details-tags-list',
      style: { display: 'flex', flexWrap: 'wrap', gap: '6px', marginBottom: '8px' }
    });

    const refreshChips = () => {
      clear(chipsContainer);
      if (!this.editModel) return;
      const allTags = this.editModel.getAllTags();

      for (const t of allTags) {
        if (t.current === 0 && t.initial === 0) continue; // Unused and untouched

        const storeTag = tagsStore.getTagById(t.tagId);
        if (storeTag) {
          t.name = storeTag.name;
          t.color = storeTag.color || storeTag.bgColor;
          t.bgColor = t.color;
        } else if (t.initial > 0 && tagsStore.getTags().length > 0) {
          // Tag was deleted globally
          t.current = 0;
          continue;
        }

        let triState;
        if (t.current === 1) triState = 'all';
        else if (t.current === 0.5) triState = 'some';
        else triState = 'none';

        const chip = createTagChip(t, {
          triState,
          showCount: false,
          onClick: () => {
            this.editModel.cycleTag(t.tagId);
            this._updateDirtyState();
            refreshChips();
          }
        });
        chipsContainer.appendChild(chip);
      }

      if (chipsContainer.childNodes.length === 0) {
        chipsContainer.appendChild(el('span', { style: { color: 'var(--text-muted)', fontSize: '0.8rem' } }, 'No tags attached'));
      }
    };

    this._refreshTagChips = refreshChips;
    refreshChips();
    tagsWrapper.appendChild(chipsContainer);

    // Combobox Input for Adding/Creating Tags
    const comboboxContainer = el('div', { className: 'tags-combobox-wrapper' });
    const tagInput = el('input', {
      type: 'text',
      className: 'input text-input full-width',
      placeholder: 'Add or create tag...',
      'aria-label': 'Add tag combobox'
    });

    const dropdown = el('div', {
      className: 'tags-combobox-dropdown',
      style: { display: 'none' }
    });

    let activeIndex = -1;
    let currentOptions = [];

    const closeDropdown = () => {
      dropdown.style.display = 'none';
      clear(dropdown);
      activeIndex = -1;
      currentOptions = [];
    };

    const updateDropdown = () => {
      const q = tagInput.value;
      const allStoreTags = tagsStore.getTags();
      const ranked = filterAndRankTags(allStoreTags, q);
      const createOpt = evaluateCreateOption(allStoreTags, q);

      clear(dropdown);
      currentOptions = [];

      for (const tag of ranked.slice(0, 8)) {
        currentOptions.push({ type: 'existing', tag });
      }

      if (createOpt.canCreate) {
        currentOptions.push({ type: 'create', name: createOpt.name });
      }

      if (currentOptions.length === 0) {
        closeDropdown();
        return;
      }

      for (let i = 0; i < currentOptions.length; i++) {
        const opt = currentOptions[i];
        let itemEl;

        if (opt.type === 'existing') {
          itemEl = el('button', {
            type: 'button',
            className: `tags-combobox-item${i === activeIndex ? ' focused' : ''}`,
            onClick: () => {
              this.editModel.addTag(opt.tag.id, opt.tag);
              this._updateDirtyState();
              refreshChips();
              tagInput.value = '';
              closeDropdown();
              tagInput.focus();
            }
          }, opt.tag.name);
        } else {
          itemEl = el('button', {
            type: 'button',
            className: `tags-combobox-item tags-combobox-create${i === activeIndex ? ' focused' : ''}`,
            onClick: async () => {
              try {
                const newTag = await tagsStore.createTag(opt.name);
                this.editModel.addTag(newTag.id, newTag);
                this._updateDirtyState();
                refreshChips();
                tagInput.value = '';
                closeDropdown();
                tagInput.focus();
                toast.success(`Created tag "${newTag.name}"`);
              } catch (err) {
                toast.error(`Failed to create tag: ${err.message || err}`);
              }
            }
          }, `+ Create "${opt.name}"`);
        }

        dropdown.appendChild(itemEl);
      }

      dropdown.style.display = 'flex';
    };

    tagInput.addEventListener('input', () => {
      activeIndex = -1;
      updateDropdown();
    });

    tagInput.addEventListener('focus', () => {
      updateDropdown();
    });

    tagInput.addEventListener('keydown', (e) => {
      if (dropdown.style.display !== 'none' && currentOptions.length > 0) {
        if (e.key === 'ArrowDown') {
          e.preventDefault();
          activeIndex = (activeIndex + 1) % currentOptions.length;
          updateDropdown();
          return;
        } else if (e.key === 'ArrowUp') {
          e.preventDefault();
          activeIndex = (activeIndex - 1 + currentOptions.length) % currentOptions.length;
          updateDropdown();
          return;
        } else if (e.key === 'Enter') {
          e.preventDefault();
          if (activeIndex >= 0 && activeIndex < currentOptions.length) {
            dropdown.children[activeIndex].click();
          } else if (currentOptions.length > 0) {
            dropdown.children[0].click();
          }
          return;
        } else if (e.key === 'Escape') {
          e.preventDefault();
          closeDropdown();
          return;
        }
      }
    });

    document.addEventListener('pointerdown', (e) => {
      if (!comboboxContainer.contains(e.target)) {
        closeDropdown();
      }
    });

    comboboxContainer.appendChild(tagInput);
    comboboxContainer.appendChild(dropdown);
    tagsWrapper.appendChild(comboboxContainer);

    this.contentEl.appendChild(tagsWrapper);
  }

  _renderRatingPicker() {
    const ratingField = this.editModel.getField('rating');
    let currentRating = ratingField.isMultiple ? null : (ratingField.value || 0);

    const pickerContainer = el('div', {
      className: 'rating-picker',
      role: 'radiogroup',
      'aria-label': 'Image rating'
    });

    const starButtons = [];

    const updateStars = (val) => {
      for (let i = 0; i < 5; i++) {
        const star = starButtons[i];
        if (val != null && i < val) {
          star.classList.add('active');
          star.textContent = '★';
        } else {
          star.classList.remove('active');
          star.textContent = '☆';
        }
      }
    };

    for (let i = 1; i <= 5; i++) {
      const star = el('button', {
        type: 'button',
        className: 'star-btn',
        role: 'radio',
        'aria-label': `${i} ${i === 1 ? 'star' : 'stars'}`,
        'aria-checked': currentRating === i ? 'true' : 'false',
        tabindex: currentRating === i || (currentRating === 0 && i === 1) ? '0' : '-1',
        onClick: () => {
          currentRating = i;
          this.editModel.setRating(i);
          this._updateDirtyState();
          updateStars(i);
        }
      }, '☆');

      starButtons.push(star);
      pickerContainer.appendChild(star);
    }

    updateStars(currentRating);

    const clearRatingBtn = el('button', {
      type: 'button',
      className: 'btn btn-xs',
      style: { marginLeft: '8px' },
      onClick: () => {
        currentRating = 0;
        this.editModel.setRating(0);
        this._updateDirtyState();
        updateStars(0);
      }
    }, 'Clear');

    const ratingRow = el('div', { className: 'details-field-row' }, [
      el('label', { className: 'details-field-label' }, 'Rating'),
      el('div', { style: { display: 'flex', alignItems: 'center' } }, [
        pickerContainer,
        clearRatingBtn
      ])
    ]);

    this.contentEl.appendChild(ratingRow);
  }

  _renderDateField(data) {
    const dateField = this.editModel.getField('takenOverrideDate');
    const dateInput = el('input', {
      type: 'text',
      className: 'input text-input full-width',
      placeholder: dateField.isMultiple ? 'Multiple values' : 'YYYY-MM-DD',
      value: dateField.isMultiple ? '' : (dateField.value || ''),
      'aria-label': 'Date taken',
      onInput: (e) => {
        this.editModel.setTakenOverrideDate(e.target.value);
        this._updateDirtyState();
      }
    });

    const exifDate = data.exifDateUtc ? data.exifDateUtc.substring(0, 10) : null;
    const createdDate = data.createdUtc ? data.createdUtc.substring(0, 10) : null;

    const useExifBtn = el('button', {
      type: 'button',
      className: 'date-quick-fill-btn',
      disabled: !exifDate,
      onClick: () => {
        if (exifDate) {
          dateInput.value = exifDate;
          this.editModel.setTakenOverrideDate(exifDate);
          this._updateDirtyState();
        }
      }
    }, 'Use EXIF date');

    const useCreatedBtn = el('button', {
      type: 'button',
      className: 'date-quick-fill-btn',
      disabled: !createdDate,
      onClick: () => {
        if (createdDate) {
          dateInput.value = createdDate;
          this.editModel.setTakenOverrideDate(createdDate);
          this._updateDirtyState();
        }
      }
    }, 'Use created date');

    const clearDateBtn = el('button', {
      type: 'button',
      className: 'date-quick-fill-btn',
      onClick: () => {
        dateInput.value = '';
        this.editModel.setTakenOverrideDate(null);
        this._updateDirtyState();
      }
    }, 'Clear');

    const dateRow = el('div', { className: 'details-field-row' }, [
      el('label', { className: 'details-field-label' }, 'Date Taken'),
      dateInput,
      el('div', { className: 'date-quick-fill-row' }, [
        useExifBtn,
        useCreatedBtn,
        clearDateBtn
      ])
    ]);

    this.contentEl.appendChild(dateRow);
  }

  _renderHiddenCheckbox() {
    const hiddenField = this.editModel.getField('isHidden');
    const checkbox = el('input', {
      type: 'checkbox',
      id: 'details-checkbox-hidden',
      checked: Boolean(hiddenField.value),
      onChange: (e) => {
        this.editModel.setIsHidden(e.target.checked);
        this._updateDirtyState();
      }
    });

    if (hiddenField.isMultiple) {
      checkbox.indeterminate = true;
    }

    const hiddenRow = el('div', {
      style: { display: 'flex', alignItems: 'center', gap: '8px', margin: '8px 0' }
    }, [
      checkbox,
      el('label', { htmlFor: 'details-checkbox-hidden', style: { cursor: 'pointer', fontSize: '0.85rem' } }, 'Hide from library')
    ]);

    this.contentEl.appendChild(hiddenRow);
  }

  async _renderReadOnlySection(details, id) {
    const sysInfo = store.get('systemInfo') || {};
    const isLocal = Boolean(sysInfo.isLocal);

    // Path & Open button
    const filePath = details.path || details.directory || '';
    const openBtn = el('button', {
      type: 'button',
      className: 'btn btn-xs btn-primary',
      disabled: !isLocal || !filePath,
      title: isLocal ? 'Open in default system application' : 'Only available locally',
      onClick: async () => {
        try {
          await api.post('/api/native/open-file', { imageId: id });
        } catch (err) {
          toast.error(`Could not open file: ${err.message || err}`);
        }
      }
    }, 'Open');

    const copyBtn = el('button', {
      type: 'button',
      className: 'btn btn-xs',
      title: 'Copy full path',
      onClick: async () => {
        try {
          await navigator.clipboard.writeText(filePath);
          toast.info('Path copied to clipboard');
        } catch {
          toast.error('Failed to copy path');
        }
      }
    }, 'Copy');

    const pathRow = el('div', { className: 'details-field-row' }, [
      el('div', { style: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' } }, [
        el('label', { className: 'details-field-label' }, 'Path'),
        el('div', { style: { display: 'flex', gap: '4px' } }, [copyBtn, openBtn])
      ]),
      el('div', {
        className: 'details-field-value',
        style: { fontFamily: 'monospace', fontSize: '0.75rem', wordBreak: 'break-all' }
      }, filePath)
    ]);
    this.contentEl.appendChild(pathRow);

    // Properties List
    const hash = details.hash || details.contentHash;
    const props = [
      ['Format', (details.format || '').toUpperCase()],
      ['Dimensions', (details.width && details.height) ? `${details.width} × ${details.height}` : 'Vector / Unknown'],
      ['File Size', this._formatBytes(details.sizeBytes ?? details.fileSizeBytes)],
      ['Effective Date', this._formatDate(details.effectiveDateUtc)],
      ['Effective Source', details.effectiveDateSource || '—'],
      ['Created', this._formatDate(details.createdUtc)],
      ['Modified', this._formatDate(details.modifiedUtc)],
      ['Added', this._formatDate(details.addedUtc)],
      ['Hash', hash ? hash.substring(0, 12) + '…' : '—']
    ];

    const table = el('dl', { className: 'details-props-list', style: { margin: '8px 0', fontSize: '0.8rem' } });
    for (const [key, val] of props) {
      table.appendChild(el('dt', { style: { color: 'var(--text-muted)' } }, key));
      table.appendChild(el('dd', { style: { marginBottom: '4px', color: 'var(--text-primary)' } }, String(val)));
    }
    this.contentEl.appendChild(table);

    // Collapsible Camera / EXIF Data
    if (details.exif || details.cameraMake || details.cameraModel || details.focalLength || details.aperture) {
      const exifGrid = el('div', { className: 'details-exif-grid' });
      const exifItems = [
        ['Make', details.cameraMake],
        ['Model', details.cameraModel],
        ['Lens', details.lensModel],
        ['Focal Length', details.focalLength ? `${details.focalLength} mm` : null],
        ['Aperture', details.aperture ? `f/${details.aperture}` : null],
        ['Shutter', details.shutterSpeed],
        ['ISO', details.iso ? String(details.iso) : null]
      ];
      for (const [k, v] of exifItems) {
        if (v) {
          exifGrid.appendChild(el('span', { style: { color: 'var(--text-muted)' } }, k));
          exifGrid.appendChild(el('span', {}, String(v)));
        }
      }
      if (exifGrid.childNodes.length > 0) {
        const exifDrawer = el('details', { className: 'details-exif-drawer' }, [
          el('summary', {}, 'Camera & EXIF Data'),
          exifGrid
        ]);
        this.contentEl.appendChild(exifDrawer);
      }
    }

    // Copies List (Duplicates)
    try {
      const duplicates = await api.get(`/api/images/${id}/duplicates`);
      if (Array.isArray(duplicates) && duplicates.length > 0) {
        const dupList = el('div', { className: 'details-duplicates-list' });
        for (const dup of duplicates) {
          const btn = el('button', {
            type: 'button',
            className: 'duplicate-copy-btn',
            title: dup.path,
            onClick: () => {
              if (typeof this.onSelectImage === 'function') {
                this.onSelectImage(dup.id);
              }
            }
          }, [
            el('span', { className: 'truncate', style: { maxWidth: '200px' } }, dup.path.split(/[\\/]/).pop()),
            el('span', { style: { fontSize: '0.75rem', color: 'var(--text-muted)' } }, this._formatBytes(dup.size))
          ]);
          dupList.appendChild(btn);
        }

        const copiesSection = el('div', { className: 'details-field-row', style: { marginTop: '8px' } }, [
          el('label', { className: 'details-field-label' }, `Duplicate Copies (${duplicates.length})`),
          dupList
        ]);
        this.contentEl.appendChild(copiesSection);
      }
    } catch (err) {
      // Duplicates lookup optional
    }
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
}
