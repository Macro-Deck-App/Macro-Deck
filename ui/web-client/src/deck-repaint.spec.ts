import { DeckRepaintQueue } from './deck-repaint';

class FakeScheduler {
  readonly pending: Array<() => void> = [];

  readonly schedule = (callback: () => void): void => {
    this.pending.push(callback);
  };

  run(): void {
    const next = this.pending.shift();
    if (next) next();
  }
}

describe('DeckRepaintQueue', () => {
  let repaints: string[];
  let scheduler: FakeScheduler;
  let queue: DeckRepaintQueue;

  beforeEach(() => {
    repaints = [];
    scheduler = new FakeScheduler();
    queue = new DeckRepaintQueue({
      paintDeck: () => { repaints.push('deck'); },
      paintWidget: widgetId => { repaints.push(`widget:${widgetId}`); return true; },
    }, scheduler.schedule);
  });

  it('lets a queued deck repaint supersede every widget repaint queued alongside it', () => {
    queue.widget('a');
    queue.widget('b');
    queue.deck();
    queue.widget('c');

    scheduler.run();

    expect(repaints).toEqual(['deck']);
  });

  it('schedules one flush for a burst of requests, and repaints each widget once', () => {
    queue.widget('a');
    queue.widget('b');
    queue.widget('a');

    expect(scheduler.pending.length).toBe(1);

    scheduler.run();

    expect(repaints.length).toBe(2);
    expect(repaints).toContain('widget:a');
    expect(repaints).toContain('widget:b');
  });

  it('schedules nothing while nothing is queued, and a flush does not re-arm itself', () => {
    expect(scheduler.pending.length).toBe(0);

    queue.widget('a');
    scheduler.run();

    expect(scheduler.pending.length).toBe(0);

    queue.widget('b');
    expect(scheduler.pending.length).toBe(1);
    scheduler.run();

    expect(repaints).toEqual(['widget:a', 'widget:b']);
  });

  it('drops a pending widget repaint in favour of a deck repaint queued after it', () => {
    queue.widget('a');
    queue.deck();
    scheduler.run();

    expect(repaints).toEqual(['deck']);
  });

  it('works with requestAnimationFrame absent, falling back to a timer', () => {
    const raf = (window as unknown as { requestAnimationFrame?: unknown }).requestAnimationFrame;
    delete (window as unknown as { requestAnimationFrame?: unknown }).requestAnimationFrame;
    jasmine.clock().install();
    try {
      const bare = new DeckRepaintQueue({
        paintDeck: () => { repaints.push('deck'); },
        paintWidget: widgetId => { repaints.push(`widget:${widgetId}`); return true; },
      });

      bare.deck();
      jasmine.clock().tick(16);

      expect(repaints).toEqual(['deck']);
    } finally {
      jasmine.clock().uninstall();
      (window as unknown as { requestAnimationFrame?: unknown }).requestAnimationFrame = raf;
    }
  });
});
