export function focusTabForKey(event: KeyboardEvent, tabs: readonly HTMLElement[]): void {
  const key = event.key;
  if (key !== 'ArrowLeft' && key !== 'ArrowRight' && key !== 'Home' && key !== 'End') {
    return;
  }

  if (tabs.length === 0) {
    return;
  }

  const currentIndex = tabs.indexOf(document.activeElement as HTMLElement);
  let nextIndex: number;
  switch (key) {
    case 'ArrowLeft':
      nextIndex = currentIndex <= 0 ? tabs.length - 1 : currentIndex - 1;
      break;
    case 'ArrowRight':
      nextIndex = currentIndex === -1 || currentIndex === tabs.length - 1 ? 0 : currentIndex + 1;
      break;
    case 'Home':
      nextIndex = 0;
      break;
    default:
      nextIndex = tabs.length - 1;
      break;
  }

  event.preventDefault();
  tabs[nextIndex].focus();
}
