import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import {
  ActionButtonTriggerType,
  GridWidget,
  WIDGET_REFERENCE_BORDER_RADIUS,
  WIDGET_REFERENCE_CELL_SIZE,
  WidgetData,
  UiNode,
  WidgetGridMode,
  WidgetType,
} from '@macro-deck/runtime';

import { IWidgetComponent } from '../../widget-definition.interface';
import { WidgetRegistryService } from '../../services/widget-registry.service';
import { ApiService } from '../../transport';
import { WidgetItemComponent } from './widget-item.component';

@Component({
  selector: 'shared-test-widget',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div class="test-widget">{{ width }}x{{ height }}</div>',
})
class TestWidgetComponent implements IWidgetComponent {
  @Input() data: WidgetData = {};
  tileDrawsBorder = false;
  readonly treeRoot = signal<UiNode | null>(null);
  @Input() width = 0;
  @Input() height = 0;
  @Input() disabled = false;
  @Input() widgetId?: string;
  @Output() pressedChange = new EventEmitter<boolean>();
  @Output() trigger = new EventEmitter<ActionButtonTriggerType>();
  @Output() valueChange = new EventEmitter<Partial<WidgetData>>();
}

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

describe('WidgetItemComponent', () => {
  function createFixture(
    widget: GridWidget,
    cellSize: number,
    gap: number | { gap?: number; mode?: WidgetGridMode } = 12,
  ): ComponentFixture<WidgetItemComponent> {
    const options = typeof gap === 'number' ? { gap } : gap;
    const fixture = TestBed.createComponent(WidgetItemComponent);
    fixture.componentRef.setInput('widget', widget);
    fixture.componentRef.setInput('cellWidth', cellSize);
    fixture.componentRef.setInput('cellHeight', cellSize);
    fixture.componentRef.setInput('gap', options.gap ?? 12);
    fixture.componentRef.setInput('padding', 12);
    fixture.componentRef.setInput('mode', options.mode ?? 'runtime');
    fixture.detectChanges();
    return fixture;
  }

  function hostedWidget(fixture: ComponentFixture<WidgetItemComponent>): TestWidgetComponent {
    return fixture.debugElement.query(el => el.componentInstance instanceof TestWidgetComponent)
      .componentInstance as TestWidgetComponent;
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService',
      ['onNotification', 'onUiSessionTreeUpdated', 'onUiSessionPatched', 'onUiSessionInvalidated', 'onUiSessionClosed', 'openWidgetUiSession', 'closeUiSession', 'onWidgetTypeCatalogChanged']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    apiSpy.onUiSessionTreeUpdated.and.callFake(() => new Subject());
    apiSpy.onUiSessionPatched.and.callFake(() => new Subject());
    apiSpy.onUiSessionInvalidated.and.callFake(() => new Subject());
    apiSpy.onUiSessionClosed.and.callFake(() => new Subject());
    apiSpy.openWidgetUiSession.and.returnValue(Promise.resolve({ success: false } as never));
    apiSpy.closeUiSession.and.returnValue(Promise.resolve({ success: true } as never));
    apiSpy.onWidgetTypeCatalogChanged.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    const registry = TestBed.inject(WidgetRegistryService);
    registry.register({
      type: WidgetType.Weather,
      component: TestWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestWidgetComponent as never),
    });
    registry.register({ type: WidgetType.ActionButton, component: TestWidgetComponent });
  });

  it('scales content by cell size relative to the reference cell size', () => {
    const cellSize = WIDGET_REFERENCE_CELL_SIZE / 2;
    const fixture = createFixture(gridWidget(), cellSize);

    expect(fixture.componentInstance.contentScale).toBe(0.5);

    const content = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.widget-content')!;
    expect(content.style.transform).toBe('scale(0.5)');
    expect(content.style.width).toBe(`${cellSize / 0.5}px`);
    expect(content.style.height).toBe(`${cellSize / 0.5}px`);
    fixture.destroy();
  });

  it('lays out multi-cell content at the reference size so the scaled box matches the real box', () => {
    const fixture = createFixture(gridWidget({ w: 2, h: 2 }), 60, 6);
    const item = fixture.componentInstance;

    expect(item.widgetWidth).toBe(2 * 60 + 6);
    expect(item.contentWidth).toBeCloseTo(2 * WIDGET_REFERENCE_CELL_SIZE + 12, 6);
    expect(item.contentHeight).toBeCloseTo(2 * WIDGET_REFERENCE_CELL_SIZE + 12, 6);
    expect(item.contentWidth * item.contentScale).toBeCloseTo(item.widgetWidth, 6);
    fixture.destroy();
  });

  it('exposes the container-query basis (--wq) as the reference box smaller side', () => {
    const fixture = createFixture(gridWidget(), 60);

    const content = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.widget-content')!;
    expect(content.style.getPropertyValue('--wq')).toBe(`${WIDGET_REFERENCE_CELL_SIZE}px`);
    fixture.destroy();
  });

  it('sets --wq to the smaller reference side for a non-square widget', () => {
    // A 2x1 widget on a 60px cell with a 6px gap is 126x60 real px; at scale 60/120 its reference box
    // is 252x120, so the basis is the reference cell size itself. Worked out from the widget's shape
    // rather than from the component, which would pass for any geometry at all.
    const fixture = createFixture(gridWidget({ w: 2, h: 1 }), 60, 6);

    const content = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.widget-content')!;
    expect(content.style.getPropertyValue('--wq')).toBe(`${WIDGET_REFERENCE_CELL_SIZE}px`);
    fixture.destroy();
  });

  it('renders content corners at the wrapper radius, not double-scaled by the deck scale', () => {
    const contentScale = 0.5;
    const fixture = createFixture(gridWidget(), WIDGET_REFERENCE_CELL_SIZE * contentScale);
    const host = fixture.nativeElement as HTMLElement;
    host.style.setProperty('--widget-radius', '12px');
    host.style.setProperty('--widget-scale', String(contentScale));

    const widget = host.querySelector<HTMLElement>('.widget')!;
    const contentRoot = host.querySelector<HTMLElement>('.test-widget')!;
    contentRoot.style.borderRadius = 'calc(var(--widget-radius, 9.75px) * var(--widget-scale, 1))';

    const wrapperRadius = parseFloat(getComputedStyle(widget).borderTopLeftRadius);
    const contentRadiusRef = parseFloat(getComputedStyle(contentRoot).borderTopLeftRadius);

    expect(wrapperRadius).toBeCloseTo(12 * contentScale, 1);
    expect(contentRadiusRef).toBeCloseTo(12, 1);
    expect(contentRadiusRef * contentScale).toBeCloseTo(wrapperRadius, 1);
    fixture.destroy();
  });

  it('rounds a widget at the built-in default radius while no radius is configured', () => {
    const contentScale = 0.5;
    const fixture = createFixture(gridWidget(), WIDGET_REFERENCE_CELL_SIZE * contentScale);
    const host = fixture.nativeElement as HTMLElement;
    host.style.setProperty('--widget-scale', String(contentScale));

    const widget = host.querySelector<HTMLElement>('.widget')!;
    const radius = parseFloat(getComputedStyle(widget).borderTopLeftRadius);

    expect(WIDGET_REFERENCE_BORDER_RADIUS).toBe(22);
    expect(radius).toBeCloseTo(WIDGET_REFERENCE_BORDER_RADIUS * contentScale, 1);
    fixture.destroy();
  });

  it('passes the reference layout size, not the pixel size, to the widget component', () => {
    const fixture = createFixture(gridWidget(), 60);

    const testWidget = (fixture.nativeElement as HTMLElement).querySelector('.test-widget');
    expect(testWidget?.textContent?.trim())
      .toBe(`${WIDGET_REFERENCE_CELL_SIZE}x${WIDGET_REFERENCE_CELL_SIZE}`);
    fixture.destroy();
  });

  it('falls back to scale 1 while the cell size is unknown', () => {
    const fixture = createFixture(gridWidget(), 0);

    expect(fixture.componentInstance.contentScale).toBe(1);
    fixture.destroy();
  });

  it('passes the widget id to a tree-rendered widget type, so it can open its own UI session', () => {
    const fixture = createFixture(gridWidget({ id: 'w-tree-1' }), 60);

    expect(hostedWidget(fixture).widgetId).toBe('w-tree-1');
    fixture.destroy();
  });

  it('scales the whole wrapper while the hosted widget is pressed', () => {
    const fixture = createFixture(gridWidget({ data: { border: { style: 'static', color: '#fff' } } }), 120);
    const host = fixture.nativeElement as HTMLElement;
    const widget = host.querySelector<HTMLElement>('.widget')!;
    const overlay = host.querySelector<HTMLElement>('shared-widget-border-overlay')!;
    const hosted = hostedWidget(fixture);

    expect(widget.contains(overlay)).toBeTrue();
    expect(widget.classList.contains('pressed')).toBeFalse();

    hosted.pressedChange.emit(true);
    fixture.detectChanges();
    expect(widget.classList.contains('pressed')).toBeTrue();

    hosted.pressedChange.emit(false);
    fixture.detectChanges();
    expect(widget.classList.contains('pressed')).toBeFalse();
    fixture.destroy();
  });

  it('renders the configured widget border via the shared overlay', () => {
    const fixture = createFixture(gridWidget({ data: { border: { style: 'breathing', color: '#ff0000' } } }), 120);

    const ring = fixture.nativeElement.querySelector('shared-widget-border-overlay .ring') as HTMLElement;
    expect(ring.classList).toContain('wb-breathing');
    expect(ring.style.getPropertyValue('--wb-color')).toBe('#ff0000');
    fixture.destroy();
  });

  it('renders no wrapper border without a border config', () => {
    const fixture = createFixture(gridWidget(), 120);

    expect(fixture.nativeElement.querySelector('shared-widget-border-overlay .ring')).toBeNull();
    fixture.destroy();
  });

  // An Action Button's border is resolved host-side per active state and arrives on its `ui.button`
  // root node, not in the stored data - so the tile draws that value and ignores `data.border`,
  // which is what keeps an explicitly `off` state from showing the stored one (issue #895).
  it('draws an action button ring from its tree root, not from the stored border', () => {
    const fixture = createFixture(gridWidget({
      type: WidgetType.ActionButton,
      data: { border: { style: 'static', color: '#ff0000' } } as WidgetData,
    }), 120);
    hostedWidget(fixture).treeRoot.set({
      id: 'btn', type: 'ui.button', properties: { borderStyle: 'comet', borderColor: '#00ff00' },
    });
    fixture.detectChanges();

    const ring = fixture.nativeElement.querySelector('shared-widget-border-overlay .ring') as HTMLElement;
    expect(ring.classList).toContain('wb-comet');
    expect(ring.style.getPropertyValue('--wb-color')).toBe('#00ff00');
    fixture.destroy();
  });

  it('draws no action button ring while its tree has not arrived', () => {
    const fixture = createFixture(gridWidget({
      type: WidgetType.ActionButton,
      data: { border: { style: 'static', color: '#ff0000' } } as WidgetData,
    }), 120);

    expect(fixture.nativeElement.querySelector('shared-widget-border-overlay .ring')).toBeNull();
    fixture.destroy();
  });

  it('draws no action button ring for a state whose border is off', () => {
    const fixture = createFixture(gridWidget({
      type: WidgetType.ActionButton,
      data: { border: { style: 'static', color: '#ff0000' } } as WidgetData,
    }), 120);
    hostedWidget(fixture).treeRoot.set({ id: 'btn', type: 'ui.button', properties: {} });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-border-overlay .ring')).toBeNull();
    fixture.destroy();
  });

  it('renders no editing chrome when nothing decorates the tile', () => {
    // A pinned widget is the strongest case: pinning is deck state a runtime client renders nothing
    // for, because every affordance that would show it belongs to the layout editor.
    const fixture = createFixture(gridWidget({ isPinned: true, pinScope: 'Profile' }), 120);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.widget')).not.toBeNull();
    expect(host.querySelector('.widget-chrome')).toBeNull();
    expect(host.querySelector('.pin-badge')).toBeNull();
    expect(host.querySelector('.edit-overlay')).toBeNull();
    expect(host.querySelector('.resize-handle')).toBeNull();
    fixture.destroy();
  });

  // The read-only client's guard (issue #213): selection is an editing gesture, so a runtime tile
  // must not offer one however the click is modified - and must not grow a way to offer one, either.
  it('treats a modifier-held click as an ordinary press - a client using the deck has no selection to join', () => {
    const fixture = createFixture(gridWidget(), 120);
    const widget = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.widget')!;

    let interacts = 0;
    fixture.componentInstance.interact.subscribe(() => interacts++);

    for (const modifier of ['ctrlKey', 'metaKey', 'shiftKey'] as const) {
      widget.dispatchEvent(new MouseEvent('click', { bubbles: true, [modifier]: true }));
    }
    fixture.detectChanges();

    expect(interacts).toBe(3);
    fixture.destroy();
  });

  describe('the runtime gate', () => {
    function hostedWidget(fixture: ComponentFixture<WidgetItemComponent>): TestWidgetComponent {
      return fixture.debugElement.query(By.directive(TestWidgetComponent)).componentInstance;
    }

    it('keeps the hosted widget live and forwards its triggers in runtime mode', () => {
      const fixture = createFixture(gridWidget(), 120);
      const triggers: ActionButtonTriggerType[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));

      expect(hostedWidget(fixture).disabled).toBeFalse();
      hostedWidget(fixture).trigger.emit('onShortPress');

      expect(triggers).toEqual(['onShortPress']);
      fixture.destroy();
    });

    it('disables the hosted widget and swallows its triggers in layout mode', () => {
      const fixture = createFixture(gridWidget(), 120, { mode: 'layout' });
      const triggers: ActionButtonTriggerType[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));

      expect(hostedWidget(fixture).disabled).toBeTrue();
      hostedWidget(fixture).trigger.emit('onShortPress');

      expect(triggers).toEqual([]);
      fixture.destroy();
    });

    it('follows a live mode change rather than the mode it was created with', () => {
      const fixture = createFixture(gridWidget(), 120, { mode: 'layout' });
      const triggers: ActionButtonTriggerType[] = [];
      fixture.componentInstance.trigger.subscribe(t => triggers.push(t));

      fixture.componentRef.setInput('mode', 'runtime');
      fixture.detectChanges();
      hostedWidget(fixture).trigger.emit('onShortPress');

      expect(hostedWidget(fixture).disabled).toBeFalse();
      expect(triggers).toEqual(['onShortPress']);
      fixture.destroy();
    });

    it('forwards a widget\'s own data change so the host can persist it', () => {
      const fixture = createFixture(gridWidget(), 120);
      const changes: Partial<WidgetData>[] = [];
      fixture.componentInstance.dataChange.subscribe(d => changes.push(d));

      hostedWidget(fixture).valueChange.emit({ label: 'next' });

      expect(changes).toEqual([{ label: 'next' }]);
      fixture.destroy();
    });
  });
});
