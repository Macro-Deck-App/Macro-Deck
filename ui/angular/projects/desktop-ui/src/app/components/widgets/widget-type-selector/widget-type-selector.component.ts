import {
  Component, Output, EventEmitter, ChangeDetectionStrategy, ElementRef, Injector, ViewChild, ViewChildren,
  QueryList, AfterViewInit, OnDestroy, OnInit, afterNextRender, computed, inject, signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  AppStrings,
  WidgetType,
  WIDGET_REFERENCE_CELL_SIZE,
  WIDGET_REFERENCE_BORDER_RADIUS,
  resolveLocalizedText,
} from '@macro-deck/runtime';
import { Subscription } from 'rxjs';

import {
  ModalComponent,
  ButtonComponent,
  InputComponent,
  LocalizationService,
  SegmentedControlComponent,
  SegmentedOption,
  TranslatePipe,
  UiTreeWidgetComponent,
  WidgetTypeCatalogService,
  WidgetTypeFavoritesService,
  WidgetTypeInfo,
  dismissModal,
} from '@shared';

export type WidgetTypeSelectorView = 'grid' | 'list';

export const WIDGET_TYPE_SELECTOR_VIEW_KEY = 'md.widgetTypeSelector.view';

@Component({
  selector: 'app-widget-type-selector',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, InputComponent, SegmentedControlComponent, FormsModule, TranslatePipe,
    UiTreeWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-type-selector.component.html',
  styleUrls: ['./widget-type-selector.component.scss']
})
export class WidgetTypeSelectorComponent implements AfterViewInit, OnDestroy, OnInit {
  private static readonly MaxPreviewSize = 132;

  protected readonly previewRadius = WIDGET_REFERENCE_BORDER_RADIUS;

  protected readonly previewCell = WIDGET_REFERENCE_CELL_SIZE;

  protected readonly previewSize = signal(WIDGET_REFERENCE_CELL_SIZE);

  protected readonly previewScale = signal(1);

  @Output() typeSelected = new EventEmitter<WidgetType>();
  @Output() cancel = new EventEmitter<void>();

  private readonly catalog = inject(WidgetTypeCatalogService);
  private readonly favorites = inject(WidgetTypeFavoritesService);
  private readonly localization = inject(LocalizationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly query = signal('');

  protected readonly view = signal<WidgetTypeSelectorView>(readStoredView());

  protected readonly viewOptions = computed<SegmentedOption[]>(() => [
    { value: 'grid', icon: 'grid', ariaLabel: this.translate(AppStrings.Widgets.TypeSelector.ViewGrid) },
    { value: 'list', icon: 'list', ariaLabel: this.translate(AppStrings.Widgets.TypeSelector.ViewList) },
  ]);

  private readonly collator = computed(() => new Intl.Collator(this.localization.culture(), { sensitivity: 'base' }));

  protected readonly sortedTypes = computed(() => {
    const favorites = this.favorites.ids();
    const collator = this.collator();
    return this.catalog.types()
      .map(type => ({ type, name: this.name(type), favorite: favorites.has(type.id) }))
      .sort((a, b) => Number(b.favorite) - Number(a.favorite)
        || collator.compare(a.name, b.name)
        || (a.type.id < b.type.id ? -1 : a.type.id > b.type.id ? 1 : 0))
      .map(entry => entry.type);
  });

  private readonly matchingIds = computed(() => {
    const needle = fold(this.query().trim());
    const types = this.sortedTypes();
    if (!needle) return new Set(types.map(type => type.id));

    return new Set(types
      .filter(type => [this.name(type), this.description(type), this.providerName(type)]
        .some(text => fold(text).includes(needle)))
      .map(type => type.id));
  });

  protected readonly noMatches = computed(() => this.catalog.types().length > 0 && this.matchingIds().size === 0);

  @ViewChild(ModalComponent) private modal?: ModalComponent;
  @ViewChild('grid') private grid?: ElementRef<HTMLElement>;
  @ViewChildren('previewBox') private previewBoxes?: QueryList<ElementRef<HTMLElement>>;

  private resizeObserver?: ResizeObserver;
  private destroyed = false;
  private cardsAppeared?: Subscription;

  ngOnInit(): void {
    void this.catalog.load();
  }

  protected name(type: WidgetTypeInfo): string {
    return resolveLocalizedText(type.name, this.localization) || type.id;
  }

  protected description(type: WidgetTypeInfo): string {
    return resolveLocalizedText(type.description, this.localization);
  }

  protected providerLabel(type: WidgetTypeInfo): string {
    return type.isPluginProvided === true ? this.providerName(type) : '';
  }

  private providerName(type: WidgetTypeInfo): string {
    if (type.isBuiltIn || !type.providerName) return '';
    return resolveLocalizedText(type.providerName, this.localization);
  }

  protected matches(type: WidgetTypeInfo): boolean {
    return this.matchingIds().has(type.id);
  }

  protected isFavorite(type: WidgetTypeInfo): boolean {
    return this.favorites.ids().has(type.id);
  }

  protected favoriteLabel(type: WidgetTypeInfo): string {
    const key = this.isFavorite(type)
      ? AppStrings.Widgets.TypeSelector.RemoveFavorite
      : AppStrings.Widgets.TypeSelector.AddFavorite;
    return this.translate(key, { name: this.name(type) });
  }

  protected toggleFavorite(type: WidgetTypeInfo, star: HTMLElement): void {
    const container = document.activeElement === star ? star.closest('.widget-types') : null;
    const settled = this.favorites.toggle(type.id);
    if (!container) return;

    // Re-sorting moves the card, and moving a focused node drops focus to the page body.
    this.refocusStar(container, type.id);
    void settled.then(() => {
      if (!this.destroyed) this.refocusStar(container, type.id);
    });
  }

  private refocusStar(container: Element, typeId: string): void {
    afterNextRender(() => {
      if (this.destroyed || document.activeElement !== document.body) return;
      Array.from(container.querySelectorAll<HTMLElement>('.type-favorite'))
        .find(candidate => candidate.dataset['typeId'] === typeId)
        ?.focus();
    }, { injector: this.injector });
  }

  protected setView(value: string): void {
    const view: WidgetTypeSelectorView = value === 'list' ? 'list' : 'grid';
    this.view.set(view);
    afterNextRender(() => this.measure(), { injector: this.injector });
    try {
      localStorage.setItem(WIDGET_TYPE_SELECTOR_VIEW_KEY, view);
    } catch {
    }
  }

  ngAfterViewInit(): void {
    // The catalogue is fetched, so the first card usually appears after this runs - measuring only here
    // would leave every preview at the reference size and never react to a resize.
    const grid = this.grid?.nativeElement;
    if (grid) {
      this.resizeObserver = new ResizeObserver(() => this.measure());
      this.resizeObserver.observe(grid);
    }
    this.measure();
    this.cardsAppeared = this.previewBoxes?.changes.subscribe(() => this.measure());
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.cardsAppeared?.unsubscribe();
    this.resizeObserver?.disconnect();
  }

  selectType(type: WidgetType): void {
    dismissModal(this.modal, () => this.typeSelected.emit(type));
  }

  onCancel(): void {
    dismissModal(this.modal, () => this.cancel.emit());
  }

  private translate(key: string, args?: Record<string, unknown>): string {
    return this.localization.translateKey(key, args);
  }

  private measure(): void {
    const box = this.previewBoxes?.find(candidate => candidate.nativeElement.clientWidth > 0)?.nativeElement;
    if (!box) return;

    const style = getComputedStyle(box);
    const inner = box.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
    const size = Math.min(Math.round(inner), WidgetTypeSelectorComponent.MaxPreviewSize);
    if (size <= 0) return;

    this.previewSize.set(size);
    this.previewScale.set(size / WIDGET_REFERENCE_CELL_SIZE);
  }
}

function readStoredView(): WidgetTypeSelectorView {
  try {
    return localStorage.getItem(WIDGET_TYPE_SELECTOR_VIEW_KEY) === 'list' ? 'list' : 'grid';
  } catch {
    return 'grid';
  }
}

function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase();
}
