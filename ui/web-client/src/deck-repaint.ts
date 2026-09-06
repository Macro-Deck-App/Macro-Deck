const FRAME_FALLBACK_MS = 16;

function defaultSchedule(callback: () => void): void {
  // The legacy bundle ships its own polyfill, so this is a runtime feature test rather than a build
  // one: whichever is actually in place by the time a repaint is queued is the one used.
  const raf = (window as unknown as { requestAnimationFrame?: (callback: () => void) => number })
    .requestAnimationFrame;
  if (typeof raf === 'function') raf(callback);
  else setTimeout(callback, FRAME_FALLBACK_MS);
}

export interface DeckRepaintTarget {
  paintDeck(): void;

  paintWidget(widgetId: string): boolean;
}

export class DeckRepaintQueue {
  private deckQueued = false;
  private readonly widgetsQueued: { [widgetId: string]: true } = {};
  private flushScheduled = false;
  private readonly schedule: (callback: () => void) => void;

  constructor(
    private readonly target: DeckRepaintTarget,
    schedule?: (callback: () => void) => void,
  ) {
    this.schedule = schedule || defaultSchedule;
  }

  deck(): void {
    this.deckQueued = true;
    this.arm();
  }

  widget(widgetId: string): void {
    if (!this.deckQueued) this.widgetsQueued[widgetId] = true;
    this.arm();
  }

  private arm(): void {
    if (this.flushScheduled) return;
    this.flushScheduled = true;
    this.schedule(() => this.flush());
  }

  private flush(): void {
    this.flushScheduled = false;
    const deck = this.deckQueued;
    this.deckQueued = false;

    const widgetIds: string[] = [];
    for (const widgetId in this.widgetsQueued) {
      if (Object.prototype.hasOwnProperty.call(this.widgetsQueued, widgetId)) {
        widgetIds.push(widgetId);
        delete this.widgetsQueued[widgetId];
      }
    }

    if (deck) {
      this.target.paintDeck();
      return;
    }

    let fellBack = false;
    for (let index = 0; index < widgetIds.length; index++) {
      if (!this.target.paintWidget(widgetIds[index])) fellBack = true;
    }
    if (fellBack) this.target.paintDeck();
  }
}
