import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { EMPTY, Observable } from 'rxjs';

import { UiTreeWidgetComponent } from './ui-tree-widget.component';
import {
  UiSessionHandle, UiSessionOpenRequest, UiSessionRejection, UiSessionService,
} from '../../../services/ui-session.service';
import { ApiService, ConnectionState } from '../../../transport';
import { HOST_URL_RESOLVER } from '../../../transport/host-url';
import { LocalizationService } from '../../../localization';
import { PRESS_FEEDBACK_MIN_VISIBLE_MS } from '../../../util/press-feedback';
import { UiNode, UiNodeEvent, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from '../../ui-render/ui-widget-tree-context';

class FakeUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  closed = false;
  readonly sent: UiNodeEvent[] = [];

  send(event: UiNodeEvent): void {
    this.sent.push(event);
  }

  close(): void {
    this.closed = true;
  }
}

function textNode(sizeBasis: number, text: unknown = 'x'): UiNode {
  return {
    id: 'root',
    type: UiComponents.Text,
    properties: {
      [UiComponentProperties.Text]: text,
      [UiComponentProperties.Size]: { basis: sizeBasis },
    },
  };
}

describe('UiTreeWidgetComponent', () => {
  let opens: UiSessionOpenRequest[];
  let handles: FakeUiSessionHandle[];
  let connectionState: ReturnType<typeof signal<ConnectionState>>;

  function createFixture(inputs: {
    widgetId?: string;
    widgetType?: string;
    data?: unknown;
    ghost?: boolean;
    sample?: boolean;
    variableScopeWidgetId?: string;
    width?: number;
    height?: number;
    disabled?: boolean;
  } = {}): ComponentFixture<UiTreeWidgetComponent> {
    const fixture = TestBed.createComponent(UiTreeWidgetComponent);
    if (inputs.widgetId !== undefined) fixture.componentRef.setInput('widgetId', inputs.widgetId);
    if (inputs.widgetType !== undefined) fixture.componentRef.setInput('widgetType', inputs.widgetType);
    fixture.componentRef.setInput('data', inputs.data ?? {});
    fixture.componentRef.setInput('ghost', inputs.ghost ?? false);
    fixture.componentRef.setInput('sample', inputs.sample ?? false);
    if (inputs.variableScopeWidgetId !== undefined) {
      fixture.componentRef.setInput('variableScopeWidgetId', inputs.variableScopeWidgetId);
    }
    fixture.componentRef.setInput('width', inputs.width ?? 120);
    fixture.componentRef.setInput('height', inputs.height ?? 120);
    fixture.componentRef.setInput('disabled', inputs.disabled ?? false);
    fixture.detectChanges();
    return fixture;
  }

  function tile(fixture: ComponentFixture<UiTreeWidgetComponent>): HTMLElement {
    return (fixture.nativeElement as HTMLElement).querySelector('.widget-tile') as HTMLElement;
  }

  function renderedRootOf(fixture: ComponentFixture<UiTreeWidgetComponent>): UiNode | null {
    return (fixture.componentInstance as unknown as { renderedRoot: () => UiNode | null }).renderedRoot();
  }

  beforeEach(() => {
    opens = [];
    handles = [];
    connectionState = signal<ConnectionState>('connected');

    const fakeUiSessions = {
      open: (request: UiSessionOpenRequest): UiSessionHandle => {
        opens.push(request);
        const handle = new FakeUiSessionHandle();
        handles.push(handle);
        return handle;
      },
    };

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getLocalization', 'onNotification']);
    apiSpy.getLocalization.and.resolveTo({
      culture: 'en', fallbackCulture: 'en', translations: {}, followSystem: false, availableCultures: ['en'],
    });
    apiSpy.onNotification.and.callFake(<T,>(): Observable<T> => EMPTY);
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: UiSessionService, useValue: fakeUiSessions },
        { provide: ApiService, useValue: apiSpy },
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  describe('layout', () => {
    it("gives the tree the widget's real box so a non-square tile is filled edge to edge", async () => {
      const fixture = createFixture({ widgetId: 'w-1', width: 240, height: 80 });
      handles[0].root.set({
        id: 'root',
        type: UiComponents.Stack,
        properties: { [UiComponentProperties.Direction]: 'vertical' },
        children: [],
      });
      fixture.detectChanges();
      await fixture.whenStable();

      const rendered = tile(fixture).querySelector('.widget-stack') as HTMLElement;

      expect(rendered).withContext('the root stack should render').toBeTruthy();
      expect(rendered.style.width).toBe('240px');
      expect(rendered.style.height).toBe('80px');
    });
  });

  describe('session lifecycle', () => {
    it('opens a widget session with the widget id on init', () => {
      createFixture({ widgetId: 'w1' });

      expect(opens).toEqual([{ kind: 'widget', widgetId: 'w1', ghost: false }]);
    });

    it('does not re-open when the data input changes for a live widget', () => {
      const fixture = createFixture({ widgetId: 'w1', data: { forecastDays: 3 } });
      expect(opens.length).toBe(1);

      fixture.componentRef.setInput('data', { forecastDays: 5 });
      fixture.detectChanges();

      expect(opens.length).toBe(1);
    });

    it('does not re-open on a resize, and rebases the tree context instead', () => {
      const fixture = createFixture({ widgetId: 'w1', width: 120, height: 120 });
      expect(opens.length).toBe(1);

      fixture.componentRef.setInput('width', 240);
      fixture.componentRef.setInput('height', 60);
      fixture.detectChanges();

      expect(opens.length).toBe(1);
      expect(fixture.debugElement.injector.get(UiWidgetTreeContext).basis()).toBe(60);
    });

    it('closes the session on destroy', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      fixture.destroy();

      expect(handles[0].closed).toBeTrue();
    });

    it('does not re-open while the connection stays connected', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      expect(opens.length).toBe(1);

      connectionState.set('connected');
      fixture.detectChanges();

      expect(opens.length).toBe(1);
    });

    it('re-opens after the connection drops and recovers, retaining the last tree meanwhile', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(textNode(0.5, 'first'));
      fixture.detectChanges();
      expect(renderedRootOf(fixture)).toEqual(textNode(0.5, 'first'));

      connectionState.set('reconnecting');
      fixture.detectChanges();
      expect(opens.length).toBe(1);

      connectionState.set('connected');
      fixture.detectChanges();

      expect(opens.length).toBe(2);
      expect(handles[0].closed).toBeTrue();
      expect(handles[1].closed).toBeFalse();
      expect(handles[1].root()).toBeNull();
      expect(renderedRootOf(fixture)).toEqual(textNode(0.5, 'first'));

      handles[1].root.set(textNode(0.5, 'second'));
      fixture.detectChanges();
      expect(renderedRootOf(fixture)).toEqual(textNode(0.5, 'second'));
    });

    it('opens a ghost session with ghost: true, independent of the live tile session', () => {
      const live = createFixture({ widgetId: 'w1', ghost: false });
      const ghost = createFixture({ widgetId: 'w1', ghost: true });

      expect(opens).toEqual([
        { kind: 'widget', widgetId: 'w1', ghost: false },
        { kind: 'widget', widgetId: 'w1', ghost: true },
      ]);

      ghost.destroy();

      expect(handles[1].closed).toBeTrue();
      expect(handles[0].closed).toBeFalse();

      live.destroy();
    });

    // Long enough to outlast the debounce whatever it is set to - the requirement is that a draft change
    // reaches the preview, not that it takes any particular number of milliseconds.
    const PAST_THE_DEBOUNCE_MS = 2000;

    it('re-opens a preview on a draft data change, but not synchronously with the keystroke', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetType: 'Weather', data: { forecastDays: 3 } });
        expect(opens).toEqual([{ kind: 'widget', widgetType: 'Weather', data: { forecastDays: 3 }, sample: false }]);

        fixture.componentRef.setInput('data', { forecastDays: 5 });
        fixture.detectChanges();
        expect(opens.length).toBe(1);

        jasmine.clock().tick(PAST_THE_DEBOUNCE_MS);
        expect(opens.length).toBe(2);
        expect(opens[1]).toEqual({ kind: 'widget', widgetType: 'Weather', data: { forecastDays: 5 }, sample: false });
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('names the widget a draft belongs to, so its widget-scoped variables resolve as they do on the deck',
      () => {
        createFixture({ widgetType: 'ActionButton', data: { label: 'Test: {{ vars.bla }}' },
          variableScopeWidgetId: 'w-7' });

        expect(opens).toEqual([{
          kind: 'widget',
          widgetType: 'ActionButton',
          data: { label: 'Test: {{ vars.bla }}' },
          sample: false,
          variableScopeWidgetId: 'w-7',
        }]);
      });

    it('re-opens the preview against the new widget when the scope changes', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetType: 'ActionButton', data: {}, variableScopeWidgetId: 'w-1' });
        expect(opens.length).toBe(1);

        fixture.componentRef.setInput('variableScopeWidgetId', 'w-2');
        fixture.detectChanges();
        jasmine.clock().tick(PAST_THE_DEBOUNCE_MS);

        // A session still scoped to w-1 would keep resolving the previous widget's variable values.
        expect(opens.length).toBe(2);
        expect(opens[1]).toEqual(
          { kind: 'widget', widgetType: 'ActionButton', data: {}, sample: false, variableScopeWidgetId: 'w-2' });
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it("asks for the widget type's sample when the caller wants one rather than the draft's own reading", () => {
      createFixture({ widgetType: 'Weather', data: {}, sample: true });

      expect(opens).toEqual([{ kind: 'widget', widgetType: 'Weather', data: {}, sample: true }]);
    });

    it('coalesces a burst of draft changes into one re-open carrying the last of them', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetType: 'Weather', data: { forecastDays: 1 } });
        expect(opens.length).toBe(1);

        // Typing: several changes closer together than the debounce.
        for (const forecastDays of [2, 3, 4]) {
          fixture.componentRef.setInput('data', { forecastDays });
          fixture.detectChanges();
          jasmine.clock().tick(10);
        }

        expect(opens.length).toBe(1);

        jasmine.clock().tick(PAST_THE_DEBOUNCE_MS);
        expect(opens.length).toBe(2);
        expect(opens[1]).toEqual({ kind: 'widget', widgetType: 'Weather', data: { forecastDays: 4 }, sample: false });
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  describe('the component-level UiWidgetTreeContext', () => {
    it('gives each tile its own basis, isolated from a sibling tile', () => {
      const small = createFixture({ widgetId: 'w1', width: 60, height: 60 });
      const large = createFixture({ widgetId: 'w2', width: 240, height: 240 });

      handles[0].root.set(textNode(1));
      handles[1].root.set(textNode(1));
      small.detectChanges();
      large.detectChanges();

      const smallFont = (small.nativeElement as HTMLElement).querySelector('.widget-text') as HTMLElement;
      const largeFont = (large.nativeElement as HTMLElement).querySelector('.widget-text') as HTMLElement;

      expect(parseFloat(smallFont.style.fontSize)).toBeCloseTo(60, 5);
      expect(parseFloat(largeFont.style.fontSize)).toBeCloseTo(240, 5);
    });
  });

  describe('locale change (S25)', () => {
    it('re-renders text in place, without reopening the session or refetching anything', async () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set({
        id: 'root',
        type: UiComponents.Text,
        properties: {
          [UiComponentProperties.Text]: {
            $localized: { scope: 'macrodeck.app', key: 'Widgets.Weather.Condition.PartlyCloudy' },
          },
        },
      });
      fixture.detectChanges();

      const textEl = () => (fixture.nativeElement as HTMLElement).querySelector('.widget-text') as HTMLElement;
      expect(textEl().textContent).toBe('Partly cloudy');
      const elementBefore = textEl();
      const opensBefore = opens.length;

      const api = TestBed.inject(ApiService) as jasmine.SpyObj<ApiService>;
      api.getLocalization.and.resolveTo({
        culture: 'de-DE',
        fallbackCulture: 'en',
        translations: { 'macrodeck.app:Widgets.Weather.Condition.PartlyCloudy': 'Teilweise bewölkt' },
        followSystem: false,
        availableCultures: ['en', 'de-DE'],
      });
      await TestBed.inject(LocalizationService).loadFromHost();
      fixture.detectChanges();

      const elementAfter = textEl();
      expect(elementAfter.textContent).toBe('Teilweise bewölkt');
      expect(elementAfter).toBe(elementBefore);
      expect(opens.length).toBe(opensBefore);
    });
  });

  describe('press interaction (S19)', () => {
    it('emits onTouchStart, onTouchEnd, onShortPress on a short tap', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetId: 'w1' });
        const triggers: string[] = [];
        const pressed: boolean[] = [];
        fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
        fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

        const el = tile(fixture);
        el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        jasmine.clock().tick(100);
        el.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
        jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

        expect(triggers).toEqual(['onTouchStart', 'onTouchEnd', 'onShortPress']);
        expect(pressed).toEqual([true, false]);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('emits onTouchStart, onLongPress, onTouchEnd (no onShortPress) on a long press', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetId: 'w1' });
        const triggers: string[] = [];
        const pressed: boolean[] = [];
        fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
        fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

        const el = tile(fixture);
        el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        jasmine.clock().tick(600);
        el.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
        jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

        expect(triggers).toEqual(['onTouchStart', 'onLongPress', 'onTouchEnd']);
        expect(pressed).toEqual([true, false]);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('emits onTouchStart, onTouchEnd (no onShortPress) when the pointer leaves while down', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetId: 'w1' });
        const triggers: string[] = [];
        const pressed: boolean[] = [];
        fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
        fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

        const el = tile(fixture);
        el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        el.dispatchEvent(new PointerEvent('pointerleave', { bubbles: true }));
        jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

        expect(triggers).toEqual(['onTouchStart', 'onTouchEnd']);
        expect(pressed).toEqual([true, false]);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('reports no tile press for a button nested in the tree, and tints that button', async () => {
      const fixture = createFixture({ widgetId: 'w1' });
      const pressed: boolean[] = [];
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));
      handles[0].root.set({
        id: 'root',
        type: UiComponents.Stack,
        properties: {},
        children: [{ id: 'nested', type: UiComponents.Button, properties: { [UiComponentProperties.Events]: ['press'] } }],
      });
      fixture.detectChanges();
      await fixture.whenStable();
      const nested = tile(fixture).querySelector('[data-node-id="nested"]') as HTMLElement;
      expect(nested).withContext('the nested button should render').toBeTruthy();

      nested.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));

      expect(pressed).not.toContain(true);
      expect(nested.querySelector('.widget-press-tint-active')).not.toBeNull();
    });

    it('reports a tile press for a button that is the whole tree', async () => {
      const fixture = createFixture({ widgetId: 'w1' });
      const pressed: boolean[] = [];
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));
      handles[0].root.set({
        id: 'root',
        type: UiComponents.Button,
        properties: { [UiComponentProperties.Events]: ['press'] },
      });
      fixture.detectChanges();
      await fixture.whenStable();
      const root = tile(fixture).querySelector('[data-node-id="root"]') as HTMLElement;
      expect(root).withContext('the root button should render').toBeTruthy();

      root.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));

      expect(pressed).toEqual([true]);
    });

    it('finishes a press started before the tree claimed the gesture, even once the tree lands mid-press', () => {
      // Regression for finding 8: onPressStart and onPressEnd both used to gate on the *current*
      // treeClaimsGesture() value. Pressing before the tree arrives (root still null, so the wrapper
      // starts its own press) and having the tree land - and start claiming the gesture - before the
      // release used to strand isPressed on true forever: no onTouchEnd, and the still-armed long-press
      // timer fires a spurious onLongPress at 600ms.
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetId: 'w1' });
        const triggers: string[] = [];
        const pressed: boolean[] = [];
        fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
        fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

        const el = tile(fixture);
        el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));

        // The tree now lands and claims the gesture itself, mid-press.
        handles[0].root.set({
          id: 'root', type: UiComponents.Button, properties: { [UiComponentProperties.Events]: ['press'] },
        });
        fixture.detectChanges();

        jasmine.clock().tick(100);
        el.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
        jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

        // No spurious onLongPress from the still-armed timeout at the 600ms mark.
        jasmine.clock().tick(600);

        expect(triggers).toEqual(['onTouchStart', 'onTouchEnd', 'onShortPress']);
        expect(pressed).toEqual([true, false]);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('emits nothing while disabled', () => {
      const fixture = createFixture({ widgetId: 'w1', disabled: true });
      const triggers: string[] = [];
      const pressed: boolean[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

      const el = tile(fixture);
      el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      el.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));

      expect(triggers).toEqual([]);
      expect(pressed).toEqual([]);
    });
  });

  describe('activation without a pointer (issue #727)', () => {
    function buttonRoot(events: string[]): UiNode {
      return {
        id: 'button',
        type: UiComponents.Button,
        properties: { [UiComponentProperties.Events]: events },
      };
    }

    it('runs the declared node lifecycle for a tree that claims the press', () => {
      // A hardware button has to reach the same producer a tap does. Emitting a bare `press` would
      // starve a producer of the press-start/press-end pair it declared and asked for.
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(buttonRoot(['press-start', 'press-end', 'press']));
      fixture.detectChanges();

      const triggers: string[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.activateFromInput();

      expect(handles[0].sent.map(event => event.name)).toEqual(['press-start', 'press-end', 'press']);
      expect(triggers).toEqual([]);
    });

    it('sends only the events the node actually declared', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(buttonRoot(['press']));
      fixture.detectChanges();

      fixture.componentInstance.activateFromInput();

      expect(handles[0].sent.map(event => event.name)).toEqual(['press']);
    });

    it('finds the interactive node when it is nested inside the tree', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set({
        id: 'root',
        type: UiComponents.Stack,
        children: [buttonRoot(['press'])],
      } as UiNode);
      fixture.detectChanges();

      fixture.componentInstance.activateFromInput();

      expect(handles[0].sent.map(event => event.nodeId)).toEqual(['button']);
    });

    it("takes the tile's own trigger path for a tree that claims no press", () => {
      // Weather, Clock and the rest declare no node events, so they go through the tile exactly as a
      // tap does - and in the same order.
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(textNode(1));
      fixture.detectChanges();

      const triggers: string[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.activateFromInput();

      expect(triggers).toEqual(['onTouchStart', 'onTouchEnd', 'onShortPress']);
      expect(handles[0].sent).toEqual([]);
    });

    it('paints the press so the activation is visible on a screen with no pointer', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(buttonRoot(['press']));
      fixture.detectChanges();

      const pressed: boolean[] = [];
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));
      fixture.componentInstance.activateFromInput();

      expect(pressed[0]).toBeTrue();
    });

    it('does nothing at all while the widget is disabled', () => {
      const fixture = createFixture({ widgetId: 'w1', disabled: true });
      handles[0].root.set(buttonRoot(['press']));
      fixture.detectChanges();

      const triggers: string[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.activateFromInput();

      expect(handles[0].sent).toEqual([]);
      expect(triggers).toEqual([]);
    });
  });

  describe('disabled regions', () => {
    const pressNode = (id: string): UiNode =>
      ({ id, type: UiComponents.Button, properties: { [UiComponentProperties.Events]: ['press'] } });
    const disabledRegion = (children: UiNode[]): UiNode => ({
      id: 'region',
      type: UiComponents.Stack,
      properties: { [UiComponentProperties.Modifiers]: { disabled: true } },
      children,
    });

    it('skips a disabled region and activates the enabled node after it', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set({
        id: 'root', type: UiComponents.Stack, children: [disabledRegion([pressNode('inner')]), pressNode('button')],
      } as UiNode);
      fixture.detectChanges();

      fixture.componentInstance.activateFromInput();

      expect(handles[0].sent.map(event => event.nodeId)).toEqual(['button']);
    });

    it("runs the tile's trigger for a keyboard activation of a tree that only drags", () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set({
        id: 'root', type: UiComponents.Modifier, properties: { [UiComponentProperties.Events]: ['drag', 'drag-end'] },
        children: [textNode(1)],
      } as UiNode);
      fixture.detectChanges();
      const triggers: string[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));

      fixture.componentInstance.activateFromInput();

      expect(triggers).toEqual(['onTouchStart', 'onTouchEnd', 'onShortPress']);
      expect(handles[0].sent).toEqual([]);
    });

    it('absorbs an activation, with no press flash, when only a disabled region claims the tile', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set({ id: 'root', type: UiComponents.Stack, children: [disabledRegion([pressNode('inner')])] } as UiNode);
      fixture.detectChanges();
      const triggers: string[] = [];
      const pressed: boolean[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

      fixture.componentInstance.activateFromInput();

      expect(triggers).toEqual([]);
      expect(pressed).toEqual([]);
      expect(handles[0].sent).toEqual([]);
    });

    it('absorbs a pointer press on a tile whose tree holds a disabled region', () => {
      jasmine.clock().install();
      try {
        const fixture = createFixture({ widgetId: 'w1' });
        handles[0].root.set({ id: 'root', type: UiComponents.Stack, children: [disabledRegion([])] } as UiNode);
        fixture.detectChanges();
        const triggers: string[] = [];
        const pressed: boolean[] = [];
        fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
        fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

        const el = tile(fixture);
        el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        jasmine.clock().tick(700);
        el.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
        jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);

        expect(triggers).toEqual([]);
        expect(pressed).toEqual([]);
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  describe('the generic widget event path (ui.slider is its first user)', () => {
    function sliderRoot(events: string[] = ['adjust', 'change']): UiNode {
      return {
        id: 'root',
        type: UiComponents.Slider,
        properties: { [UiComponentProperties.Events]: events },
      };
    }

    it('gives a tile whose tree offers its own interaction no press tint and no press triggers, even ' +
      'where the pointer lands beside the control', () => {
      // The card around a slider - its label, its padding - is not the slider node, so a press there
      // reaches the tile. A slider has no press flows to run, so the tint would promise something that
      // is never going to happen.
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set({
        id: 'root',
        type: UiComponents.Stack,
        children: [sliderRoot()],
      } as UiNode);
      fixture.detectChanges();

      const triggers: string[] = [];
      const pressed: boolean[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

      const el = tile(fixture);
      el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      el.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));

      expect(triggers).toEqual([]);
      expect(pressed).toEqual([]);
    });

    it('lets a tree event raised inside the widget tree reach the session', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(sliderRoot());
      fixture.detectChanges();

      const surface = tile(fixture).querySelector('.widget-slider') as HTMLElement;
      const rect = surface.getBoundingClientRect();
      surface.dispatchEvent(new PointerEvent('pointerdown', {
        bubbles: true, cancelable: true, pointerId: 1,
        clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2,
      }));

      expect(handles[0].sent.length).toBeGreaterThan(0);
      expect(handles[0].sent[0]).toEqual(jasmine.objectContaining({ nodeId: 'root', name: 'adjust' }));
    });

    it('a drag inside the tree fires neither trigger nor pressedChange on the enclosing tile', () => {
      const fixture = createFixture({ widgetId: 'w1' });
      handles[0].root.set(sliderRoot());
      fixture.detectChanges();

      const triggers: string[] = [];
      const pressed: boolean[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));
      fixture.componentInstance.pressedChange.subscribe(p => pressed.push(p));

      const surface = tile(fixture).querySelector('.widget-slider') as HTMLElement;
      const rect = surface.getBoundingClientRect();
      const x = rect.left + rect.width / 2;
      const y = rect.top + rect.height / 2;
      surface.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerId: 1, clientX: x, clientY: y }));
      surface.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, pointerId: 1, clientX: x, clientY: y }));

      expect(triggers).toEqual([]);
      expect(pressed).toEqual([]);
    });
  });
});
