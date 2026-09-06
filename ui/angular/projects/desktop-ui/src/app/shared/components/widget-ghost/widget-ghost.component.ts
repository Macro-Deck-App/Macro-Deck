import {
  ChangeDetectionStrategy,
  Component,
  ComponentRef,
  HostBinding,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  ViewChild,
  ViewContainerRef,
  inject,
} from '@angular/core';

import { GridWidget, WidgetBorder, widgetTileBorder } from '@macro-deck/runtime';
import { IWidgetComponent } from '../../widget-definition.interface';
import { WidgetRegistryService } from '../../services/widget-registry.service';
import { WidgetBorderOverlayComponent } from '../widget-border-overlay/widget-border-overlay.component';

@Component({
  selector: 'shared-widget-ghost',
  standalone: true,
  imports: [WidgetBorderOverlayComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-ghost.component.html',
  styleUrls: ['./widget-ghost.component.scss'],
})
export class WidgetGhostComponent implements OnChanges, OnDestroy {
  @ViewChild('ghostHost', { read: ViewContainerRef, static: true }) ghostHost!: ViewContainerRef;

  @Input({ required: true }) widget!: GridWidget;
  @Input() widthPx = 0;
  @Input() heightPx = 0;
  @Input() contentScale = 1;
  @Input() offsetX = 0;
  @Input() offsetY = 0;
  @Input() settling = false;

  private readonly registry = inject(WidgetRegistryService);
  private componentRef: ComponentRef<IWidgetComponent> | null = null;

  /** Drawn beside the scaled content like the tile's - see `WidgetItemComponent.widgetBorder`. */
  get widgetBorder(): WidgetBorder | undefined {
    return widgetTileBorder(
      this.widget.type,
      this.widget.data,
      this.componentRef?.instance.treeRoot?.() ?? null,
    );
  }

  @HostBinding('style.width.px')
  get hostWidth(): number {
    return this.widthPx;
  }

  @HostBinding('style.height.px')
  get hostHeight(): number {
    return this.heightPx;
  }

  @HostBinding('style.left.px')
  get hostLeft(): number {
    return this.offsetX;
  }

  @HostBinding('style.top.px')
  get hostTop(): number {
    return this.offsetY;
  }

  @HostBinding('class.settling')
  get hostSettling(): boolean {
    return this.settling;
  }

  @HostBinding('style.--widget-scale')
  get hostWidgetScale(): number {
    return this.contentScale > 0 ? this.contentScale : 1;
  }

  @HostBinding('style.--deck-scale')
  get hostDeckScale(): number {
    return this.hostWidgetScale;
  }

  get contentWidth(): number {
    return this.contentScale > 0 ? this.widthPx / this.contentScale : this.widthPx;
  }

  get contentHeight(): number {
    return this.contentScale > 0 ? this.heightPx / this.contentScale : this.heightPx;
  }

  get contentTransform(): string {
    return `scale(${this.contentScale > 0 ? this.contentScale : 1})`;
  }

  get contentQueryBasis(): string {
    return `${Math.min(this.contentWidth, this.contentHeight)}px`;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['widget'] || changes['widthPx'] || changes['heightPx'] || changes['contentScale']) {
      this.loadWidgetComponent();
    }
  }

  ngOnDestroy(): void {
    this.ghostHost?.clear();
    this.componentRef = null;
  }

  private loadWidgetComponent(): void {
    if (!this.ghostHost || !this.widget) return;

    const componentType = this.registry.getComponent(this.widget.type);
    if (!componentType) return;

    if (!this.componentRef || this.componentRef.componentType !== componentType) {
      this.ghostHost.clear();
      this.componentRef = this.ghostHost.createComponent(componentType);
    }

    this.componentRef.setInput('widgetId', this.widget.id);
    this.componentRef.setInput('ghost', true);
    this.componentRef.setInput('data', this.widget.data);
    this.componentRef.setInput('width', this.contentWidth);
    this.componentRef.setInput('height', this.contentHeight);
    this.componentRef.setInput('disabled', true);
    // A plain assignment rather than `setInput`: only a tree-rendering widget declares this, and it
    // is read once when that tree opens its session, before the first change detection runs here.
    this.componentRef.instance.tileDrawsBorder = true;
    this.componentRef.changeDetectorRef.detectChanges();
  }
}
