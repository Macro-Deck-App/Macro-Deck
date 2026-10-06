import { MotionPreference } from './motion-preference';

describe('MotionPreference', () => {
  let reduce: boolean;
  let changeListeners: (() => void)[];
  let originalMatchMedia: unknown;

  function setReduce(value: boolean): void {
    reduce = value;
    changeListeners.forEach(listener => listener());
  }

  beforeEach(() => {
    reduce = false;
    changeListeners = [];
    originalMatchMedia = (window as unknown as { matchMedia: unknown }).matchMedia;
    (window as unknown as { matchMedia: unknown }).matchMedia = (query: string) => ({
      get matches() {
        return query === '(prefers-reduced-motion: reduce)' && reduce;
      },
      addEventListener: (_type: string, listener: () => void) => changeListeners.push(listener),
    });
  });

  afterEach(() => {
    (window as unknown as { matchMedia: unknown }).matchMedia = originalMatchMedia;
  });

  it('reads whether the viewer asked for less motion', () => {
    reduce = true;
    expect(new MotionPreference().reduced()).toBeTrue();
  });

  it('tells its listeners when the viewer changes the setting, so icons can switch appearance', () => {
    const motion = new MotionPreference();
    const repaint = jasmine.createSpy('repaint');
    motion.onChange(repaint);

    setReduce(true);

    expect(motion.reduced()).toBeTrue();
    expect(repaint).toHaveBeenCalledTimes(1);
  });

  it('assumes full motion where the browser cannot tell', () => {
    (window as unknown as { matchMedia: unknown }).matchMedia = undefined;
    expect(new MotionPreference().reduced()).toBeFalse();
  });
});
