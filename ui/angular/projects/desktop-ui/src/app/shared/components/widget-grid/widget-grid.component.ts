import { Component, Input, Output, EventEmitter, ElementRef, TemplateRef, ViewChild, ViewChildren, QueryList, AfterViewInit, OnDestroy, OnChanges, SimpleChanges, ChangeDetectionStrategy, ChangeDetectorRef, NgZone, inject } from '@angular/core';

import {
  ActionButtonTriggerType,
  CellDimensions,
  DEFAULT_WIDGET_RENDER_STATE,
  GridMetrics,
  GridRect,
  GridWidget,
  isCellOccupied,
  WIDGET_REFERENCE_GAP,
  WidgetGridMode,
  WidgetRenderState,
} from '@macro-deck/runtime';
import { WidgetItemComponent } from '../widget-item/widget-item.component';

@Component({
  selector: 'shared-widget-grid',
  standalone: true,
  imports: [WidgetItemComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-grid.component.html',
  styleUrls: ['./widget-grid.component.scss']
})
export class WidgetGridComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('wrapper') wrapper!: ElementRef<HTMLElement>;
  @ViewChild('gridContainer') gridContainer!: ElementRef<HTMLElement>;
  @ViewChildren(WidgetItemComponent) private widgetItems!: QueryList<WidgetItemComponent>;

  @Input() cols = 5;
  @Input() rows = 3;
  @Input() widgets: GridWidget[] = [];
  @Input() background: string | null = '';
  @Input() mode: WidgetGridMode = 'runtime';
  @Input() outerMargin = 16;
  @Input() spacing = WIDGET_REFERENCE_GAP;
  @Input() borderRadius: number | null = null;
  @Input() dropTargetCell: { x: number; y: number } | null = null;
  @Input() busyCell: { x: number; y: number } | null = null;
  @Input() busyCellLabel = '';
  @Input() focusedWidgetId: string | null = null;
  @Input() renderStates: ReadonlyMap<string, WidgetRenderState> = new Map();
  @Input() widgetDecoration: TemplateRef<{ $implicit: GridWidget }> | null = null;

  @Output() widgetDataChange = new EventEmitter<{ widgetId: string; data: Partial<GridWidget['data']> }>();
  @Output() cellClick = new EventEmitter<{ x: number; y: number; event: MouseEvent }>();
  @Output() widgetInteract = new EventEmitter<GridWidget>();
  @Output() widgetTrigger = new EventEmitter<{ widget: GridWidget; triggerType: ActionButtonTriggerType }>();

  readonly metrics = new GridMetrics();

  private resizeObserver: ResizeObserver | null = null;

  constructor(
    private cdr: ChangeDetectorRef,
    private ngZone: NgZone
  ) { }

  protected get isLayoutMode(): boolean {
    return this.mode === 'layout';
  }

  get gridWidth(): number {
    return this.metrics.width;
  }

  get gridHeight(): number {
    return this.metrics.height;
  }

  get cellDimensions(): CellDimensions {
    return this.metrics.cell;
  }

  get GAP(): number {
    return this.metrics.gap;
  }

  get PADDING(): number {
    return this.metrics.padding;
  }

  get emptyCells(): number[] {
    return Array(this.metrics.cellCount).fill(0);
  }

  protected renderStateFor(widgetId: string): WidgetRenderState {
    return this.renderStates.get(widgetId) ?? DEFAULT_WIDGET_RENDER_STATE;
  }

  cellRect(index: number): GridRect {
    return this.metrics.cellRect(index);
  }

  isCellOccupied(index: number): boolean {
    const rect = this.metrics.cellRect(index);
    return this.isCellTaken(rect.x, rect.y);
  }

  isCell(cell: { x: number; y: number } | null, index: number): boolean {
    return !!cell && this.metrics.cellIndex(cell.x, cell.y) === index;
  }

  cellAtViewportPoint(clientX: number, clientY: number): { x: number; y: number } | null {
    const container = this.gridContainer?.nativeElement;
    if (!container) {
      return null;
    }

    const bounds = container.getBoundingClientRect();
    return this.metrics.cellAt(clientX - bounds.left, clientY - bounds.top);
  }

  isCellTaken(cellX: number, cellY: number): boolean {
    return isCellOccupied(this.widgets, cellX, cellY);
  }

  ngAfterViewInit(): void {
    this.setupResizeObserver();
    this.calculateDimensions();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['cols'] || changes['rows'] || changes['outerMargin'] || changes['spacing']) {
      // The shape is pushed into the metrics before the view renders, because the template derives
      // its cell count and every cell rect from them. Measuring needs the wrapper and so waits for
      // it; the shape must not.
      this.configureMetrics();
      if (this.wrapper) {
        this.calculateDimensions();
      }
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  private configureMetrics(): void {
    this.metrics.configure({
      cols: this.cols,
      rows: this.rows,
      spacing: this.spacing,
      outerMargin: this.outerMargin,
    });
  }

  private setupResizeObserver(): void {
    this.ngZone.runOutsideAngular(() => {
      this.resizeObserver = new ResizeObserver(() => {
        this.ngZone.run(() => {
          this.calculateDimensions();
          this.cdr.detectChanges();
        });
      });

      if (this.wrapper?.nativeElement) {
        this.resizeObserver.observe(this.wrapper.nativeElement);
      }
    });
  }

  get layoutBounds(): { width: number; height: number } | null {
    const rect = this.wrapper?.nativeElement?.getBoundingClientRect();
    return rect && rect.width > 0 && rect.height > 0 ? { width: rect.width, height: rect.height } : null;
  }

  private calculateDimensions(): void {
    const wrapperEl = this.wrapper?.nativeElement;
    if (!wrapperEl) return;

    this.configureMetrics();
    const rect = wrapperEl.getBoundingClientRect();
    this.metrics.measure(rect.width, rect.height);
  }

  cellLeft(rect: GridRect): number {
    return this.metrics.left(rect);
  }

  cellTop(rect: GridRect): number {
    return this.metrics.top(rect);
  }

  rectWidth(rect: GridRect): number {
    return this.metrics.widthOf(rect);
  }

  rectHeight(rect: GridRect): number {
    return this.metrics.heightOf(rect);
  }

  get contentScale(): number {
    return this.metrics.contentScale;
  }

  onWidgetDataChange(widgetId: string, data: Partial<GridWidget['data']>): void {
    this.widgetDataChange.emit({ widgetId, data });
  }

  onWidgetInteract(widget: GridWidget): void {
    this.widgetInteract.emit(widget);
  }

  onWidgetTrigger(widget: GridWidget, triggerType: ActionButtonTriggerType): void {
    this.widgetTrigger.emit({ widget, triggerType });
  }

  protected onCellClick(index: number, event: MouseEvent): void {
    const rect = this.metrics.cellRect(index);
    this.cellClick.emit({ x: rect.x, y: rect.y, event });
  }

  remeasure(): void {
    this.calculateDimensions();
  }
}
