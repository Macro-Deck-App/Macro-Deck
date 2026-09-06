import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { GridWidget, WidgetData, WidgetGridMode, WidgetType } from '@macro-deck/runtime';

import { IWidgetComponent } from '../../widget-definition.interface';
import { IconImageService } from '../../services/icon-image.service';
import { WidgetRegistryService } from '../../services/widget-registry.service';
import { ApiService } from '../../transport';
import { WidgetGridComponent } from './widget-grid.component';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'onWidgetTypeCatalogChanged']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  apiSpy.onWidgetTypeCatalogChanged.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

@Component({
  selector: 'shared-test-widget',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div></div>',
})
class TestWidgetComponent implements IWidgetComponent {
  @Input() data: WidgetData = {};
  @Input() width = 0;
  @Input() height = 0;
  @Input() disabled = false;
  @Input() widgetId?: string;
  @Output() pressedChange = new EventEmitter<boolean>();
}

function widget(id: string, x: number, y: number): GridWidget {
  return { id, folderId: 'f1', x, y, w: 1, h: 1, type: WidgetType.Weather, data: {} };
}

describe('WidgetGridComponent empty-cell clicks', () => {
  let fixture: ComponentFixture<WidgetGridComponent>;
  let component: WidgetGridComponent;

  function createFixture(mode: WidgetGridMode): void {
    TestBed.configureTestingModule({
      imports: [WidgetGridComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: IconImageService, useValue: {} },
      ],
    });

    fixture = TestBed.createComponent(WidgetGridComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('cols', 2);
    fixture.componentRef.setInput('rows', 1);
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('widgets', []);
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  function clickCell(index: number): void {
    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('.empty-cell');
    (cells[index] as HTMLElement).click();
    fixture.detectChanges();
  }

  it('reports the grid coordinates of the clicked cell in layout mode', () => {
    createFixture('layout');
    const clicks: { x: number; y: number }[] = [];
    component.cellClick.subscribe(cell => clicks.push({ x: cell.x, y: cell.y }));

    // Second cell of a single row, so the coordinates follow from the 2x1 grid alone.
    clickCell(1);

    expect(clicks).toEqual([{ x: 1, y: 0 }]);
  });

  it('hands the host the click itself, so it can read the modifier keys', () => {
    createFixture('layout');
    const events: MouseEvent[] = [];
    component.cellClick.subscribe(cell => events.push(cell.event));

    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('.empty-cell');
    cells[1].dispatchEvent(new MouseEvent('click', { bubbles: true, metaKey: true }));
    fixture.detectChanges();

    expect(events.length).toBe(1);
    expect(events[0].metaKey).toBeTrue();
  });

  it('reports nothing in runtime mode, so a read-only client cannot create a widget', () => {
    createFixture('runtime');
    const clicks: { x: number; y: number }[] = [];
    component.cellClick.subscribe(cell => clicks.push({ x: cell.x, y: cell.y }));

    clickCell(1);

    expect(clicks).toEqual([]);
  });
});

describe('WidgetGridComponent empty-cell placement', () => {
  let fixture: ComponentFixture<WidgetGridComponent>;
  let component: WidgetGridComponent;
  let reset: HTMLStyleElement;

  const COLS = 3;
  const ROWS = 2;
  const SPACING = 12;
  const OUTER_MARGIN = 10;
  // Solves to a 60px cell with a 6px gap and 6px padding: 3*60 + 2*6 + 2*6 = 204, plus 2*10 margin.
  const WRAPPER_W = 224;
  const WRAPPER_H = 2 * 60 + 6 + 2 * 6 + 2 * 10;

  beforeEach(() => {
    // Mirrors the app's global reset, which the shared test target does not load - without it the
    // 1px cell border would fall outside the box the metrics describe.
    reset = document.createElement('style');
    reset.textContent = '* { box-sizing: border-box; }';
    document.head.appendChild(reset);

    TestBed.configureTestingModule({
      imports: [WidgetGridComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: IconImageService, useValue: { getIconUrl: () => null } },
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    TestBed.inject(WidgetRegistryService).register({
      type: WidgetType.Weather,
      component: TestWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestWidgetComponent as never),
    });

    fixture = TestBed.createComponent(WidgetGridComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => reset.remove());

  function render(widgets: GridWidget[]): void {
    fixture.componentRef.setInput('cols', COLS);
    fixture.componentRef.setInput('rows', ROWS);
    fixture.componentRef.setInput('spacing', SPACING);
    fixture.componentRef.setInput('outerMargin', OUTER_MARGIN);
    fixture.detectChanges();

    // A headless wrapper measures 0x0, which would collapse every cell. Feed the real solver a fixed
    // box rather than stubbing it out, so these assertions still cover the arithmetic they exist for.
    spyOn(component.wrapper.nativeElement, 'getBoundingClientRect').and.returnValue(
      { left: 0, top: 0, right: WRAPPER_W, bottom: WRAPPER_H, width: WRAPPER_W, height: WRAPPER_H,
        x: 0, y: 0, toJSON: () => ({}) } as DOMRect);
    component.metrics.measure(WRAPPER_W, WRAPPER_H);

    // Setting the widgets last marks this OnPush component dirty, so the view re-renders against the
    // metrics that were just measured.
    fixture.componentRef.setInput('widgets', widgets);
    fixture.detectChanges();
  }

  function boxOf(el: Element): { left: number; top: number; width: number; height: number } {
    const container = component.gridContainer.nativeElement.getBoundingClientRect();
    const rect = el.getBoundingClientRect();
    return {
      left: Math.round(rect.left - container.left),
      top: Math.round(rect.top - container.top),
      width: Math.round(rect.width),
      height: Math.round(rect.height),
    };
  }

  it('places every cell on its computed metrics when the engine gives the container no grid layout', () => {
    render([]);
    component.gridContainer.nativeElement.style.display = 'block';

    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('.empty-cell');
    expect(cells.length).toBe(COLS * ROWS);

    // 60px cells, 6px gaps, 6px padding: column origins 6/72/138, row origins 6/72.
    expect(boxOf(cells[0])).toEqual({ left: 6, top: 6, width: 60, height: 60 });
    expect(boxOf(cells[1])).toEqual({ left: 72, top: 6, width: 60, height: 60 });
    expect(boxOf(cells[2])).toEqual({ left: 138, top: 6, width: 60, height: 60 });
    expect(boxOf(cells[COLS])).toEqual({ left: 6, top: 72, width: 60, height: 60 });
    expect(boxOf(cells[COLS * ROWS - 1])).toEqual({ left: 138, top: 72, width: 60, height: 60 });
  });

  it('gives a cell the same box as the widget sitting in it', () => {
    render([widget('w1', 2, 1)]);
    component.gridContainer.nativeElement.style.display = 'block';

    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('.empty-cell');
    const widgetEl = (fixture.nativeElement as HTMLElement).querySelector('shared-widget-item');
    expect(widgetEl).not.toBeNull();

    expect(boxOf(cells[COLS * ROWS - 1])).toEqual(boxOf(widgetEl as Element));
  });
});
