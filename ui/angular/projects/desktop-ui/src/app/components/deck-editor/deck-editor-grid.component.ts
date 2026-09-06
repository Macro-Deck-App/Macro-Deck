import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, HostListener, Input, OnChanges, OnDestroy, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import {
  ActionButtonTriggerType,
  AppStrings,
  GridRect,
  GridWidget,
  PinScope,
  WidgetRenderState,
  isApplePlatform,
  isSelectionModifierEvent,
} from '@macro-deck/runtime';
import {
  DECK_DRAG_THRESHOLD_PX,
  DeckDragMode,
  DeckDragService,
  DeckMarqueeService,
  DismissibleHintService,
  LocalizationService,
  NoticeModalComponent,
  TranslatePipe,
  WidgetContextMenuAction,
  WidgetContextMenuComponent,
  WidgetContextMenuMode,
  WidgetGhostComponent,
  WidgetGridComponent,
} from '@shared';

const PINNED_MOVE_HINT = 'pinned-widget-locked';
const PIN_SCOPE_HINT = 'pin-scope-middle-click';

interface WidgetGridContextMenu {
  isOpen: boolean;
  x: number;
  y: number;
  mode: WidgetContextMenuMode;
  widget: GridWidget | null;
  cell: { x: number; y: number } | null;
}

function isSelectionModifier(event: MouseEvent | PointerEvent): boolean {
  return isSelectionModifierEvent(event, isApplePlatform());
}

@Component({
  selector: 'app-deck-editor-grid',
  standalone: true,
  imports: [WidgetGridComponent, WidgetGhostComponent, WidgetContextMenuComponent, NoticeModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DeckDragService, DeckMarqueeService],
  templateUrl: './deck-editor-grid.component.html',
  styleUrls: ['./deck-editor-grid.component.scss'],
})
export class DeckEditorGridComponent implements OnChanges, OnDestroy {
  @ViewChild('grid') private gridRef!: WidgetGridComponent;

  @Input() cols = 5;
  @Input() rows = 3;
  @Input() widgets: GridWidget[] = [];
  @Input() background: string | null = '';
  @Input() editMode = false;
  @Input() outerMargin = 16;
  @Input() spacing = 12;
  @Input() borderRadius: number | null = null;
  @Input() canPaste = false;
  @Input() cutWidgetIds: ReadonlySet<string> = new Set();
  @Input() dropTargetCell: { x: number; y: number } | null = null;
  @Input() busyCell: { x: number; y: number } | null = null;
  @Input() selectedWidgetIds: ReadonlySet<string> = new Set();
  @Input() focusedWidgetId: string | null = null;

  @Output() widgetDelete = new EventEmitter<string>();
  @Output() widgetEdit = new EventEmitter<GridWidget>();
  @Output() widgetCopy = new EventEmitter<GridWidget>();
  @Output() widgetCut = new EventEmitter<GridWidget>();
  @Output() widgetPaste = new EventEmitter<{ x: number; y: number }>();
  @Output() widgetExport = new EventEmitter<GridWidget>();
  @Output() widgetPinnedChange = new EventEmitter<{ widget: GridWidget; pinned: boolean; scope?: PinScope }>();
  @Output() widgetsImport = new EventEmitter<{ x: number; y: number }>();
  @Output() widgetDataChange = new EventEmitter<{ widgetId: string; data: Partial<GridWidget['data']> }>();
  @Output() cellClick = new EventEmitter<{ x: number; y: number }>();
  @Output() widgetTrigger = new EventEmitter<{ widget: GridWidget; triggerType: ActionButtonTriggerType }>();
  @Output() layoutCommit = new EventEmitter<ReadonlyMap<string, GridRect>>();
  @Output() widgetSelect = new EventEmitter<{ widget: GridWidget; toggle: boolean; range: boolean }>();
  @Output() marqueeSelect = new EventEmitter<{ rect: GridRect; additive: boolean }>();
  @Output() selectionClear = new EventEmitter<void>();

  readonly drag = inject(DeckDragService);
  readonly marquee = inject(DeckMarqueeService);

  private readonly hints = inject(DismissibleHintService);
  private readonly localization = inject(LocalizationService);

  readonly contextMenu = signal<WidgetGridContextMenu>({
    isOpen: false,
    x: 0,
    y: 0,
    mode: 'widget',
    widget: null,
    cell: null,
  });

  readonly creatingWidgetLabel = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.Grid.CreatingWidget));
  readonly pinnedHintHeading = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.Grid.PinnedHintHeading));
  readonly pinnedHintMessage = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.Grid.PinnedHintMessage));
  readonly pinScopeHintHeading = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.Grid.PinScopeHintHeading));
  readonly pinScopeHintMessage = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.Grid.PinScopeHintMessage));

  readonly pinnedHintVisible = signal(false);

  readonly pinScopeHintVisible = signal(false);

  private readonly cutIds = signal<ReadonlySet<string>>(new Set());
  private readonly widgetList = signal<readonly GridWidget[]>([]);

  readonly renderStates = computed<ReadonlyMap<string, WidgetRenderState>>(() => {
    const displacements = this.drag.displacements();
    const justDropped = this.drag.justDropped();
    const cut = this.cutIds();
    const states = new Map<string, WidgetRenderState>();

    for (const widget of this.widgetList()) {
      const liveRect = displacements.get(widget.id) ?? null;
      const hidden = this.drag.isDragSource(widget.id);
      const dimmed = cut.has(widget.id);
      const landing = justDropped.has(widget.id);
      if (liveRect || hidden || dimmed || landing) {
        states.set(widget.id, { liveRect, hidden, dimmed, landing });
      }
    }
    return states;
  });

  private attachedMarqueeOverlayEl: HTMLElement | null = null;
  private attachedGhostLayerEl: HTMLElement | null = null;
  private readonly droppedSub: Subscription;
  private readonly marqueeCompletedSub: Subscription;

  constructor() {
    this.droppedSub = this.drag.dropped.subscribe(changes => this.layoutCommit.emit(changes));
    this.marqueeCompletedSub = this.marquee.completed.subscribe(({ rect, additive }) =>
      this.marqueeSelect.emit({ rect, additive }));
  }

  ngOnChanges(): void {
    this.cutIds.set(this.cutWidgetIds);
    this.widgetList.set(this.widgets);
  }

  ngOnDestroy(): void {
    this.droppedSub.unsubscribe();
    this.marqueeCompletedSub.unsubscribe();
    this.stopPinnedMoveWatch();
  }

  @ViewChild('marqueeOverlay')
  set marqueeOverlayRef(ref: ElementRef<HTMLElement> | undefined) {
    if (ref) {
      this.attachedMarqueeOverlayEl = ref.nativeElement;
      this.marquee.attachOverlay(ref.nativeElement);
    } else if (this.attachedMarqueeOverlayEl) {
      this.marquee.detachOverlay(this.attachedMarqueeOverlayEl);
      this.attachedMarqueeOverlayEl = null;
    }
  }

  @ViewChild('ghostLayer')
  set ghostLayerRef(ref: ElementRef<HTMLElement> | undefined) {
    if (ref) {
      this.attachedGhostLayerEl = ref.nativeElement;
      this.drag.attachGhost(ref.nativeElement);
    } else if (this.attachedGhostLayerEl) {
      this.drag.detachGhost(this.attachedGhostLayerEl);
      this.attachedGhostLayerEl = null;
    }
  }

  // --- the surface the deck page reaches for, forwarded to the runtime grid ----------------------

  cellAtViewportPoint(clientX: number, clientY: number): { x: number; y: number } | null {
    return this.gridRef?.cellAtViewportPoint(clientX, clientY) ?? null;
  }

  isCellTaken(cellX: number, cellY: number): boolean {
    return this.gridRef?.isCellTaken(cellX, cellY) ?? false;
  }

  get layoutBounds(): { width: number; height: number } | null {
    return this.gridRef?.layoutBounds ?? null;
  }

  // --- tile chrome ------------------------------------------------------------------------------

  pinnedTitle(widget: GridWidget): string {
    return this.localization.translateKey(
      widget.pinScope === 'Subtree'
        ? AppStrings.Widgets.Item.PinnedTitleSubtree
        : AppStrings.Widgets.Item.PinnedTitleProfile,
    );
  }

  onTilePointerDown(widget: GridWidget, event: PointerEvent): void {
    if (event.button !== 0) {
      if (event.button === 1) {
        event.preventDefault();
      }
      return;
    }
    if (isSelectionModifier(event) || event.shiftKey) {
      return;
    }
    if (widget.isPinned) {
      this.watchForPinnedMoveAttempt(event);
      return;
    }
    this.startDrag(widget, event, 'DRAG');
  }

  onResizePointerDown(widget: GridWidget, event: PointerEvent): void {
    event.stopPropagation();
    this.startDrag(widget, event, 'RESIZE');
  }

  private startDrag(widget: GridWidget, event: PointerEvent, mode: DeckDragMode): void {
    const grid = this.gridRef;
    const gridEl = grid?.gridContainer?.nativeElement;
    // The tile element is read off the event rather than through a view query: the chrome is stamped
    // inside the tile, so its own host element is the tile the drag has to carry.
    const sourceEl = (event.currentTarget as HTMLElement).closest('shared-widget-item') as HTMLElement | null;
    if (!grid || !gridEl || !sourceEl) return;

    grid.remeasure();
    const session = {
      widgets: this.widgets,
      cols: this.cols,
      rows: this.rows,
      cell: grid.cellDimensions,
      gridEl,
      sourceEl,
    };

    // A multi-selection (2+) drags as a rigid set (issue #213); a lone selected widget - or one
    // dragged outside any selection - keeps today's single-widget path, including reflow. RESIZE is
    // always single-widget: only a DRAG on a widget that is itself part of the 2+ selection qualifies.
    if (mode === 'DRAG' && this.selectedWidgetIds.size > 1 && this.selectedWidgetIds.has(widget.id)) {
      const members = this.widgets.filter(w => this.selectedWidgetIds.has(w.id));
      if (members.some(w => w.isPinned)) {
        this.onPinnedMoveBlocked();
        return;
      }
      this.drag.pressGroup(event, widget, members, session);
      return;
    }

    this.drag.press(event, widget, mode, session);
  }

  private stopPinnedMoveWatch: () => void = () => undefined;

  private watchForPinnedMoveAttempt(event: PointerEvent): void {
    this.stopPinnedMoveWatch();

    const startX = event.clientX;
    const startY = event.clientY;

    const onMove = (move: PointerEvent): void => {
      if (Math.hypot(move.clientX - startX, move.clientY - startY) < DECK_DRAG_THRESHOLD_PX) return;
      this.stopPinnedMoveWatch();
      this.onPinnedMoveBlocked();
    };

    this.stopPinnedMoveWatch = () => {
      document.removeEventListener('pointermove', onMove);
      document.removeEventListener('pointerup', this.stopPinnedMoveWatch);
      document.removeEventListener('pointercancel', this.stopPinnedMoveWatch);
      this.stopPinnedMoveWatch = () => undefined;
    };

    document.addEventListener('pointermove', onMove);
    document.addEventListener('pointerup', this.stopPinnedMoveWatch);
    document.addEventListener('pointercancel', this.stopPinnedMoveWatch);
  }

  onTileClick(widget: GridWidget, event: MouseEvent): void {
    // First, exactly as before: a click that only ends a drag must not also open the editor.
    if (this.drag.consumeClickSuppression()) return;

    const toggle = isSelectionModifier(event);
    const range = event.shiftKey;
    if (toggle || range) {
      this.widgetSelect.emit({ widget, toggle, range });
      return;
    }

    this.widgetEdit.emit(widget);
  }

  onTileAuxClick(widget: GridWidget, event: MouseEvent): void {
    if (event.button !== 1) return;
    event.preventDefault();
    event.stopPropagation();
    this.onWidgetPinToggle(widget);
  }

  onTileContextMenu(widget: GridWidget, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();

    // Right-clicking a widget outside the current selection replaces it first - otherwise a menu
    // reading "Delete N widgets" could be opened from a widget that isn't one of them (issue #213).
    if (!this.selectedWidgetIds.has(widget.id)) {
      this.widgetSelect.emit({ widget, toggle: false, range: false });
    }

    this.contextMenu.set({ isOpen: true, x: event.clientX, y: event.clientY, mode: 'widget', widget, cell: null });
  }

  stopClick(event: MouseEvent): void {
    event.stopPropagation();
  }

  // --- grid-level pointer handling ---------------------------------------------------------------

  // Bound on this host rather than the runtime grid's container: only a press landing on the container
  // or an empty cell qualifies, so a press inside a tile - handled by the chrome and not re-raised -
  // never reaches here. That is what keeps this and DeckDragService mutually exclusive.
  @HostListener('pointerdown', ['$event'])
  onGridPointerDown(event: PointerEvent): void {
    if (!this.editMode || event.button !== 0) return;
    const target = event.target as HTMLElement;
    const container = this.gridRef?.gridContainer?.nativeElement;
    if (!container || (target !== container && !target.classList.contains('empty-cell'))) return;

    this.gridRef.remeasure();
    this.marquee.press(event, {
      gridEl: container,
      cell: this.gridRef.cellDimensions,
      cols: this.cols,
      rows: this.rows,
    });
  }

  @HostListener('contextmenu', ['$event'])
  onHostContextMenu(event: MouseEvent): void {
    if (!this.editMode) return;
    const target = event.target as HTMLElement;
    const container = this.gridRef?.gridContainer?.nativeElement;
    if (!container || (target !== container && !target.classList.contains('empty-cell'))) return;

    const cell = this.gridRef.cellAtViewportPoint(event.clientX, event.clientY);
    if (!cell || this.gridRef.isCellTaken(cell.x, cell.y)) return;

    event.preventDefault();
    this.contextMenu.set({ isOpen: true, x: event.clientX, y: event.clientY, mode: 'empty', widget: null, cell });
  }

  onCellClick(cell: { x: number; y: number; event: MouseEvent }): void {
    // Swallows the click that follows a completed marquee drag - the release already delivered the
    // selection via `marqueeSelect`, so the same click must not also open the type selector.
    if (this.marquee.consumeClickSuppression()) return;

    const { event } = cell;
    if (this.selectedWidgetIds.size > 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
      this.selectionClear.emit();
      return;
    }

    this.cellClick.emit({ x: cell.x, y: cell.y });
  }

  // --- hints and the context menu ----------------------------------------------------------------

  onPinnedMoveBlocked(): void {
    if (this.hints.isDismissed(PINNED_MOVE_HINT)) return;
    this.pinnedHintVisible.set(true);
  }

  onWidgetPinToggle(widget: GridWidget): void {
    // No scope: the host keeps the widget's current one, which is Profile for anything not already
    // pinned. Deliberate - the shortcut predates scopes and must not quietly mean something else now.
    const pinned = !widget.isPinned;
    this.widgetPinnedChange.emit({ widget, pinned });

    if (pinned && !this.hints.isDismissed(PIN_SCOPE_HINT)) {
      this.pinScopeHintVisible.set(true);
    }
  }

  onPinScopeHintClosed(dontShowAgain: boolean): void {
    this.pinScopeHintVisible.set(false);
    if (dontShowAgain) {
      this.hints.dismiss(PIN_SCOPE_HINT);
    }
  }

  onPinnedHintClosed(dontShowAgain: boolean): void {
    this.pinnedHintVisible.set(false);
    if (dontShowAgain) {
      this.hints.dismiss(PINNED_MOVE_HINT);
    }
  }

  menuSelectionCount(): number {
    const widget = this.contextMenu().widget;
    if (!widget || this.selectedWidgetIds.size <= 1 || !this.selectedWidgetIds.has(widget.id)) {
      return 1;
    }
    return this.selectedWidgetIds.size;
  }

  menuAnyPinned(): boolean {
    const widget = this.contextMenu().widget;
    if (!widget) return false;
    if (this.menuSelectionCount() <= 1) return !!widget.isPinned;
    return this.widgets.some(w => this.selectedWidgetIds.has(w.id) && w.isPinned);
  }

  closeContextMenu(): void {
    this.contextMenu.update(menu => ({ ...menu, isOpen: false }));
  }

  onContextMenuAction(action: WidgetContextMenuAction): void {
    const menu = this.contextMenu();
    this.closeContextMenu();

    if (menu.mode === 'empty') {
      if (action === 'paste' && menu.cell) {
        this.widgetPaste.emit(menu.cell);
      } else if (action === 'import' && menu.cell) {
        this.widgetsImport.emit(menu.cell);
      }
      return;
    }

    const widget = menu.widget;
    if (!widget) return;
    switch (action) {
      case 'edit':
        this.widgetEdit.emit(widget);
        break;
      case 'copy':
        this.widgetCopy.emit(widget);
        break;
      case 'cut':
        this.widgetCut.emit(widget);
        break;
      case 'export':
        this.widgetExport.emit(widget);
        break;
      case 'pin-profile':
        this.widgetPinnedChange.emit({ widget, pinned: true, scope: 'Profile' });
        break;
      case 'pin-subtree':
        this.widgetPinnedChange.emit({ widget, pinned: true, scope: 'Subtree' });
        break;
      case 'unpin':
        this.widgetPinnedChange.emit({ widget, pinned: false });
        break;
      case 'delete':
        this.widgetDelete.emit(widget.id);
        break;
    }
  }
}
