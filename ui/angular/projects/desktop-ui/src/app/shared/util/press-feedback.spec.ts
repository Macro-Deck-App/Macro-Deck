import { PRESS_FEEDBACK_MIN_VISIBLE_MS, PressFeedback } from './press-feedback';

describe('PressFeedback', () => {
  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date());
  });

  afterEach(() => jasmine.clock().uninstall());

  it('turns on immediately and reports the change', () => {
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.press();

    expect(feedback.isPressed()).toBeTrue();
    expect(states).toEqual([true]);
  });

  it('keeps a very short press visible for the minimum duration', () => {
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.press();
    jasmine.clock().tick(5);
    feedback.release();

    expect(feedback.isPressed()).toBeTrue();

    jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS - 5);

    expect(feedback.isPressed()).toBeFalse();
    expect(states).toEqual([true, false]);
  });

  it('clears right away once the minimum duration has passed', () => {
    const feedback = new PressFeedback();

    feedback.press();
    jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);
    feedback.release();

    expect(feedback.isPressed()).toBeFalse();
  });

  it('stays on when a new press arrives during the hold', () => {
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.press();
    feedback.release();
    jasmine.clock().tick(10);
    feedback.press();
    jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

    expect(feedback.isPressed()).toBeTrue();
    expect(states).toEqual([true]);
  });

  it('ignores a release without a press', () => {
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.release();
    jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

    expect(feedback.isPressed()).toBeFalse();
    expect(states).toEqual([]);
  });

  it('drops a pending release timer when disposed, reporting no further change from it', () => {
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.press();
    feedback.release();
    feedback.dispose();
    states.length = 0;
    jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

    expect(states).toEqual([]);
  });

  it('releases a mid-press node on dispose, rather than leaving the tint/scale stuck on', () => {
    // Regression: a patch or folder switch can destroy the node before a pointerup/leave ever reaches
    // it. Clearing the release timer alone (the old behaviour) left `isPressed` - and whatever it
    // drives, the press tint and UiTreeWidgetComponent's scale(0.95) - stuck on for good.
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.press();
    feedback.dispose();

    expect(feedback.isPressed()).toBeFalse();
    expect(states).toEqual([true, false]);
  });

  it('disposing an already-released feedback reports no further change', () => {
    const states: boolean[] = [];
    const feedback = new PressFeedback(pressed => states.push(pressed));

    feedback.press();
    jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);
    feedback.release();
    states.length = 0;

    feedback.dispose();

    expect(states).toEqual([]);
  });
});
