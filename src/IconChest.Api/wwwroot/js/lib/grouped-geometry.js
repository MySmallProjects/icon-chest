/**
 * Pure arithmetic grouped grid geometry calculations for duplicate sets.
 * Never accesses the DOM or layout properties.
 */

export function buildGroupLayout(
  groupSizes,
  columnCount,
  cellWidth,
  cellHeight,
  { gap = 8, headerHeight = 36, groupGap = 16 } = {}
) {
  if (!Array.isArray(groupSizes) || columnCount <= 0) {
    return {
      groups: [],
      totalHeight: 0,
      totalItems: 0,
      totalRows: 0,
      columnCount: Math.max(1, columnCount || 1),
      cellWidth,
      cellHeight,
      gap,
      headerHeight,
      groupGap
    };
  }

  const groups = new Array(groupSizes.length);
  let currentY = 0;
  let runningItemIndex = 0;
  let totalRows = 0;

  for (let g = 0; g < groupSizes.length; g++) {
    const itemCount = Math.max(0, groupSizes[g] || 0);
    const rowCount = itemCount > 0 ? Math.ceil(itemCount / columnCount) : 0;
    const contentHeight = rowCount > 0 ? rowCount * cellHeight + (rowCount - 1) * gap : 0;
    const height = headerHeight + (rowCount > 0 ? gap + contentHeight : 0);

    groups[g] = {
      groupIndex: g,
      itemCount,
      firstItemIndex: runningItemIndex,
      rowCount,
      y: currentY,
      height
    };

    runningItemIndex += itemCount;
    totalRows += rowCount;
    currentY += height + (g < groupSizes.length - 1 ? groupGap : 0);
  }

  return {
    groups,
    totalHeight: currentY,
    totalItems: runningItemIndex,
    totalRows,
    columnCount,
    cellWidth,
    cellHeight,
    gap,
    headerHeight,
    groupGap
  };
}

export function findGroupIndexForY(layout, y) {
  const groups = layout.groups;
  if (!groups || groups.length === 0 || y < 0) return -1;
  if (y > layout.totalHeight) return -1;

  let low = 0;
  let high = groups.length - 1;

  while (low <= high) {
    const mid = (low + high) >> 1;
    const grp = groups[mid];
    const grpEnd = grp.y + grp.height + layout.groupGap;

    if (y < grp.y) {
      high = mid - 1;
    } else if (y >= grpEnd) {
      low = mid + 1;
    } else {
      return mid;
    }
  }

  return Math.min(groups.length - 1, Math.max(0, low));
}

export function findGroupIndexForItem(layout, itemIndex) {
  const groups = layout.groups;
  if (!groups || groups.length === 0 || itemIndex < 0 || itemIndex >= layout.totalItems) {
    return -1;
  }

  let low = 0;
  let high = groups.length - 1;

  while (low <= high) {
    const mid = (low + high) >> 1;
    const grp = groups[mid];

    if (itemIndex < grp.firstItemIndex) {
      high = mid - 1;
    } else if (itemIndex >= grp.firstItemIndex + grp.itemCount) {
      low = mid + 1;
    } else {
      return mid;
    }
  }

  return -1;
}

export function getItemPosition(layout, itemIndex) {
  const groupIdx = findGroupIndexForItem(layout, itemIndex);
  if (groupIdx === -1) {
    return { x: 0, y: 0, width: layout.cellWidth, height: layout.cellHeight, groupIndex: -1 };
  }

  const grp = layout.groups[groupIdx];
  const idxInGroup = itemIndex - grp.firstItemIndex;
  const col = idxInGroup % layout.columnCount;
  const row = Math.floor(idxInGroup / layout.columnCount);

  const x = col * (layout.cellWidth + layout.gap);
  const y = grp.y + layout.headerHeight + layout.gap + row * (layout.cellHeight + layout.gap);

  return {
    x,
    y,
    width: layout.cellWidth,
    height: layout.cellHeight,
    groupIndex: groupIdx,
    rowInGroup: row,
    colInGroup: col
  };
}

export function getVisibleGroupedRange(layout, scrollTop, viewportHeight, bufferRows = 2) {
  if (!layout || !layout.groups || layout.groups.length === 0 || viewportHeight <= 0) {
    return {
      startGroupIndex: 0,
      endGroupIndex: -1,
      startIndex: 0,
      endIndex: -1,
      visibleGroups: []
    };
  }

  const bufferY = bufferRows * (layout.cellHeight + layout.gap);
  const visibleTop = Math.max(0, scrollTop - bufferY);
  const visibleBottom = scrollTop + viewportHeight + bufferY;

  let startGroup = findGroupIndexForY(layout, visibleTop);
  let endGroup = findGroupIndexForY(layout, visibleBottom);

  if (startGroup === -1) startGroup = 0;
  if (endGroup === -1) endGroup = layout.groups.length - 1;

  startGroup = Math.max(0, Math.min(layout.groups.length - 1, startGroup));
  endGroup = Math.max(startGroup, Math.min(layout.groups.length - 1, endGroup));

  let globalStartIndex = -1;
  let globalEndIndex = -1;
  const visibleGroups = [];

  for (let g = startGroup; g <= endGroup; g++) {
    const grp = layout.groups[g];
    if (grp.itemCount === 0) continue;

    const grpBottom = grp.y + grp.height;
    if (grpBottom < visibleTop || grp.y > visibleBottom) continue;

    const headerVisible = grp.y + layout.headerHeight >= visibleTop && grp.y <= visibleBottom;
    const itemAreaTop = grp.y + layout.headerHeight + layout.gap;

    let startRow = 0;
    let endRow = grp.rowCount - 1;

    if (itemAreaTop < visibleBottom && grpBottom >= visibleTop && grp.rowCount > 0) {
      const rowStep = layout.cellHeight + layout.gap;
      const rawStartRow = Math.floor((visibleTop - itemAreaTop) / rowStep);
      const rawEndRow = Math.floor((visibleBottom - itemAreaTop) / rowStep);

      startRow = Math.max(0, Math.min(grp.rowCount - 1, rawStartRow));
      endRow = Math.max(0, Math.min(grp.rowCount - 1, rawEndRow));
    }

    const startItem = grp.firstItemIndex + startRow * layout.columnCount;
    const endItem = Math.min(
      grp.firstItemIndex + grp.itemCount - 1,
      grp.firstItemIndex + (endRow + 1) * layout.columnCount - 1
    );

    if (globalStartIndex === -1 || startItem < globalStartIndex) {
      globalStartIndex = startItem;
    }
    if (globalEndIndex === -1 || endItem > globalEndIndex) {
      globalEndIndex = endItem;
    }

    visibleGroups.push({
      groupIndex: g,
      headerVisible,
      y: grp.y,
      height: grp.height,
      itemCount: grp.itemCount,
      firstItemIndex: grp.firstItemIndex,
      startRow,
      endRow,
      startItemIndex: startItem,
      endItemIndex: endItem
    });
  }

  return {
    startGroupIndex: startGroup,
    endGroupIndex: endGroup,
    startIndex: globalStartIndex >= 0 ? globalStartIndex : 0,
    endIndex: globalEndIndex >= 0 ? globalEndIndex : -1,
    visibleGroups
  };
}

export function getItemIndexAtPoint(layout, x, y) {
  if (!layout || !layout.groups || layout.groups.length === 0 || x < 0 || y < 0) {
    return -1;
  }

  const groupIdx = findGroupIndexForY(layout, y);
  if (groupIdx === -1) return -1;

  const grp = layout.groups[groupIdx];
  const itemAreaTop = grp.y + layout.headerHeight + layout.gap;

  // Click is above items (in header or header gap)
  if (y < itemAreaTop) return -1;
  if (y >= grp.y + grp.height) return -1;

  const colWidth = layout.cellWidth + layout.gap;
  const rowHeight = layout.cellHeight + layout.gap;

  const col = Math.floor(x / colWidth);
  const row = Math.floor((y - itemAreaTop) / rowHeight);

  if (col >= layout.columnCount) return -1;
  if (row >= grp.rowCount) return -1;

  // Check gap boundaries
  if (x % colWidth >= layout.cellWidth) return -1;
  if ((y - itemAreaTop) % rowHeight >= layout.cellHeight) return -1;

  const idxInGroup = row * layout.columnCount + col;
  if (idxInGroup >= grp.itemCount) return -1;

  return grp.firstItemIndex + idxInGroup;
}

export function getAdjacentItemIndex(layout, currentIndex, direction) {
  if (!layout || layout.totalItems <= 0 || currentIndex < 0 || currentIndex >= layout.totalItems) {
    return currentIndex;
  }

  const groupIdx = findGroupIndexForItem(layout, currentIndex);
  if (groupIdx === -1) return currentIndex;

  const grp = layout.groups[groupIdx];
  const idxInGroup = currentIndex - grp.firstItemIndex;
  const col = idxInGroup % layout.columnCount;
  const row = Math.floor(idxInGroup / layout.columnCount);

  switch (direction) {
    case 'left':
      return Math.max(0, currentIndex - 1);

    case 'right':
      return Math.min(layout.totalItems - 1, currentIndex + 1);

    case 'up':
      if (row > 0) {
        return currentIndex - layout.columnCount;
      }
      // Move to previous group's last row
      if (groupIdx > 0) {
        const prevGroup = layout.groups[groupIdx - 1];
        if (prevGroup.itemCount > 0) {
          const prevLastRow = prevGroup.rowCount - 1;
          const targetInPrev = Math.min(prevGroup.itemCount - 1, prevLastRow * layout.columnCount + col);
          return prevGroup.firstItemIndex + targetInPrev;
        }
      }
      return currentIndex;

    case 'down':
      if (row < grp.rowCount - 1) {
        const target = currentIndex + layout.columnCount;
        if (target < grp.firstItemIndex + grp.itemCount) {
          return target;
        }
        return grp.firstItemIndex + grp.itemCount - 1;
      }
      // Move to next group's first row
      if (groupIdx < layout.groups.length - 1) {
        const nextGroup = layout.groups[groupIdx + 1];
        if (nextGroup.itemCount > 0) {
          const targetInNext = Math.min(nextGroup.itemCount - 1, col);
          return nextGroup.firstItemIndex + targetInNext;
        }
      }
      return currentIndex;

    default:
      return currentIndex;
  }
}

