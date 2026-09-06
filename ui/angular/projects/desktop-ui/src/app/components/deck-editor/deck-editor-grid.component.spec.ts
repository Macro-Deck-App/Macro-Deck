import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import {
  GridRect,
  GridWidget,
  PinScope,
  WidgetData,
  WidgetType,
} from '@macro-deck/runtime';
import {
  DECK_DRAG_THRESHOLD_PX,
  IWidgetComponent,
  NoticeModalComponent,
  WidgetContextMenuComponent,
  WidgetGridComponent,
  WidgetRegistryService,
} from '@shared';

import { DeckEditorGridComponent } from './deck-editor-grid.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

@Component({
  selector: 'app-test-widget',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div class="test-widget"></div>',
})
class TestWidgetComponent implements IWidgetComponent {
  @Input() data: WidgetData = {};
  @Input() width = 0;
  @Input() height = 0;
  @Input() disabled = false;
  @Input() widgetId?: string;
  @Input() ghost = false;
  @Output() pressedChange = new EventEmitter<boolean>();
}

const PINNED_MOVE_HINT_STORAGE_KEY = 'md.hint.dismissed.pinned-widget-locked';
const PIN_SCOPE_HINT_STORAGE_KEY = 'md.hint.dismissed.pin-scope-middle-click';

const GRID_SIZE_PX = 400;
const COLS = 4;
const ROWS = 4;
const CELL_PX = GRID_SIZE_PX / COLS;

describe('DeckEditorGridComponent', () => {
  let fixture: ComponentFixture<DeckEditorGridComponent>;
  let component: DeckEditorGridComponent;

  function gridWidget(overrides: Partial<GridWidget> = {}): GridWidget {
    return {
      id: 'w1',
      folderId: 'f1',
      x: 0,
      y: 0,
      w: 1,
      h: 1,
      type: WidgetType.Weather,
      data: {},
      ...overrides,
    };
  }

  beforeEach(() => {
    localStorage.removeItem(PINNED_MOVE_HINT_STORAGE_KEY);
    localStorage.removeItem(PIN_SCOPE_HINT_STORAGE_KEY);

    TestBed.configureTestingModule({
      imports: [DeckEditorGridComponent],
      providers: [provideZonelessChangeDetection(), provideLocalizationTesting()],
    });

    TestBed.inject(WidgetRegistryService).register({
      type: WidgetType.Weather,
      component: TestWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestWidgetComponent as never),
    });

    fixture = TestBed.createComponent(DeckEditorGridComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    localStorage.removeItem(PINNED_MOVE_HINT_STORAGE_KEY);
    localStorage.removeItem(PIN_SCOPE_HINT_STORAGE_KEY);
  });

  interface RenderOptions {
    editMode?: boolean;
    selectedWidgetIds?: ReadonlySet<string>;
    cutWidgetIds?: ReadonlySet<string>;
  }

  function render(widgets: GridWidget[], options: RenderOptions = {}): void {
    fixture.componentRef.setInput('cols', COLS);
    fixture.componentRef.setInput('rows', ROWS);
    fixture.componentRef.setInput('spacing', 0);
    fixture.componentRef.setInput('outerMargin', 0);
    fixture.componentRef.setInput('editMode', options.editMode ?? true);
    fixture.componentRef.setInput('selectedWidgetIds', options.selectedWidgetIds ?? new Set<string>());
    fixture.componentRef.setInput('cutWidgetIds', options.cutWidgetIds ?? new Set<string>());
    fixture.componentRef.setInput('widgets', widgets);
    fixture.detectChanges();

    // A headless wrapper measures 0x0, which would collapse every cell to nothing. Feed the real
    // solver a fixed box instead of stubbing it, so the pointer maths under test is the maths the
    // grid ships, and pin the container's viewport origin so client coordinates are grid pixels.
    const box = {
      left: 0, top: 0, right: GRID_SIZE_PX, bottom: GRID_SIZE_PX,
      width: GRID_SIZE_PX, height: GRID_SIZE_PX, x: 0, y: 0, toJSON: () => ({}),
    } as DOMRect;
    spyOn(grid().wrapper.nativeElement, 'getBoundingClientRect').and.returnValue(box);
    spyOn(grid().gridContainer.nativeElement, 'getBoundingClientRect').and.returnValue(box);
    grid().metrics.configure({ cols: COLS, rows: ROWS, spacing: 0, outerMargin: 0 });
    grid().metrics.measure(GRID_SIZE_PX, GRID_SIZE_PX);
    fixture.detectChanges();
  }

  function grid(): WidgetGridComponent {
    return fixture.debugElement.query(By.directive(WidgetGridComponent)).componentInstance as WidgetGridComponent;
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function tiles(): HTMLElement[] {
    return Array.from(host().querySelectorAll('shared-widget-item'));
  }

  function chrome(index = 0): HTMLElement {
    const element = tiles()[index].querySelector<HTMLElement>('.widget-chrome');
    if (!element) throw new Error(`no .widget-chrome on tile ${index}`);
    return element;
  }

  function pointer(target: EventTarget, type: string, x: number, y: number, init: PointerEventInit = {}): PointerEvent {
    const event = new PointerEvent(type, { clientX: x, clientY: y, button: 0, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
  }

  function mouse(target: EventTarget, type: string, init: MouseEventInit = {}): MouseEvent {
    const event = new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
  }

  function dragTile(index: number, init: PointerEventInit = {}): void {
    pointer(chrome(index), 'pointerdown', 100, 100, init);
    pointer(document, 'pointermove', 100 + DECK_DRAG_THRESHOLD_PX * 10, 100, init);
    fixture.detectChanges();
  }

  function dragIsLive(): boolean {
    return host().querySelector('.ghost-layer') !== null;
  }

  function openNoticeHeading(): string | null {
    return host().querySelector('shared-notice-modal .modal-title')?.textContent?.trim() ?? null;
  }

  function closeNotice(dontShowAgain: boolean): void {
    const notice = fixture.debugElement.query(By.directive(NoticeModalComponent));
    (notice.componentInstance as NoticeModalComponent).closed.emit(dontShowAgain);
    fixture.detectChanges();
  }

  function contextMenu(): WidgetContextMenuComponent | null {
    return (fixture.debugElement.query(By.directive(WidgetContextMenuComponent))
      ?.componentInstance as WidgetContextMenuComponent) ?? null;
  }

  function collect<T>(output: { subscribe(next: (value: T) => void): unknown }): T[] {
    const events: T[] = [];
    output.subscribe(value => events.push(value));
    return events;
  }

  // --- middle-click pin shortcut (issues #212, #245) ---------------------------------------------

  describe('middle-click pin shortcut', () => {
    it('pins without a scope, so the host applies its profile-wide default', () => {
      render([gridWidget()]);
      const pins = collect<{ widget: GridWidget; pinned: boolean; scope?: PinScope }>(component.widgetPinnedChange);

      mouse(chrome(), 'auxclick', { button: 1 });

      expect(pins.length).toBe(1);
      expect(pins[0].widget.id).toBe('w1');
      expect(pins[0].pinned).toBeTrue();
      expect(pins[0].scope).toBeUndefined();
    });

    it('unpins a pinned widget, so the shortcut is a true toggle', () => {
      render([gridWidget({ isPinned: true })]);
      const pins = collect<{ widget: GridWidget; pinned: boolean }>(component.widgetPinnedChange);

      mouse(chrome(), 'auxclick', { button: 1 });

      expect(pins.length).toBe(1);
      expect(pins[0].pinned).toBeFalse();
    });

    it('prevents the browser default so the middle click is not pasted or scrolled', () => {
      render([gridWidget()]);

      const event = mouse(chrome(), 'auxclick', { button: 1 });

      expect(event.defaultPrevented).toBeTrue();
    });

    it('explains the pin scope the first time the shortcut pins', () => {
      render([gridWidget()]);

      mouse(chrome(), 'auxclick', { button: 1 });
      fixture.detectChanges();

      expect(openNoticeHeading()).toBe('Pinned to every folder');
    });

    it('shows no scope hint when the shortcut unpins', () => {
      render([gridWidget({ isPinned: true })]);

      mouse(chrome(), 'auxclick', { button: 1 });
      fixture.detectChanges();

      expect(openNoticeHeading()).toBeNull();
    });

    it('stops explaining once the user ticks it off, and remembers that across a reload', () => {
      render([gridWidget(), gridWidget({ id: 'w2', x: 1 })]);

      mouse(chrome(0), 'auxclick', { button: 1 });
      fixture.detectChanges();
      closeNotice(true);

      mouse(chrome(1), 'auxclick', { button: 1 });
      fixture.detectChanges();

      expect(openNoticeHeading()).toBeNull();
      expect(localStorage.getItem(PIN_SCOPE_HINT_STORAGE_KEY)).not.toBeNull();
    });

    it('keeps explaining when the user closes it without ticking it off', () => {
      render([gridWidget(), gridWidget({ id: 'w2', x: 1 })]);

      mouse(chrome(0), 'auxclick', { button: 1 });
      fixture.detectChanges();
      closeNotice(false);

      mouse(chrome(1), 'auxclick', { button: 1 });
      fixture.detectChanges();

      expect(openNoticeHeading()).toBe('Pinned to every folder');
    });

    it('ignores auxiliary buttons other than the middle one', () => {
      render([gridWidget()]);
      const pins = collect(component.widgetPinnedChange);

      mouse(chrome(), 'auxclick', { button: 2 });

      expect(pins).toEqual([]);
    });

    it('starts no drag on a middle-button press, and suppresses browser autoscroll', () => {
      render([gridWidget()]);

      const press = pointer(chrome(), 'pointerdown', 100, 100, { button: 1 });
      pointer(document, 'pointermove', 200, 100);
      fixture.detectChanges();

      expect(dragIsLive()).toBeFalse();
      expect(press.defaultPrevented).toBeTrue();
    });

    it('arms no pinned-move watch on a middle-button press', () => {
      render([gridWidget({ isPinned: true })]);

      pointer(chrome(), 'pointerdown', 100, 100, { button: 1 });
      pointer(document, 'pointermove', 400, 400);
      fixture.detectChanges();

      expect(openNoticeHeading()).toBeNull();
    });

    it('changes nothing outside edit mode - the tile carries no chrome at all', () => {
      render([gridWidget()], { editMode: false });
      const pins = collect(component.widgetPinnedChange);

      expect(host().querySelector('.widget-chrome')).toBeNull();

      mouse(tiles()[0].querySelector('.widget')!, 'auxclick', { button: 1 });
      fixture.detectChanges();

      expect(pins).toEqual([]);
      expect(openNoticeHeading()).toBeNull();
    });
  });

  // --- moving widgets ---------------------------------------------------------------------------

  describe('dragging widgets', () => {
    it('starts a drag on an unmodified press of an unpinned tile', () => {
      render([gridWidget()]);

      dragTile(0);

      expect(dragIsLive()).toBeTrue();
    });

    for (const modifier of ['metaKey', 'shiftKey'] as const) {
      // A drag armed by a modifier-held press lets a few pixels of travel raise click suppression,
      // which swallows the select/deselect outright - and on an already-selected widget it drags the
      // whole group instead of toggling. A selection gesture must never arm a drag. Ctrl is not a
      // selection modifier everywhere, so it stays with isSelectionModifierEvent's own spec.
      it(`starts no drag when the press is held with ${modifier}`, () => {
        render([gridWidget()]);

        dragTile(0, { [modifier]: true });

        expect(dragIsLive()).toBeFalse();
      });
    }

    it('starts no drag and explains the block when a selected member is pinned (issue #213)', () => {
      render(
        [gridWidget({ id: 'a' }), gridWidget({ id: 'b', x: 1, isPinned: true })],
        { selectedWidgetIds: new Set(['a', 'b']) },
      );

      dragTile(0);

      expect(dragIsLive()).toBeFalse();
      expect(openNoticeHeading()).toBe('Widget is pinned');
    });
  });

  // --- pinned widgets (issue #117) --------------------------------------------------------------

  describe('pinned widgets', () => {
    it('shows the pin badge and offers no resize handle in edit mode', () => {
      render([gridWidget({ isPinned: true })]);

      expect(chrome().querySelector('.pin-badge')).not.toBeNull();
      expect(chrome().querySelector('.resize-handle')).toBeNull();
    });

    it('distinguishes a subtree pin\'s reach from a profile pin in the badge tooltip', () => {
      render([
        gridWidget({ id: 'subtree', isPinned: true, pinScope: 'Subtree' }),
        gridWidget({ id: 'profile', x: 1, isPinned: true, pinScope: 'Profile' }),
      ]);

      const subtree = chrome(0).querySelector<HTMLElement>('.pin-badge')!;
      const profile = chrome(1).querySelector<HTMLElement>('.pin-badge')!;

      expect(subtree.title).toContain('this folder and its subfolders');
      expect(profile.title).toContain('every folder of this profile');
      expect(subtree.title).not.toBe(profile.title);
    });

    it('falls back to the profile-wide tooltip when the scope is absent', () => {
      render([gridWidget({ isPinned: true })]);

      expect(chrome().querySelector<HTMLElement>('.pin-badge')!.title)
        .toContain('every folder of this profile');
    });

    it('opens the editor on a plain click, raising neither a drag nor the pinned notice', () => {
      render([gridWidget({ isPinned: true })]);
      const edits = collect<GridWidget>(component.widgetEdit);

      pointer(chrome(), 'pointerdown', 100, 100);
      pointer(document, 'pointermove', 102, 101);
      pointer(document, 'pointerup', 102, 101);
      mouse(chrome(), 'click');
      fixture.detectChanges();

      expect(edits.length).toBe(1);
      expect(dragIsLive()).toBeFalse();
      expect(openNoticeHeading()).toBeNull();
    });

    it('reports the block once the pointer travels far enough to be a move, and not again', () => {
      render([gridWidget({ isPinned: true })]);

      pointer(chrome(), 'pointerdown', 100, 100);
      pointer(document, 'pointermove', 140, 100);
      fixture.detectChanges();
      expect(openNoticeHeading()).toBe('Widget is pinned');
      expect(dragIsLive()).toBeFalse();

      // Further travel in the same press must not raise the notice a second time.
      closeNotice(false);
      pointer(document, 'pointermove', 180, 100);
      fixture.detectChanges();

      expect(openNoticeHeading()).toBeNull();
    });

    it('stops watching after the press ends, so a later move is not mistaken for a drag', () => {
      render([gridWidget({ isPinned: true })]);

      pointer(chrome(), 'pointerdown', 100, 100);
      pointer(document, 'pointerup', 100, 100);
      pointer(document, 'pointermove', 400, 400);
      fixture.detectChanges();

      expect(openNoticeHeading()).toBeNull();
    });
  });

  // --- clicking a tile (issue #213) --------------------------------------------------------------

  describe('clicking a tile', () => {
    it('requests the editor, not a selection, on a plain click', () => {
      render([gridWidget()]);
      const edits = collect<GridWidget>(component.widgetEdit);
      const selects = collect(component.widgetSelect);

      mouse(chrome(), 'click');

      expect(edits.length).toBe(1);
      expect(selects).toEqual([]);
    });

    it('requests a toggle, not the editor, on a Cmd (meta) click', () => {
      render([gridWidget()]);
      const edits = collect(component.widgetEdit);
      const selects = collect<{ widget: GridWidget; toggle: boolean; range: boolean }>(component.widgetSelect);

      mouse(chrome(), 'click', { metaKey: true });

      expect(selects.map(s => ({ toggle: s.toggle, range: s.range }))).toEqual([{ toggle: true, range: false }]);
      expect(edits).toEqual([]);
    });

    it('requests a range on a Shift+click', () => {
      render([gridWidget()]);
      const selects = collect<{ widget: GridWidget; toggle: boolean; range: boolean }>(component.widgetSelect);

      mouse(chrome(), 'click', { shiftKey: true });

      expect(selects.map(s => ({ toggle: s.toggle, range: s.range }))).toEqual([{ toggle: false, range: true }]);
    });

    it('neither selects nor edits when the click follows a drag', () => {
      render([gridWidget()]);
      const edits = collect(component.widgetEdit);
      const selects = collect(component.widgetSelect);

      dragTile(0);
      pointer(document, 'pointerup', 100 + DECK_DRAG_THRESHOLD_PX * 10, 100);
      // Modifier held on purpose: a plain click would pass whichever order the suppression check and
      // the selection branch run in, so it would not pin the ordering this test exists for.
      mouse(chrome(), 'click', { metaKey: true });

      expect(edits).toEqual([]);
      expect(selects).toEqual([]);
    });
  });

  // --- the editing context menu ------------------------------------------------------------------

  describe('the editing context menu', () => {
    it('opens at the pointer on a right-click and suppresses the native menu', () => {
      render([gridWidget()]);

      const event = mouse(chrome(), 'contextmenu', { clientX: 37, clientY: 91 });
      fixture.detectChanges();

      const menu = contextMenu()!;
      expect(menu.isOpen()).toBeTrue();
      expect(menu.mode()).toBe('widget');
      expect([menu.x(), menu.y()]).toEqual([37, 91]);
      expect(event.defaultPrevented).toBeTrue();
    });

    it('opens no editing context menu outside edit mode', () => {
      render([gridWidget()], { editMode: false });

      mouse(tiles()[0].querySelector('.widget')!, 'contextmenu', { clientX: 37, clientY: 91 });
      fixture.detectChanges();

      expect(contextMenu()).toBeNull();
      expect(host().querySelector('.op-panel')).toBeNull();
    });
  });

  // --- the edit chrome ---------------------------------------------------------------------------

  it('shows the edit chrome without a hover delete button (issue #243)', () => {
    render([gridWidget()]);

    expect(chrome().querySelector('.edit-overlay')).not.toBeNull();
    expect(chrome().querySelector('.resize-handle')).not.toBeNull();
    expect(host().querySelector('.delete-btn')).toBeNull();
  });

  // --- marquee selection (issue #213) ------------------------------------------------------------

  describe('marquee selection', () => {
    // No widgets are rendered in this block, so the first cell is free.
    function emptyCell(): HTMLElement {
      return host().querySelector<HTMLElement>('.empty-cell')!;
    }

    it('emits marqueeSelect covering the dragged cells once the drag clears the threshold', () => {
      render([]);
      const events = collect<{ rect: GridRect; additive: boolean }>(component.marqueeSelect);

      pointer(grid().gridContainer.nativeElement, 'pointerdown', CELL_PX * 0.1, CELL_PX * 0.1);
      pointer(document, 'pointermove', CELL_PX * 1.5, CELL_PX * 1.5);
      pointer(document, 'pointerup', CELL_PX * 1.5, CELL_PX * 1.5);

      expect(events).toEqual([{ rect: { x: 0, y: 0, w: 2, h: 2 }, additive: false }]);
    });

    it('marks the release additive when Ctrl/Cmd is held', () => {
      render([]);
      const events = collect<{ rect: GridRect; additive: boolean }>(component.marqueeSelect);

      pointer(grid().gridContainer.nativeElement, 'pointerdown', CELL_PX * 0.1, CELL_PX * 0.1);
      pointer(document, 'pointermove', CELL_PX * 1.5, CELL_PX * 1.5);
      pointer(document, 'pointerup', CELL_PX * 1.5, CELL_PX * 1.5, { ctrlKey: true });

      expect(events[0].additive).toBeTrue();
    });

    it('treats a below-threshold press on an empty cell as a click, not a marquee', () => {
      render([]);
      const marquees = collect(component.marqueeSelect);
      const cellClicks = collect<{ x: number; y: number }>(component.cellClick);

      const startX = CELL_PX * 0.1;
      const startY = CELL_PX * 0.1;
      const jitter = Math.max(1, DECK_DRAG_THRESHOLD_PX - 1);

      pointer(emptyCell(), 'pointerdown', startX, startY);
      pointer(document, 'pointermove', startX + jitter, startY);
      pointer(document, 'pointerup', startX + jitter, startY);
      mouse(emptyCell(), 'click');

      expect(marquees).toEqual([]);
      expect(cellClicks).toEqual([{ x: 0, y: 0 }]);
    });

    it('never starts a marquee from a press on a widget', () => {
      render([gridWidget()]);
      const marquees = collect(component.marqueeSelect);

      pointer(chrome(), 'pointerdown', CELL_PX * 0.1, CELL_PX * 0.1);
      pointer(document, 'pointermove', CELL_PX * 1.5, CELL_PX * 1.5);
      pointer(document, 'pointerup', CELL_PX * 1.5, CELL_PX * 1.5);

      expect(marquees).toEqual([]);
    });
  });

  describe('clicking empty space while a selection is active', () => {
    // Cell 0 is where the fixture's widget sits, and the grid only reports clicks on free cells.
    function freeCell(): HTMLElement {
      return host().querySelectorAll<HTMLElement>('.empty-cell')[1];
    }

    it('clears the selection on a plain click', () => {
      render([gridWidget()], { selectedWidgetIds: new Set(['w1']) });
      const cells = collect(component.cellClick);
      let cleared = 0;
      component.selectionClear.subscribe(() => cleared++);

      mouse(freeCell(), 'click');

      expect(cleared).toBe(1);
      expect(cells).toEqual([]);
    });

    it('still offers a new widget when a modifier is held', () => {
      // A modified click on empty space is not "deselect" - the modifiers are how the user extends a
      // selection, so the click keeps its ordinary meaning and opens the type selector.
      render([gridWidget()], { selectedWidgetIds: new Set(['w1']) });
      const cells = collect(component.cellClick);
      let cleared = 0;
      component.selectionClear.subscribe(() => cleared++);

      mouse(freeCell(), 'click', { metaKey: true });

      expect(cleared).toBe(0);
      expect(cells.length).toBe(1);
    });
  });

  describe('per-tile render state', () => {
    it('dims a widget that is pending a cut, and only that widget', () => {
      render([gridWidget(), gridWidget({ id: 'w2', x: 1 })], { cutWidgetIds: new Set(['w1']) });

      const dimmed = host().querySelectorAll('shared-widget-item .widget.is-cut');
      expect(dimmed.length).toBe(1);
      expect(component.renderStates().get('w1')?.dimmed).toBeTrue();
      expect(component.renderStates().has('w2')).toBeFalse();
    });

    it('hides the widget being dragged, because its visuals move to the ghost', () => {
      render([gridWidget()]);

      dragTile(0);

      expect(component.renderStates().get('w1')?.hidden).toBeTrue();
      expect(host().querySelector('shared-widget-item .widget.drag-source')).not.toBeNull();
    });

    it('re-dims a cut widget after the deck it lives on comes back', () => {
      // Navigating away and back changes the widget list without the clipboard or any drag changing.
      // A render state derived from the plain input rather than a signal would stay on the map it
      // built for the other folder, and the cut widget would come back undimmed.
      render([gridWidget()], { cutWidgetIds: new Set(['w1']) });
      expect(component.renderStates().get('w1')?.dimmed).toBeTrue();

      fixture.componentRef.setInput('widgets', [gridWidget({ id: 'other' })]);
      fixture.detectChanges();
      expect(component.renderStates().has('w1')).toBeFalse();

      fixture.componentRef.setInput('widgets', [gridWidget()]);
      fixture.detectChanges();

      expect(component.renderStates().get('w1')?.dimmed).toBeTrue();
      expect(host().querySelector('shared-widget-item .widget.is-cut')).not.toBeNull();
    });

    it('leaves an untouched deck entirely undecorated', () => {
      render([gridWidget(), gridWidget({ id: 'w2', x: 1 })]);

      expect(component.renderStates().size).toBe(0);
      expect(host().querySelector('.widget.is-cut')).toBeNull();
      expect(host().querySelector('.widget.drag-source')).toBeNull();
    });
  });
});
