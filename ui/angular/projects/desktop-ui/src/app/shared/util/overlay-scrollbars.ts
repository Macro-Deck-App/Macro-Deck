export const OVERLAY_SCROLLBARS_ATTRIBUTE = 'data-overlay-scrollbars';

export function watchOverlayScrollbars(win: Window = window): void {
  const update = (): void => {
    const root = win.document.documentElement;
    const overlay = scrollbarsTakeNoSpace(win.document);
    if (overlay !== root.hasAttribute(OVERLAY_SCROLLBARS_ATTRIBUTE)) {
      root.toggleAttribute(OVERLAY_SCROLLBARS_ATTRIBUTE, overlay);
    }
  };

  update();
  win.addEventListener('resize', update);
  win.addEventListener('focus', update);
}

function scrollbarsTakeNoSpace(doc: Document): boolean {
  const probe = doc.createElement('div');
  probe.style.cssText = 'position:absolute;top:-9999px;left:-9999px;width:100px;height:100px;overflow:scroll;visibility:hidden';
  doc.body.append(probe);
  const overlay = probe.offsetWidth - probe.clientWidth === 0;
  probe.remove();
  return overlay;
}
