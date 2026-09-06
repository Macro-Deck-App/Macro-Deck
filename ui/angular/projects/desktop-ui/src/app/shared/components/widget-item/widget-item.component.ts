import { Component, Input, Output, EventEmitter, HostBinding, ChangeDetectionStrategy, ChangeDetectorRef, TemplateRef, ViewChild, ViewContainerRef, OnChanges, SimpleChanges, ComponentRef, inject, OnDestroy } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { Subscription } from 'rxjs';
import {
  ActionButtonTriggerType,
  cellOffset,
  contentScaleFor,
  DEFAULT_WIDGET_RENDER_STATE,
  GridWidget,
  spanSize,
  WidgetBorder,
  WidgetGridMode,
  WidgetRenderState,
  widgetTileBorder,
} from '@macro-deck/runtime';
import { IWidgetComponent } from '../../widget-definition.interface';
import { WidgetRegistryService } from '../../services/widget-registry.service';
import { WidgetBorderOverlayComponent } from '../widget-border-overlay/widget-border-overlay.component';

@Component({
  selector: 'shared-widget-item',
  standalone: true,
  imports: [WidgetBorderOverlayComponent, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-item.component.html',
  styleUrls: ['./widget-item.component.scss']
})
export class WidgetItemComponent implements OnChanges, OnDestroy {
  @ViewChild('widgetHost', { read: ViewContainerRef, static: true }) widgetHost!: ViewContainerRef;

  @Input({ required: true }) widget!: GridWidget;
  @Input() renderState: WidgetRenderState = DEFAULT_WIDGET_RENDER_STATE;
  @Input() mode: WidgetGridMode = 'runtime';
  @Input() decoration: TemplateRef<{ $implicit: GridWidget }> | null = null;
  @Input() focused = false;
  @Input() cellWidth = 0;
  @Input() cellHeight = 0;
  @Input() gap = 16;
  @Input() padding = 16;

  @Output() dataChange = new EventEmitter<Partial<GridWidget['data']>>();
  @Output() interact = new EventEmitter<void>();
  @Output() trigger = new EventEmitter<ActionButtonTriggerType>();

  private readonly registry = inject(WidgetRegistryService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private componentRef: ComponentRef<IWidgetComponent> | null = null;

  private valueChangeSub: Subscription | null = null;
  private triggerSub: Subscription | null = null;
  private pressedSub: Subscription | null = null;

  protected isPressed = false;

  protected get isLayoutMode(): boolean {
    return this.mode === 'layout';
  }

  @HostBinding('style.left.px')
  get left(): number {
    return cellOffset(this.widget.x, this.cellWidth, this.gap, this.padding);
  }

  @HostBinding('style.top.px')
  get top(): number {
    return cellOffset(this.widget.y, this.cellHeight, this.gap, this.padding);
  }

  @HostBinding('style.width.px')
  get widgetWidth(): number {
    return spanSize(this.renderState.liveRect?.w ?? this.widget.w, this.cellWidth, this.gap);
  }

  @HostBinding('style.height.px')
  get widgetHeight(): number {
    return spanSize(this.renderState.liveRect?.h ?? this.widget.h, this.cellHeight, this.gap);
  }

  get contentScale(): number {
    return contentScaleFor(this.cellWidth, this.cellHeight);
  }

  get contentWidth(): number {
    return this.widgetWidth / this.contentScale;
  }

  get contentHeight(): number {
    return this.widgetHeight / this.contentScale;
  }

  get contentTransform(): string {
    return `scale(${this.contentScale})`;
  }

  get contentQueryBasis(): string {
    return `${Math.min(this.contentWidth, this.contentHeight)}px`;
  }

  /**
   * Every widget's ring is drawn here, beside the transform-scaled content - an Action Button's
   * included, whose value the host resolves per active state onto the `ui.button` root node instead
   * of into the stored data (see `widgetTileBorder`). The button itself no longer paints one: a ring
   * inside the scaled content is resampled with it and renders visibly thinner than every other
   * widget's (issue #895), which is what `tileDrawsBorder` on the hosted component switches off.
   */
  get widgetBorder(): WidgetBorder | undefined {
    return widgetTileBorder(
      this.widget.type,
      this.widget.data,
      this.componentRef?.instance.treeRoot?.() ?? null,
    );
  }

  @HostBinding('style.transform')
  get displacementTransform(): string | null {
    const rect = this.renderState.liveRect;
    if (!rect || (rect.x === this.widget.x && rect.y === this.widget.y)) {
      return null;
    }
    const dx = cellOffset(rect.x, this.cellWidth, this.gap, 0) - cellOffset(this.widget.x, this.cellWidth, this.gap, 0);
    const dy = cellOffset(rect.y, this.cellHeight, this.gap, 0) - cellOffset(this.widget.y, this.cellHeight, this.gap, 0);
    return `translate(${dx}px, ${dy}px)`;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['widget'] || changes['cellWidth'] || changes['cellHeight'] || changes['mode']
      || changes['renderState']) {
      this.loadWidgetComponent();
    }
  }

  private loadWidgetComponent(): void {
    if (!this.widgetHost || !this.widget) return;

    const componentType = this.registry.getComponent(this.widget.type);
    if (!componentType) return;

    if (!this.componentRef || this.componentRef.componentType !== componentType) {
      this.widgetHost.clear();
      this.valueChangeSub?.unsubscribe();
      this.triggerSub?.unsubscribe();
      this.pressedSub?.unsubscribe();
      this.valueChangeSub = null;
      this.triggerSub = null;
      this.pressedSub = null;
      this.isPressed = false;
      this.componentRef = this.widgetHost.createComponent(componentType);

      const instance = this.componentRef.instance;
      this.valueChangeSub = instance.valueChange?.subscribe(value => {
        if (typeof value === 'object' && value !== null) {
          this.dataChange.emit(value as Partial<GridWidget['data']>);
        }
      }) ?? null;

      // Reads the mode at emit time, not at subscribe time, so flipping into and out of layout
      // editing takes effect on the very next press rather than on the next reload.
      this.triggerSub = instance.trigger?.subscribe(triggerType => {
        if (!this.isLayoutMode) {
          this.trigger.emit(triggerType);
        }
      }) ?? null;

      this.pressedSub = instance.pressedChange?.subscribe(pressed => {
        this.isPressed = pressed;
        this.changeDetector.markForCheck();
      }) ?? null;
    }

    // Every widget type renders as a host-built tree since #750, so the hosted component always
    // wants the widget's id: that is what its session is opened for.
    this.componentRef.setInput('widgetId', this.widget.id);
    this.componentRef.setInput('data', this.widget.data);
    this.componentRef.setInput('width', this.contentWidth);
    this.componentRef.setInput('height', this.contentHeight);
    this.componentRef.setInput('disabled', this.isLayoutMode);
    // A plain assignment rather than `setInput`: only a tree-rendering widget declares this, and it
    // is read once when that tree opens its session, before the first change detection runs here.
    this.componentRef.instance.tileDrawsBorder = true;
    this.componentRef.changeDetectorRef.detectChanges();
  }

  ngOnDestroy(): void {
    this.valueChangeSub?.unsubscribe();
    this.triggerSub?.unsubscribe();
    this.pressedSub?.unsubscribe();
  }

  activateFromInput(): boolean {
    const instance = this.componentRef?.instance;
    if (!instance?.activateFromInput) return false;
    instance.activateFromInput();
    return true;
  }

  onWidgetClick(): void {
    if (this.isLayoutMode) return;
    this.interact.emit();
  }
}
