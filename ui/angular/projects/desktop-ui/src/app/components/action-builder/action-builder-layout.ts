export function isToolbarCompact(
  naturalWidth: number,
  availableWidth: number,
  currentlyCompact: boolean,
): boolean {
  return currentlyCompact
    ? naturalWidth > availableWidth - TOOLBAR_LAYOUT_HYSTERESIS
    : naturalWidth > availableWidth;
}

const TOOLBAR_LAYOUT_HYSTERESIS = 4;

export function measureToolbarNaturalWidth(row: HTMLElement): number {
  const groups = Array.from(row.querySelectorAll<HTMLElement>('.tab-group'));
  if (groups.length === 0) return 0;

  const gap = parseFloat(getComputedStyle(groups[0]).columnGap) || 0;
  const total = groups.reduce((sum, group) => sum + naturalGroupWidth(group, gap), 0);
  return total + gap * (groups.length - 1);
}

function naturalGroupWidth(group: HTMLElement, gap: number): number {
  const children = Array.from(group.children) as HTMLElement[];
  if (children.length === 0) return 0;

  const total = children.reduce((sum, child) => {
    const bar = child.matches('shared-tab-bar') ? child.querySelector<HTMLElement>('.tab-bar') : null;
    const width = bar ? bar.scrollWidth + (bar.offsetWidth - bar.clientWidth) : child.offsetWidth;
    return sum + width;
  }, 0);

  return total + gap * (children.length - 1);
}
