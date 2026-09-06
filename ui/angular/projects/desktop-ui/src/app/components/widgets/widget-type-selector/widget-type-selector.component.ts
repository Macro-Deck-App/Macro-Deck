import {
  Component, Output, EventEmitter, ChangeDetectionStrategy, ElementRef, ViewChild, ViewChildren,
  QueryList, AfterViewInit, OnDestroy, OnInit, inject, signal,
} from '@angular/core';

import {
  WidgetType,
  WIDGET_REFERENCE_CELL_SIZE,
  WIDGET_REFERENCE_BORDER_RADIUS,
  resolveLocalizedText,
} from '@macro-deck/runtime';
import { Subscription } from 'rxjs';

import {
  ModalComponent,
  ButtonComponent,
  LocalizationService,
  TranslatePipe,
  UiTreeWidgetComponent,
  WidgetTypeCatalogService,
  WidgetTypeInfo,
  dismissModal,
} from '@shared';

@Component({
  selector: 'app-widget-type-selector',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe, UiTreeWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-type-selector.component.html',
  styleUrls: ['./widget-type-selector.component.scss']
})
export class WidgetTypeSelectorComponent implements AfterViewInit, OnDestroy, OnInit {
  private static readonly MaxPreviewSize = 132;

  protected readonly previewRadius = WIDGET_REFERENCE_BORDER_RADIUS;

  protected readonly previewSize = signal(WIDGET_REFERENCE_CELL_SIZE);

  protected readonly previewScale = signal(1);

  @Output() typeSelected = new EventEmitter<WidgetType>();
  @Output() cancel = new EventEmitter<void>();

  private readonly catalog = inject(WidgetTypeCatalogService);
  private readonly localization = inject(LocalizationService);

  @ViewChild(ModalComponent) private modal?: ModalComponent;
  @ViewChildren('previewBox') private previewBoxes?: QueryList<ElementRef<HTMLElement>>;

  private resizeObserver?: ResizeObserver;
  private observedBox?: HTMLElement;
  private cardsAppeared?: Subscription;

  protected get widgetTypes(): WidgetTypeInfo[] {
    return this.catalog.types();
  }

  ngOnInit(): void {
    void this.catalog.load();
  }

  protected name(type: WidgetTypeInfo): string {
    return resolveLocalizedText(type.name, this.localization) || type.id;
  }

  protected description(type: WidgetTypeInfo): string {
    return resolveLocalizedText(type.description, this.localization);
  }

  ngAfterViewInit(): void {
    // The catalogue is fetched, so the first card usually appears after this runs - measuring only here
    // would leave every preview at the reference size and never react to a resize.
    this.observeFirstCard();
    this.cardsAppeared = this.previewBoxes?.changes.subscribe(() => this.observeFirstCard());
  }

  ngOnDestroy(): void {
    this.cardsAppeared?.unsubscribe();
    this.resizeObserver?.disconnect();
  }

  selectType(type: WidgetType): void {
    dismissModal(this.modal, () => this.typeSelected.emit(type));
  }

  onCancel(): void {
    dismissModal(this.modal, () => this.cancel.emit());
  }

  private observeFirstCard(): void {
    const box = this.previewBoxes?.first?.nativeElement;
    if (!box || box === this.observedBox) return;

    this.resizeObserver?.disconnect();
    this.observedBox = box;
    this.measure(box);
    this.resizeObserver = new ResizeObserver(() => this.measure(box));
    this.resizeObserver.observe(box);
  }

  private measure(box: HTMLElement): void {
    const available = Math.round(box.clientWidth);
    if (available <= 0) return;

    const size = Math.min(available, WidgetTypeSelectorComponent.MaxPreviewSize);

    this.previewSize.set(size);
    this.previewScale.set(size / WIDGET_REFERENCE_CELL_SIZE);
  }
}
