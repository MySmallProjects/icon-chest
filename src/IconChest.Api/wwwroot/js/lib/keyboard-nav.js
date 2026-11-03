/**
 * Pure keyboard navigation calculation for 2D grid of items.
 */

export function navigateIndex(currentIndex, key, totalItems, columnCount, rowsPerPage = 5, isCtrl = false) {
  if (totalItems <= 0) return -1;
  if (currentIndex < 0) return 0;
  if (columnCount <= 0) columnCount = 1;

  switch (key) {
    case 'ArrowLeft':
      return Math.max(0, currentIndex - 1);

    case 'ArrowRight':
      return Math.min(totalItems - 1, currentIndex + 1);

    case 'ArrowUp':
      return Math.max(0, currentIndex - columnCount);

    case 'ArrowDown':
      return Math.min(totalItems - 1, currentIndex + columnCount);

    case 'Home':
      if (isCtrl) {
        return 0;
      }
      return Math.floor(currentIndex / columnCount) * columnCount;

    case 'End':
      if (isCtrl) {
        return totalItems - 1;
      }
      return Math.min(totalItems - 1, Math.floor(currentIndex / columnCount) * columnCount + columnCount - 1);

    case 'PageUp':
      return Math.max(0, currentIndex - rowsPerPage * columnCount);

    case 'PageDown':
      return Math.min(totalItems - 1, currentIndex + rowsPerPage * columnCount);

    default:
      return currentIndex;
  }
}

