import { disablePageZoom } from './disable-page-zoom';

describe('disablePageZoom', () => {
  it('cancels iOS pinch gesture events', () => {
    const doc = document.implementation.createHTMLDocument();
    disablePageZoom(doc);

    for (const type of ['gesturestart', 'gesturechange']) {
      const event = new Event(type, { cancelable: true });
      doc.dispatchEvent(event);
      expect(event.defaultPrevented).withContext(type).toBeTrue();
    }
  });

  it('leaves non-cancelable events untouched', () => {
    const doc = document.implementation.createHTMLDocument();
    disablePageZoom(doc);

    const event = new Event('gesturestart', { cancelable: false });
    doc.dispatchEvent(event);

    expect(event.defaultPrevented).toBeFalse();
  });
});
