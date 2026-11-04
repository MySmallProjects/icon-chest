import { api } from './api.js';
import { toast } from './toast.js';
import { dialog } from './dialog.js';
import { bridge } from './bridge.js';

export async function exportTags() {
  try {
    const data = await api.get('/api/tags/export');
    const jsonStr = JSON.stringify(data, null, 2);

    if (bridge.isAvailable()) {
      let savedPath = await bridge.saveFile(jsonStr, 'iconchest-tags.json', 'Export Tags');
      if (savedPath) {
        const baseName = savedPath.split(/[/\\]/).pop();
        if (!baseName.includes('.')) {
          savedPath += '.json';
        }
        toast.show(`Tags exported to ${savedPath}`, 'success');
        return true;
      }
      return false; // User cancelled
    }

    // Web fallback download
    const blob = new Blob([jsonStr], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'iconchest-tags.json';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);

    toast.show('Tags exported successfully', 'success');
    return true;
  } catch (err) {
    console.error('Failed to export tags:', err);
    toast.show(err.message || 'Failed to export tags', 'error');
    return false;
  }
}

export async function importTagsFromText(jsonText) {
  if (!jsonText || typeof jsonText !== 'string') {
    toast.show('Selected file is empty or invalid', 'error');
    return null;
  }

  if (new Blob([jsonText]).size > 1024 * 1024) {
    toast.show('Tag file exceeds 1 MB limit', 'error');
    return null;
  }

  let doc;
  try {
    doc = JSON.parse(jsonText);
  } catch (e) {
    toast.show('File is not valid JSON', 'error');
    return null;
  }

  try {
    const result = await api.post('/api/tags/import', doc);
    const added = result.addedCount ?? result.tagsImported ?? 0;
    const skipped = result.skippedCount ?? result.tagsSkipped ?? 0;
    const dups = result.duplicateInFileCount ?? 0;
    const invalid = result.invalidCount ?? 0;
    const total = result.tagsRead ?? (added + skipped + dups + invalid);
    const msg = `Tags imported: ${added} added, ${skipped} skipped (already existed), ${dups} duplicates in file, ${invalid} invalid (out of ${total} read).`;
    await dialog.alert({
      title: 'Tag Import Completed',
      message: msg
    });
    // Trigger tags reload
    window.dispatchEvent(new CustomEvent('iconchest-tags-changed'));
    return result;
  } catch (err) {
    console.error('Tag import error:', err);
    toast.show(err.message || 'Failed to import tags', 'error');
    return null;
  }
}

export function promptImportTags() {
  return new Promise((resolve) => {
    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.accept = '.json,application/json';
    fileInput.style.display = 'none';

    fileInput.addEventListener('change', async () => {
      const file = fileInput.files?.[0];
      if (!file) {
        resolve(null);
        return;
      }

      if (file.size > 1024 * 1024) {
        toast.show('Selected file exceeds maximum allowed size of 1 MB', 'error');
        resolve(null);
        return;
      }

      try {
        const text = await file.text();
        const res = await importTagsFromText(text);
        resolve(res);
      } catch (err) {
        console.error('Error reading file:', err);
        toast.show('Failed to read file', 'error');
        resolve(null);
      } finally {
        if (fileInput.parentNode) {
          document.body.removeChild(fileInput);
        }
      }
    });

    document.body.appendChild(fileInput);
    fileInput.click();
  });
}

