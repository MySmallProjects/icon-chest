/**
 * Hierarchical tree model implementing WAI-ARIA Tree View algorithms:
 * node flattening, expansion, keyboard moves, and type-ahead.
 */

export class TreeModel {
  constructor(roots = [], options = {}) {
    this.roots = roots;
    this.expandedIds = new Set(options.expandedIds || []);
    this.selectedId = options.selectedId || null;
    this.getId = options.getId || (node => node.id || node.path);
    this.getName = options.getName || (node => node.name || '');
    this.getChildren = options.getChildren || (node => node.children || []);
  }

  setRoots(roots) {
    this.roots = roots;
    // Check if selectedId still exists in the new tree
    if (this.selectedId !== null && !this.findNode(this.selectedId)) {
      this.selectedId = null; // Fallback to "Show all"
    }
  }

  isExpanded(id) {
    return this.expandedIds.has(id);
  }

  toggleExpanded(id) {
    if (this.expandedIds.has(id)) {
      this.expandedIds.delete(id);
    } else {
      this.expandedIds.add(id);
    }
  }

  setExpanded(id, expanded) {
    if (expanded) {
      this.expandedIds.add(id);
    } else {
      this.expandedIds.delete(id);
    }
  }

  expandParents(targetId) {
    const path = this.getNodePath(targetId);
    if (!path) return;
    for (let i = 0; i < path.length - 1; i++) {
      this.expandedIds.add(this.getId(path[i]));
    }
  }

  findNode(id, nodes = this.roots) {
    for (const node of nodes) {
      if (this.getId(node) === id) return node;
      const children = this.getChildren(node);
      if (children && children.length > 0) {
        const found = this.findNode(id, children);
        if (found) return found;
      }
    }
    return null;
  }

  getNodePath(id, nodes = this.roots, currentPath = []) {
    for (const node of nodes) {
      const nextPath = [...currentPath, node];
      if (this.getId(node) === id) return nextPath;
      const children = this.getChildren(node);
      if (children && children.length > 0) {
        const found = this.getNodePath(id, children, nextPath);
        if (found) return found;
      }
    }
    return null;
  }

  getVisibleNodes() {
    const list = [];
    const traverse = (nodes, level, parentId) => {
      for (const node of nodes) {
        const id = this.getId(node);
        const children = this.getChildren(node);
        const hasChildren = children && children.length > 0;
        const isExpanded = hasChildren && this.expandedIds.has(id);

        list.push({
          node,
          id,
          name: this.getName(node),
          level,
          parentId,
          hasChildren,
          isExpanded
        });

        if (isExpanded) {
          traverse(children, level + 1, id);
        }
      }
    };
    traverse(this.roots, 1, null);
    return list;
  }

  navigate(currentId, key) {
    const visible = this.getVisibleNodes();
    if (visible.length === 0) return null;

    const currentIdx = visible.findIndex(item => item.id === currentId);
    const currentItem = currentIdx !== -1 ? visible[currentIdx] : visible[0];

    switch (key) {
      case 'ArrowDown':
        if (currentIdx < visible.length - 1) {
          return { targetId: visible[currentIdx + 1].id, action: 'focus' };
        }
        return null;

      case 'ArrowUp':
        if (currentIdx > 0) {
          return { targetId: visible[currentIdx - 1].id, action: 'focus' };
        }
        return null;

      case 'ArrowRight':
        if (currentItem.hasChildren) {
          if (!currentItem.isExpanded) {
            this.setExpanded(currentItem.id, true);
            return { targetId: currentItem.id, action: 'expand' };
          } else if (currentIdx < visible.length - 1) {
            return { targetId: visible[currentIdx + 1].id, action: 'focus' };
          }
        }
        return null;

      case 'ArrowLeft':
        if (currentItem.hasChildren && currentItem.isExpanded) {
          this.setExpanded(currentItem.id, false);
          return { targetId: currentItem.id, action: 'collapse' };
        } else if (currentItem.parentId !== null) {
          return { targetId: currentItem.parentId, action: 'focus' };
        }
        return null;

      case 'Home':
        return { targetId: visible[0].id, action: 'focus' };

      case 'End':
        return { targetId: visible[visible.length - 1].id, action: 'focus' };

      default:
        return null;
    }
  }

  typeAhead(currentId, char) {
    if (!char || char.length !== 1) return null;
    const lowerChar = char.toLowerCase();
    const visible = this.getVisibleNodes();
    if (visible.length === 0) return null;

    const currentIdx = visible.findIndex(item => item.id === currentId);
    const searchOrder = [
      ...visible.slice(currentIdx + 1),
      ...visible.slice(0, currentIdx + 1)
    ];

    for (const item of searchOrder) {
      if (item.name && item.name.toLowerCase().startsWith(lowerChar)) {
        return item.id;
      }
    }
    return null;
  }
}

