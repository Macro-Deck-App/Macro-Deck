import { IdleTimer, type IdleTimerClock } from './idle-timer';

describe('idle timer', () => {
  let pending: Array<{ callback: () => void; delayMs: number }>;
  let clock: IdleTimerClock;
  let idle: number;
  let target: HTMLElement;
  let timer: IdleTimer;

  beforeEach(() => {
    pending = [];
    idle = 0;
    clock = {
      set: (callback, delayMs) => { const entry = { callback, delayMs }; pending.push(entry); return entry; },
      clear: handle => { pending = pending.filter(entry => entry !== handle); },
    };
    target = document.createElement('div');
    document.body.appendChild(target);
    timer = new IdleTimer(() => { idle++; }, target, clock);
  });

  afterEach(() => {
    timer.stop();
    target.remove();
  });

  const fire = () => { const entry = pending.shift(); if (entry) entry.callback(); };

  it('fires after the configured delay with no input', () => {
    timer.configure(5000);

    expect(pending[0].delayMs).toBe(5000);
    fire();
    expect(idle).toBe(1);
  });

  it('starts over on any input, including a press a widget node stops in the bubble phase', () => {
    timer.configure(5000);
    const stopper = document.createElement('div');
    stopper.addEventListener('pointerdown', event => event.stopPropagation());
    target.appendChild(stopper);

    stopper.dispatchEvent(new Event('pointerdown', { bubbles: true }));

    expect(pending.length).toBe(1);
    fire();
    expect(idle).toBe(1);
  });

  it('keeps a running countdown when configured again with the same delay', () => {
    timer.configure(5000);
    const first = pending[0];

    timer.configure(5000);

    expect(pending.length).toBe(1);
    expect(pending[0]).toBe(first);
  });

  it('runs nothing while disabled', () => {
    timer.configure(5000);
    timer.configure(null);

    expect(pending.length).toBe(0);
    target.dispatchEvent(new KeyboardEvent('keydown', { key: 'a', bubbles: true }));
    expect(pending.length).toBe(0);
  });
});
