import { createPlaceholderView } from './placeholder.js';

export function createMissingView() {
  return createPlaceholderView(
    'Missing Files',
    'Arriving in Phase 10',
    'Locate, restore or purge images whose original files can no longer be found on disk.'
  );
}

