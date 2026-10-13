import { OVERLAY_SCROLLBARS_ATTRIBUTE, watchOverlayScrollbars } from './overlay-scrollbars';

describe('watchOverlayScrollbars', () => {
  let frame: HTMLIFrameElement;
  let win: Window;
  let scrollbarStyle: HTMLStyleElement;

  const setScrollbarWidth = (px: number): void => {
    scrollbarStyle.textContent = `::-webkit-scrollbar { width: ${px}px; height: ${px}px; }`;
  };
  const isMarked = (): boolean => win.document.documentElement.hasAttribute(OVERLAY_SCROLLBARS_ATTRIBUTE);

  beforeEach(() => {
    frame = document.createElement('iframe');
    document.body.append(frame);
    win = frame.contentWindow!;
    win.document.open();
    win.document.write('<!doctype html><html><head></head><body></body></html>');
    win.document.close();
    scrollbarStyle = win.document.createElement('style');
    win.document.head.append(scrollbarStyle);
  });

  afterEach(() => {
    frame.remove();
  });

  it('marks the document when scrollbars take no layout width', () => {
    setScrollbarWidth(0);

    watchOverlayScrollbars(win);

    expect(isMarked()).toBeTrue();
  });

  it('leaves the document unmarked when scrollbars take layout width', () => {
    setScrollbarWidth(12);

    watchOverlayScrollbars(win);

    expect(isMarked()).toBeFalse();
  });

  it('follows a change of scrollbar style on the next resize', () => {
    setScrollbarWidth(12);
    watchOverlayScrollbars(win);

    setScrollbarWidth(0);
    win.dispatchEvent(new Event('resize'));

    expect(isMarked()).toBeTrue();
  });

  it('leaves nothing behind in the page', () => {
    const before = win.document.body.childElementCount;

    watchOverlayScrollbars(win);

    expect(win.document.body.childElementCount).toBe(before);
  });
});
