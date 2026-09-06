import { Component, HostListener, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { FileOpenService, FolderService } from '../../../services';
import { ActionButtonTriggerType, AppStrings, GridRect, GridWidget, PinScope, WidgetType, WidgetClipboardEntry, collectIconPrefetchTargets, rectsOverlap, canPlaceGroup, LocalizedText, resolveLocalizedText, WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';
import { LocalizationService, ButtonComponent, ModalComponent, ProfileService, WidgetClipboardService, IconPrefetchService, ErrorBannerComponent, ToastService, TranslatePipe, FolderViewHostComponent, FolderViewService } from '@shared';
import { DeckEditorGridComponent } from '../../deck-editor/deck-editor-grid.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { ArchivePreviewModalComponent } from '../../portable/archive-preview-modal/archive-preview-modal.component';
import { ExportOptionsModalComponent, EXPORT_ICONS_TOGGLE } from '../../portable/export-options-modal/export-options-modal.component';
import { ImportPasswordModalComponent } from '../../portable/import-password-modal/import-password-modal.component';
import { ShellDrop, ShellDropTargetDirective } from '../../shell-drop/shell-drop-target.directive';
import { ArchiveImportSession } from '../../../domain/archive-import-session';
import { ShellDropKind } from '../../../domain/shell-drop.util';
import { savedFileDetail } from '../../../services/file-save.service';
import { IconPackService } from '../../../services/icon-pack.service';
import { PortabilityService, PortableExportOptions, ArchiveSource } from '../../../services/portability.service';
import { WidgetSelectionService } from '../../../services/widget-selection.service';
import { FolderNavigationComponent, GridSettingsMenuComponent, WidgetTypeSelectorComponent } from '../../widgets';
import { FolderViewPickerComponent } from '../../widgets/folder-view-picker/folder-view-picker.component';

@Component({
  selector: 'app-deck-page',
  standalone: true,
  imports: [DeckEditorGridComponent, ShellDropTargetDirective, FolderNavigationComponent, GridSettingsMenuComponent, WidgetTypeSelectorComponent, ConfirmationModalComponent, ButtonComponent, ArchivePreviewModalComponent, ExportOptionsModalComponent, ImportPasswordModalComponent, ErrorBannerComponent, TranslatePipe, FolderViewHostComponent, FolderViewPickerComponent, ModalComponent],
  templateUrl: './deck-page.component.html',
  styleUrls: ['./deck-page.component.scss']
})
export class DeckPageComponent {
  private readonly localization = inject(LocalizationService);
  protected readonly folderService = inject(FolderService);
  protected readonly profileService = inject(ProfileService);
  protected readonly clipboard = inject(WidgetClipboardService);
  protected readonly selection = inject(WidgetSelectionService);
  private readonly portability = inject(PortabilityService);
  private readonly iconPacks = inject(IconPackService);
  private readonly iconPrefetch = inject(IconPrefetchService);
  private readonly fileOpen = inject(FileOpenService);
  private readonly toasts = inject(ToastService);
  private readonly router = inject(Router);
  private readonly folderViews = inject(FolderViewService);

  @ViewChild('widgetImportInput') private widgetImportInput?: { nativeElement: HTMLInputElement };
  @ViewChild(DeckEditorGridComponent) private widgetGrid?: DeckEditorGridComponent;

  editMode = signal(true);

  protected readonly exportWidgetId = signal<string | null>(null);
  protected readonly exportToggles = [EXPORT_ICONS_TOGGLE];
  protected readonly importAnchor = signal<{ x: number; y: number } | null>(null);

  protected readonly archiveImport = new ArchiveImportSession(
    this.portability,
    (source, password) => this.importWidgets(source, password),
    () => this.importAnchor.set(null)
  );

  protected readonly isEditingFolderView = signal(false);

  protected readonly editingFolderId = signal<string | null>(null);

  protected readonly editViewId = signal<string>(WIDGET_GRID_VIEW_ID);

  protected readonly editViewConfiguration = signal<string | null>(null);

  protected readonly folderViewName = computed(() => {
    const view = this.folderViews.find(this.folderService.currentViewId());
    return view
      ? resolveLocalizedText(view.name as LocalizedText, this.localization)
      : this.folderService.currentViewId();
  });

  private readonly pageError = signal<string | null>(null);

  protected readonly deckError = computed(() => this.pageError() ?? this.archiveImport.error());

  protected openFolderSettings(folderId?: string): void {
    const folder = folderId
      ? this.folderService.getFolderById(folderId)
      : this.folderService.selectedFolder();
    if (!folder) return;

    this.editingFolderId.set(folder.id);
    this.editViewId.set(folder.viewId);
    this.editViewConfiguration.set(folder.viewConfiguration);
    this.isEditingFolderView.set(true);
  }

  protected cancelFolderSettings(): void {
    this.isEditingFolderView.set(false);
    this.editingFolderId.set(null);
  }

  protected async confirmFolderSettings(): Promise<void> {
    const folderId = this.editingFolderId();
    if (!folderId) return;

    const result = await this.folderService.setFolderView(folderId,
      this.editViewId(),
      this.editViewConfiguration());

    if (!result.success) {
      this.pageError.set(result.error?.message ?? '');
      return;
    }

    this.isEditingFolderView.set(false);
  }

  protected openIntegrations(): void {
    void this.router.navigate(['/integrations']);
  }

  private readonly prefetchScope = computed(() =>
    [this.profileService.selectedProfileId(), ...this.folderService.folders().map(folder => folder.id)].join('|'));

  constructor() {
    effect(() => {
      if (this.profileService.isCurrentProfileLocked()) {
        this.editMode.set(false);
      }
    });

    effect(() => {
      if (!this.editMode()) {
        this.selection.clear();
      }
    });

    effect(() => {
      this.prefetchScope();
      this.prefetchFolderIcons();
    });
  }

  showTypeSelector = signal(false);
  pendingWidgetPosition = signal<{ x: number; y: number } | null>(null);

  showDeleteConfirm = signal(false);
  widgetToDelete = signal<string[] | null>(null);

  protected readonly gridLockNote = computed(() => {
    const constraint = this.profileService.currentGridConstraint();
    if (!constraint?.deviceName || (!constraint.rowsLocked && !constraint.columnsLocked)) {
      return '';
    }

    return this.localization.translateKey(AppStrings.Widgets.GridSettings.FixedByDevice,
      { device: constraint.deviceName });
  });

  protected readonly noEffectNote = computed(() => {
    const deviceName = this.profileService.currentGridConstraint()?.deviceName;
    return deviceName
      ? this.localization.translateKey(AppStrings.Widgets.GridSettings.NoEffectOnDevice, { device: deviceName })
      : '';
  });

  protected readonly deleteConfirmHeading = computed(() =>
    this.localization.translateKey(AppStrings.Deck.DeleteWidgetHeading, { count: this.widgetToDelete()?.length ?? 0 }));

  protected readonly deleteConfirmMessage = computed(() =>
    this.localization.translateKey(AppStrings.Deck.DeleteWidgetMessage, { count: this.widgetToDelete()?.length ?? 0 }));

  private prefetchFolderIcons(): void {
    this.iconPrefetch.prefetch(() => {
      const grid = this.widgetGrid;
      const bounds = grid?.layoutBounds;
      if (!grid || !bounds) {
        return null;
      }

      return collectIconPrefetchTargets(this.folderService.folders(), {
        excludeFolderId: this.folderService.selectedFolderId(),
        viewportWidth: bounds.width,
        viewportHeight: bounds.height,
        outerMargin: grid.outerMargin,
        devicePixelRatio: window.devicePixelRatio || 1,
        resolveGrid: folder => ({
          cols: this.folderService.getEffectiveCols(folder),
          rows: this.folderService.getEffectiveRows(folder),
          spacing: this.folderService.getEffectiveSpacing(folder)
        })
      });
    });
  }

  toggleEditMode(): void {
    if (this.profileService.isCurrentProfileLocked()) {
      return;
    }
    this.editMode.update(v => !v);
  }

  async onColsChange(cols: number | null): Promise<void> {
    await this.resizeGrid(cols, this.folderService.selectedFolder()?.rows ?? null);
  }

  async onRowsChange(rows: number | null): Promise<void> {
    await this.resizeGrid(this.folderService.selectedFolder()?.cols ?? null, rows);
  }

  private async resizeGrid(cols: number | null, rows: number | null): Promise<void> {
    this.pageError.set(null);
    const result = await this.folderService.setGridDimensions(cols, rows);
    if (!result.success) {
      this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.ResizeFailed));
    }
  }

  async onBackgroundChange(background: string): Promise<void> {
    await this.folderService.setBackground(background);
  }

  async onSpacingChange(spacing: number | null): Promise<void> {
    await this.folderService.setWidgetSpacing(spacing);
  }

  async onBorderRadiusChange(borderRadius: number | null): Promise<void> {
    await this.folderService.setWidgetBorderRadius(borderRadius);
  }

  onCellClick(position: { x: number; y: number }): void {
    this.pendingWidgetPosition.set(position);
    this.showTypeSelector.set(true);
  }

  async onWidgetTypeSelected(type: WidgetType): Promise<void> {
    const position = this.pendingWidgetPosition();
    this.closeTypeSelector();
    if (position) {
      const newWidget = await this.folderService.addWidgetAt(position.x, position.y, type);
      if (newWidget) {
        await this.router.navigate(['/deck/widgets', newWidget.id]);
      }
    }
  }

  closeTypeSelector(): void {
    this.showTypeSelector.set(false);
    this.pendingWidgetPosition.set(null);
  }

  async onLayoutCommit(changes: ReadonlyMap<string, GridRect>): Promise<void> {
    this.pageError.set(null);
    const result = await this.folderService.commitWidgetLayout(changes);
    if (!result.success) {
      this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.MoveWidgetFailed));
    }
  }

  // --- Multi-selection (issue #213) ---

  onWidgetSelect(event: { widget: GridWidget; toggle: boolean; range: boolean }): void {
    if (event.range) {
      if (this.selection.has(event.widget.id)) {
        this.selection.deselect(event.widget.id);
        return;
      }

      const widgets = this.folderService.currentWidgets();
      const anchor = widgets.find(w => w.id === this.selection.anchorWidgetId);
      if (anchor) {
        this.selection.selectRange(anchor, event.widget, widgets);
      } else {
        this.selection.selectOnly(event.widget.id);
      }
      return;
    }

    if (event.toggle) {
      this.selection.toggle(event.widget.id);
      return;
    }

    this.selection.selectOnly(event.widget.id);
  }

  onMarqueeSelect(event: { rect: GridRect; additive: boolean }): void {
    const ids = this.folderService.currentWidgets()
      .filter(w => rectsOverlap({ x: w.x, y: w.y, w: w.w, h: w.h }, event.rect))
      .map(w => w.id);
    this.selection.setMany(ids, event.additive);
  }

  onSelectionClear(): void {
    this.selection.clear();
  }

  onWidgetDelete(id: string): void {
    const ids = this.selection.count() > 1 && this.selection.has(id)
      ? [...this.selection.ids()]
      : [id];
    this.widgetToDelete.set(ids);
    this.showDeleteConfirm.set(true);
  }

  async confirmWidgetDelete(): Promise<void> {
    const ids = this.widgetToDelete();
    this.showDeleteConfirm.set(false);
    this.widgetToDelete.set(null);
    if (!ids || ids.length === 0) return;

    this.pageError.set(null);
    if (ids.length > 1) {
      const result = await this.folderService.removeWidgets(ids);
      if (!result.success) {
        this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.DeleteWidgetsFailed));
      }
      return;
    }

    await this.folderService.removeWidget(ids[0]);
  }

  cancelWidgetDelete(): void {
    this.showDeleteConfirm.set(false);
    this.widgetToDelete.set(null);
  }

  onWidgetEdit(widget: GridWidget): void {
    this.selection.clear();
    void this.router.navigate(['/deck/widgets', widget.id]);
  }

  onWidgetCopy(widget: GridWidget): void {
    const folderId = this.folderService.selectedFolderId();
    if (!folderId) return;

    if (this.selection.count() > 1 && this.selection.has(widget.id)) {
      this.clipboard.copyMany(this.selectedWidgets(), folderId);
      return;
    }
    this.clipboard.copy(widget, folderId);
  }

  onWidgetCut(widget: GridWidget): void {
    const folderId = this.folderService.selectedFolderId();
    if (!folderId) return;

    if (this.selection.count() > 1 && this.selection.has(widget.id)) {
      this.clipboard.cutMany(this.selectedWidgets(), folderId);
      return;
    }
    this.clipboard.cut(widget, folderId);
  }

  protected selectedWidgets(): GridWidget[] {
    return this.folderService.currentWidgets().filter(w => this.selection.has(w.id));
  }

  async onWidgetPaste(position: { x: number; y: number }): Promise<void> {
    const entries = this.clipboard.entries();
    if (entries.length === 0) {
      return;
    }

    this.pageError.set(null);
    if (entries.length > 1) {
      const result = await this.folderService.pasteWidgets(entries, position.x, position.y);
      if (!result.success) return;
      await this.clearCutSources(entries, result.data?.replacedWidgetIds);
      return;
    }

    const pasted = await this.folderService.pasteWidget(entries[0], position.x, position.y);
    if (!pasted) return;
    await this.clearCutSources(entries);
  }

  // alreadyReplacedIds are same-folder cut sources the batch create already removed atomically
  // (issue #213); a separate delete for them would either fail with NotFound or race a still-in-flight
  // WidgetsDeletedNotification for the same ids.
  private async clearCutSources(
    entries: readonly WidgetClipboardEntry[],
    alreadyReplacedIds: readonly string[] = []
  ): Promise<void> {
    const allCutOrigins = entries.filter(e => e.isCut && e.origin).map(e => e.origin!);
    if (allCutOrigins.length === 0) return;

    const replaced = new Set(alreadyReplacedIds);
    const cutOrigins = allCutOrigins.filter(o => !replaced.has(o.widgetId));

    if (cutOrigins.length === 1) {
      await this.folderService.removeWidget(cutOrigins[0].widgetId, cutOrigins[0].folderId);
    } else if (cutOrigins.length > 1) {
      await this.folderService.removeWidgets(cutOrigins.map(o => o.widgetId), cutOrigins[0].folderId);
    }

    this.clipboard.clear();
  }

  async onWidgetPinnedChange(event: { widget: GridWidget; pinned: boolean; scope?: PinScope }): Promise<void> {
    this.pageError.set(null);

    if (this.selection.count() > 1 && this.selection.has(event.widget.id)) {
      const result = await this.folderService.setWidgetsPinned([...this.selection.ids()], event.pinned, event.scope);
      if (!result.success) {
        this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.PinWidgetsFailed));
      }
      return;
    }

    const result = await this.folderService.setWidgetPinned(event.widget.id, event.pinned, event.scope);
    if (!result.success) {
      this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.PinWidgetFailed));
    }
  }

  // --- Batch actions (issue #213) ---

  async onBatchPin(event: { scope: PinScope }): Promise<void> {
    this.pageError.set(null);
    const result = await this.folderService.setWidgetsPinned([...this.selection.ids()], true, event.scope);
    if (!result.success) {
      this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.PinWidgetsFailed));
    }
  }

  async onBatchUnpin(): Promise<void> {
    this.pageError.set(null);
    const result = await this.folderService.setWidgetsPinned([...this.selection.ids()], false);
    if (!result.success) {
      this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.UnpinWidgetsFailed));
    }
  }

  onBatchCopy(): void {
    const folderId = this.folderService.selectedFolderId();
    if (!folderId) return;
    this.clipboard.copyMany(this.selectedWidgets(), folderId);
  }

  onBatchCut(): void {
    const folderId = this.folderService.selectedFolderId();
    if (!folderId) return;
    this.clipboard.cutMany(this.selectedWidgets(), folderId);
  }

  onBatchDelete(): void {
    this.widgetToDelete.set([...this.selection.ids()]);
    this.showDeleteConfirm.set(true);
  }

  onBatchClear(): void {
    this.selection.clear();
  }

  // --- Keyboard shortcuts (issue #213) ---

  @HostListener('document:keydown', ['$event'])
  protected onKeyDown(event: KeyboardEvent): void {
    if (!this.editMode()) return;
    if (this.showTypeSelector() || this.showDeleteConfirm() || this.exportWidgetId()
      || this.archiveImport.summary() || this.archiveImport.passwordVisible() || ModalComponent.isAnyOpen()) {
      return;
    }

    const isEditable = event.target instanceof HTMLElement
      && (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA'
        || event.target.isContentEditable);
    if (isEditable) return;

    if (event.key === 'Escape') {
      if (!this.widgetGrid?.drag.isActive() && !this.widgetGrid?.marquee.isActive() && this.selection.hasSelection()) {
        this.selection.clear();
      }
      return;
    }

    if ((event.key === 'Delete' || event.key === 'Backspace') && this.selection.hasSelection()) {
      event.preventDefault();
      this.widgetToDelete.set([...this.selection.ids()]);
      this.showDeleteConfirm.set(true);
      return;
    }

    const isMod = event.ctrlKey || event.metaKey;
    if (!isMod) return;

    if (event.key.toLowerCase() === 'a') {
      event.preventDefault();
      this.selection.selectAll(this.folderService.currentWidgets());
      return;
    }

    if ((event.key.toLowerCase() === 'c' || event.key.toLowerCase() === 'x') && this.selection.hasSelection()) {
      event.preventDefault();
      const widgets = this.selectedWidgets();
      if (widgets.length === 0) return;

      const folderId = this.folderService.selectedFolderId();
      if (!folderId) return;

      if (event.key.toLowerCase() === 'x') {
        this.clipboard.cutMany(widgets, folderId);
      } else {
        this.clipboard.copyMany(widgets, folderId);
      }
      return;
    }

    if (event.key.toLowerCase() === 'v') {
      event.preventDefault();
      void this.pasteAtOriginOrNearestFit();
    }
  }

  private async pasteAtOriginOrNearestFit(): Promise<void> {
    const entries = this.clipboard.entries();
    if (entries.length === 0) return;

    const folder = this.folderService.selectedFolder();
    if (!folder) return;

    const cols = this.folderService.currentCols();
    const rows = this.folderService.currentRows();
    const excludeIds = new Set(
      entries.filter(e => e.isCut && e.origin?.folderId === folder.id).map(e => e.origin!.widgetId)
    );
    const occupied: GridRect[] = this.folderService.currentWidgets()
      .filter(w => !excludeIds.has(w.id))
      .map(w => ({ x: w.x, y: w.y, w: w.w, h: w.h }));

    const anchor = this.findGroupAnchor(entries, cols, rows, occupied);
    if (!anchor) {
      this.pageError.set(this.localization.translateKey(AppStrings.Deck.PasteNoFit));
      return;
    }

    this.pageError.set(null);
    if (entries.length > 1) {
      const result = await this.folderService.pasteWidgets(entries, anchor.x, anchor.y);
      if (result.success) {
        await this.clearCutSources(entries, result.data?.replacedWidgetIds);
      }
      return;
    }

    const pasted = await this.folderService.pasteWidget(entries[0], anchor.x, anchor.y);
    if (pasted) {
      await this.clearCutSources(entries);
    }
  }

  private findGroupAnchor(
    entries: readonly WidgetClipboardEntry[],
    cols: number,
    rows: number,
    occupied: readonly GridRect[]
  ): { x: number; y: number } | null {
    const fits = (x: number, y: number): boolean =>
      canPlaceGroup(entries.map(e => ({ x: x + e.dx, y: y + e.dy, w: e.w, h: e.h })), cols, rows, occupied);

    const original = this.clipboard.originAnchor();
    if (original && fits(original.x, original.y)) {
      return original;
    }

    for (let y = 0; y < rows; y++) {
      for (let x = 0; x < cols; x++) {
        if (fits(x, y)) return { x, y };
      }
    }
    return null;
  }

  onWidgetExport(widget: GridWidget): void {
    this.pageError.set(null);
    this.exportWidgetId.set(widget.id);
  }

  async onExportConfirmed(options: PortableExportOptions): Promise<void> {
    const widgetId = this.exportWidgetId();
    const folderId = this.folderService.selectedFolderId();
    this.exportWidgetId.set(null);
    if (!widgetId || !folderId) {
      return;
    }

    const result = await this.portability.exportWidgets(folderId, [widgetId], options);
    if (!result.ok) {
      this.pageError.set(result.error ?? this.localization.translateKey(AppStrings.Deck.ExportFailed));
      return;
    }
    if (!result.canceled) {
      this.toasts.show(this.localization.translateKey(AppStrings.Deck.WidgetExported), { detail: savedFileDetail(result) });
    }
  }

  cancelExport(): void {
    this.exportWidgetId.set(null);
  }

  onWidgetsImport(anchor: { x: number; y: number }): void {
    this.pageError.set(null);
    this.importAnchor.set(anchor);
    this.widgetImportInput?.nativeElement.click();
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (file) {
      await this.archiveImport.beginFile(file);
    }
  }

  protected readonly deckDropKinds: readonly ShellDropKind[] = ['iconPack', 'widgets', 'application'];

  protected readonly dropTargetCell = signal<{ x: number; y: number } | null>(null);

  protected readonly creatingWidgetCell = signal<{ x: number; y: number } | null>(null);

  protected readonly acceptDeckDrop = (kind: ShellDropKind, at: { x: number; y: number }): boolean => {
    if (kind !== 'widgets' && kind !== 'application') {
      return true;
    }

    const grid = this.widgetGrid;
    const cell = grid?.cellAtViewportPoint(at.x, at.y);
    return !!grid && !!cell && !grid.isCellTaken(cell.x, cell.y);
  };

  private readonly claimOpenedWidgets = effect(() => {
    if (!this.fileOpen.pending().some(entry => entry.kind === 'widgets')) {
      return;
    }
    if (!this.folderService.selectedFolderId()) {
      return;
    }

    const opened = this.fileOpen.claim('widgets');
    if (opened) {
      this.importAnchor.set({ x: 0, y: 0 });
      void this.archiveImport.beginPath(opened.path);
    }
  });

  onShellDropHover(drop: ShellDrop | null): void {
    const cell = drop && drop.kind !== 'iconPack'
      ? this.widgetGrid?.cellAtViewportPoint(drop.x, drop.y) ?? null
      : null;

    const current = this.dropTargetCell();
    if (cell?.x !== current?.x || cell?.y !== current?.y) {
      this.dropTargetCell.set(cell);
    }
  }

  async onShellDropped(drop: ShellDrop): Promise<void> {
    this.pageError.set(null);
    this.dropTargetCell.set(null);

    if (drop.kind === 'application') {
      await this.createApplicationWidget(drop);
      return;
    }

    if (drop.kind === 'iconPack') {
      const name = await this.iconPacks.restoreFromPath(drop.path);
      if (name) {
        this.toasts.show(this.localization.translateKey(AppStrings.Deck.IconPackImported), { detail: name });
      } else {
        this.pageError.set(this.localization.translateKey(AppStrings.Deck.IconPackImportFailed));
      }
      return;
    }

    const cell = this.widgetGrid?.cellAtViewportPoint(drop.x, drop.y);
    if (!cell) {
      return;
    }

    this.importAnchor.set(cell);
    await this.archiveImport.beginPath(drop.path);
  }

  private async createApplicationWidget(drop: ShellDrop): Promise<void> {
    const cell = this.widgetGrid?.cellAtViewportPoint(drop.x, drop.y);
    if (!cell) {
      return;
    }

    this.creatingWidgetCell.set(cell);
    try {
      const result = await this.folderService.addWidgetFromApplication(cell.x, cell.y, drop.path);
      if (!result.success) {
        this.pageError.set(result.error?.message || this.localization.translateKey(AppStrings.Deck.ApplicationAddFailed));
      }
    } finally {
      this.creatingWidgetCell.set(null);
    }
  }

  cancelImport(): void {
    this.archiveImport.cancel();
    this.importAnchor.set(null);
  }

  dismissDeckError(): void {
    this.pageError.set(null);
    this.archiveImport.dismissError();
  }

  private importWidgets(source: ArchiveSource, password?: string) {
    const anchor = this.importAnchor();
    const folderId = this.folderService.selectedFolderId();
    if (!anchor || !folderId) {
      return Promise.resolve({ status: 'error' as const, message: this.localization.translateKey(AppStrings.Deck.ImportNoFolder) });
    }

    return this.portability.importWidgets(folderId, anchor.x, anchor.y, source, password);
  }

  onWidgetDataChange(event: { widgetId: string; data: Partial<GridWidget['data']> }): void {
    void this.folderService.updateWidgetRuntimeData(event.widgetId, event.data);
  }

  async onWidgetTrigger(event: { widget: GridWidget; triggerType: ActionButtonTriggerType }): Promise<void> {
    await this.folderService.executeActionButtonTrigger(event.widget, event.triggerType);
  }
}
