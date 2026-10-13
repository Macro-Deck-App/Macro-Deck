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
import { SelectComponent, SelectOption } from '../../forms/select/select.component';

export type WidgetTypeSelectorView = 'grid' | 'list';

export const WIDGET_TYPE_SELECTOR_VIEW_KEY = 'md.widgetTypeSelector.view';

export type WidgetTypeSortMode = 'default' | 'alphabetical' | 'type';

export const WIDGET_TYPE_SELECTOR_SORT_KEY = 'md.widgetTypeSelector.sort';

const CALENDAR_WIDGET_TYPE = 'app.macro-deck.calendar::calendar';

const DEFAULT_ORDER: readonly string[] = [
  WidgetType.ActionButton,
  WidgetType.Slider,
  WidgetType.Gauges,
  WidgetType.HistoryGraph,
  WidgetType.Battery,
  WidgetType.MusicPlayer,
  WidgetType.Countdown,
  WidgetType.Stopwatch,
  WidgetType.Clock,
  CALENDAR_WIDGET_TYPE,
  WidgetType.Weather,
];

type TypeGroup = 'favorites' | 'builtIn' | 'plugins';

type SelectorEntry =
  | { kind: 'heading'; group: TypeGroup; key: string }
  | { kind: 'type'; type: WidgetTypeInfo; group: TypeGroup; key: string };

const GROUP_HEADINGS: Record<TypeGroup, string> = {
  favorites: AppStrings.Widgets.TypeSelector.GroupFavorites,
  builtIn: AppStrings.Widgets.TypeSelector.GroupBuiltIn,
  plugins: AppStrings.Widgets.TypeSelector.GroupPlugins,
};

@Component({
  selector: 'app-widget-type-selector',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, InputComponent, SegmentedControlComponent, SelectComponent, FormsModule,
    TranslatePipe, UiTreeWidgetComponent],
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

  protected readonly sortMode = signal<WidgetTypeSortMode>(readStoredSortMode());

  protected readonly sortOptions = computed<SelectOption[]>(() => [
    { value: 'default', label: this.translate(AppStrings.Widgets.TypeSelector.SortDefault) },
    { value: 'alphabetical', label: this.translate(AppStrings.Widgets.TypeSelector.SortAlphabetical) },
    { value: 'type', label: this.translate(AppStrings.Widgets.TypeSelector.SortByType) },
  ]);

  private readonly collator = computed(() => new Intl.Collator(this.localization.culture(), { sensitivity: 'base' }));

  protected readonly entries = computed<SelectorEntry[]>(() => {
    const favorites = this.favorites.ids();
    const mode = this.sortMode();
    const compare = mode === 'default' ? compareByDefaultOrder : this.compareByName();
    const groupOf = (type: WidgetTypeInfo): TypeGroup => favorites.has(type.id)
      ? 'favorites'
      : mode === 'type' && type.isPluginProvided === true ? 'plugins' : 'builtIn';
    const groupRank: Record<TypeGroup, number> = { favorites: 0, builtIn: 1, plugins: 2 };

    const types = this.catalog.types()
      .map(type => ({ type, group: groupOf(type) }))
      .sort((a, b) => groupRank[a.group] - groupRank[b.group] || compare(a.type, b.type));

    const entries: SelectorEntry[] = [];
    for (const { type, group } of types) {
      if (mode === 'type' && entries.at(-1)?.group !== group) {
        entries.push({ kind: 'heading', group, key: `heading:${group}` });
      }
      entries.push({ kind: 'type', type, group, key: type.id });
    }
    return entries;
  });

  private readonly matchingIds = computed(() => {
    const needle = fold(this.query().trim());
    const types = this.catalog.types();
    if (!needle) return new Set(types.map(type => type.id));

    return new Set(types
      .filter(type => [this.name(type), this.description(type), this.providerName(type)]
        .some(text => fold(text).includes(needle)))
      .map(type => type.id));
  });

  private readonly visibleGroups = computed(() => {
    const matching = this.matchingIds();
    return new Set(this.entries()
      .filter(entry => entry.kind === 'type' && matching.has(entry.type.id))
      .map(entry => entry.group));
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

  protected groupVisible(group: TypeGroup): boolean {
    return this.visibleGroups().has(group);
  }

  protected groupHeading(group: TypeGroup): string {
    return this.translate(GROUP_HEADINGS[group]);
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

  protected setSortMode(value: string): void {
    const mode = parseSortMode(value);
    this.sortMode.set(mode);
    try {
      localStorage.setItem(WIDGET_TYPE_SELECTOR_SORT_KEY, mode);
    } catch {
    }
  }

  private compareByName(): (a: WidgetTypeInfo, b: WidgetTypeInfo) => number {
    const collator = this.collator();
    return (a, b) => collator.compare(this.name(a), this.name(b)) || compareIds(a.id, b.id);
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

function readStoredSortMode(): WidgetTypeSortMode {
  try {
    return parseSortMode(localStorage.getItem(WIDGET_TYPE_SELECTOR_SORT_KEY));
  } catch {
    return 'default';
  }
}

function parseSortMode(value: string | null): WidgetTypeSortMode {
  return value === 'alphabetical' || value === 'type' ? value : 'default';
}

function compareByDefaultOrder(a: WidgetTypeInfo, b: WidgetTypeInfo): number {
  return defaultRank(a) - defaultRank(b) || compareIds(a.id, b.id);
}

function defaultRank(type: WidgetTypeInfo): number {
  const index = DEFAULT_ORDER.indexOf(type.id);
  if (index >= 0) return index;
  return DEFAULT_ORDER.length + (type.isPluginProvided === true ? 1 : 0);
}

function compareIds(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase();
}
