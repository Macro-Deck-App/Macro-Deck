import {
  ClientInputEvent,
  GridWidget,
  HardwareInputMap,
  HardwareInputSource,
  keyboardInputSource,
  wheelInputSource,
} from '@macro-deck/runtime';

export interface DeckInputTarget {
  widgets(): readonly GridWidget[];
  activateWidget(widgetId: string): boolean;
  triggerWidget(widget: GridWidget): void;
  goBack(): void;
  modalOpen(): boolean;
  focusChanged(widgetId: string | null): void;
}

export class DeckInput {
  private index = 0;
  private readonly teardowns: Array<() => void> = [];

  constructor(
    private readonly target: DeckInputTarget,
    map: HardwareInputMap | undefined,
    documentTarget: EventTarget = document,
    sources: readonly HardwareInputSource[] = [keyboardInputSource, wheelInputSource],
  ) {
    if (!map) return;

    for (let index = 0; index < sources.length; index++) {
      this.teardowns.push(sources[index]({
        map,
        target: documentTarget,
        emit: event => this.handle(event),
      }));
    }
  }

  focusedWidgetId(): string | null {
    const widget = this.focusedWidget();
    return widget === null ? null : widget.id;
  }

  resetFocus(): void {
    this.index = 0;
    this.target.focusChanged(this.focusedWidgetId());
  }

  dispose(): void {
    for (let index = 0; index < this.teardowns.length; index++) this.teardowns[index]();
    this.teardowns.length = 0;
  }

  private ordered(): readonly GridWidget[] {
    const widgets = this.target.widgets().slice();
    widgets.sort((left, right) => (left.y - right.y) || (left.x - right.x));
    return widgets;
  }

  private focusedWidget(): GridWidget | null {
    if (this.teardowns.length === 0) return null;
    const widgets = this.ordered();
    if (widgets.length === 0) return null;
    return widgets[Math.min(this.index, widgets.length - 1)];
  }

  private handle(event: ClientInputEvent): void {
    switch (event.kind) {
      case 'focusMove':
        this.move(event.delta);
        return;
      case 'selectIndex':
        // A numbered button aims and presses in one action; moving the cursor with it is what makes
        // which tile it hit visible.
        this.focusIndex(event.index);
        this.activateFocused();
        return;
      case 'activate':
        this.activateFocused();
        return;
      case 'back':
        // A dialog that is up owns this gesture and closes on it; navigating the deck underneath
        // would leave someone staring at a dialog for a folder they just left.
        if (!this.target.modalOpen()) this.target.goBack();
        return;
    }
  }

  private move(delta: number): void {
    const count = this.ordered().length;
    if (count === 0) return;

    const next = (this.index + delta) % count;
    this.index = next < 0 ? next + count : next;
    this.target.focusChanged(this.focusedWidgetId());
  }

  private focusIndex(index: number): void {
    if (index < 0 || index >= this.ordered().length) return;
    this.index = index;
    this.target.focusChanged(this.focusedWidgetId());
  }

  private activateFocused(): void {
    const widget = this.focusedWidget();
    if (widget === null) return;

    // A widget whose tree claims the press runs its own lifecycle; anything else takes the tile's
    // trigger path. That is exactly the split a tap goes through.
    if (this.target.activateWidget(widget.id)) return;
    this.target.triggerWidget(widget);
  }
}
