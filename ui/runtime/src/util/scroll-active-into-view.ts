export function scrollActiveIntoView(container: HTMLElement, active: HTMLElement): void {
  const styles = getComputedStyle(container);
  const padLeft = parseFloat(styles.paddingLeft) || 0;
  const padRight = parseFloat(styles.paddingRight) || 0;
  const containerRect = container.getBoundingClientRect();
  const activeRect = active.getBoundingClientRect();
  const left = activeRect.left - containerRect.left - container.clientLeft + container.scrollLeft;
  const right = left + activeRect.width;

  if (left - padLeft < container.scrollLeft) {
    container.scrollLeft = left - padLeft;
  } else if (right + padRight > container.scrollLeft + container.clientWidth) {
    container.scrollLeft = right + padRight - container.clientWidth;
  }
}
