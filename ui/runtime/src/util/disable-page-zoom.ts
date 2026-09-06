// iOS Safari ignores both user-scalable=no and touch-action for pinch; only cancelling its
// proprietary gesture events stops it.
export function disablePageZoom(doc: Document = document): void {
  const prevent = (event: Event) => {
    if (event.cancelable) {
      event.preventDefault();
    }
  };
  for (const type of ['gesturestart', 'gesturechange']) {
    doc.addEventListener(type, prevent, { passive: false });
  }
}
