/**
 * Pure arithmetic grid geometry calculations.
 * Never accesses the DOM or layout properties.
 */

export function calculateColumnCount(containerWidth, cellSize, gap = 8) {
  if (containerWidth <= 0 || cellSize <= 0) return 1;
  const count = Math.floor((containerWidth + gap) / (cellSize + gap));
  return Math.max(1, count);
}

export function calculateRowCount(totalItems, columnCount) {
  if (totalItems <= 0 || columnCount <= 0) return 0;
  return Math.ceil(totalItems / columnCount);
}

export function calculateTotalHeight(totalItems, columnCount, cellWidth, cellHeight, gap = 8) {
  const rows = calculateRowCount(totalItems, columnCount);
  if (rows <= 0) return 0;
  return rows * (cellHeight + gap) - gap;
}

export function getCellRect(index, columnCount, cellWidth, cellHeight, gap = 8) {
  if (index < 0 || columnCount <= 0) {
    return { x: 0, y: 0, width: cellWidth, height: cellHeight };
  }
  const col = index % columnCount;
  const row = Math.floor(index / columnCount);
  const x = col * (cellWidth + gap);
  const y = row * (cellHeight + gap);
  return { x, y, width: cellWidth, height: cellHeight };
}

export function getIndexAtPoint(x, y, columnCount, totalItems, cellWidth, cellHeight, gap = 8) {
  if (totalItems <= 0 || columnCount <= 0 || x < 0 || y < 0) return -1;

  const colWidth = cellWidth + gap;
  const rowHeight = cellHeight + gap;

  const col = Math.floor(x / colWidth);
  const row = Math.floor(y / rowHeight);

  if (col >= columnCount) return -1;

  // Check if point falls inside gap
  const inColGap = (x % colWidth) >= cellWidth;
  const inRowGap = (y % rowHeight) >= cellHeight;
  if (inColGap || inRowGap) return -1;

  const index = row * columnCount + col;
  if (index >= totalItems) return -1;

  return index;
}

export function getVisibleRange(scrollTop, viewportHeight, totalItems, columnCount, cellHeight, gap = 8, bufferRows = 3) {
  if (totalItems <= 0 || columnCount <= 0 || viewportHeight <= 0) {
    return { startRow: 0, endRow: -1, startIndex: 0, endIndex: -1 };
  }

  const rowStep = cellHeight + gap;
  const totalRows = calculateRowCount(totalItems, columnCount);

  const rawStartRow = Math.floor(Math.max(0, scrollTop) / rowStep);
  const rawEndRow = Math.floor(Math.max(0, scrollTop + viewportHeight - 1) / rowStep);

  const startRow = Math.max(0, rawStartRow - bufferRows);
  const endRow = Math.min(totalRows - 1, rawEndRow + bufferRows);

  if (startRow > endRow) {
    return { startRow: 0, endRow: -1, startIndex: 0, endIndex: -1 };
  }

  const startIndex = startRow * columnCount;
  const endIndex = Math.min(totalItems - 1, (endRow + 1) * columnCount - 1);

  return { startRow, endRow, startIndex, endIndex };
}

export function getAnchorScrollTop(anchorIndex, oldColCount, newColCount, oldCellHeight, newCellHeight, gap = 8) {
  if (anchorIndex < 0 || newColCount <= 0) return 0;
  const newRow = Math.floor(anchorIndex / newColCount);
  return newRow * (newCellHeight + gap);
}

export function getMarqueeHits(marqueeRect, totalItems, columnCount, cellWidth, cellHeight, gap = 8) {
  const hits = [];
  if (totalItems <= 0 || columnCount <= 0) return hits;

  const mLeft = Math.min(marqueeRect.x, marqueeRect.x + marqueeRect.width);
  const mRight = Math.max(marqueeRect.x, marqueeRect.x + marqueeRect.width);
  const mTop = Math.min(marqueeRect.y, marqueeRect.y + marqueeRect.height);
  const mBottom = Math.max(marqueeRect.y, marqueeRect.y + marqueeRect.height);

  const colStep = cellWidth + gap;
  const rowStep = cellHeight + gap;

  // Bounding box of touched rows and columns
  const startCol = Math.max(0, Math.floor(mLeft / colStep));
  const endCol = Math.min(columnCount - 1, Math.floor(mRight / colStep));
  const startRow = Math.max(0, Math.floor(mTop / rowStep));
  const totalRows = calculateRowCount(totalItems, columnCount);
  const endRow = Math.min(totalRows - 1, Math.floor(mBottom / rowStep));

  for (let r = startRow; r <= endRow; r++) {
    for (let c = startCol; c <= endCol; c++) {
      const idx = r * columnCount + c;
      if (idx >= totalItems) break;

      const cellLeft = c * colStep;
      const cellRight = cellLeft + cellWidth;
      const cellTop = r * rowStep;
      const cellBottom = cellTop + cellHeight;

      // Axis-aligned bounding box intersection
      const overlaps = cellLeft < mRight && cellRight > mLeft && cellTop < mBottom && cellBottom > mTop;
      if (overlaps) {
        hits.push(idx);
      }
    }
  }

  return hits;
}

