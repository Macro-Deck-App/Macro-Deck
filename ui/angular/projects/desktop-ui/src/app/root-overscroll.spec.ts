describe('shell overscroll', () => {
  it('does not let the viewport overscroll in either direction', () => {
    for (const element of [document.documentElement, document.body]) {
      const style = getComputedStyle(element);

      expect(style.overscrollBehaviorY).withContext(`${element.tagName} vertical`).toBe('none');
      expect(style.overscrollBehaviorX).withContext(`${element.tagName} horizontal`).toBe('none');
    }
  });
});
