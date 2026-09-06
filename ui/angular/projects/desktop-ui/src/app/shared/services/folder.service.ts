import { Injectable, signal, computed, effect, inject, untracked } from '@angular/core';
import {
  ActionButtonTriggerType,
  ActionFlow,
  AppStrings,
  canPlaceGroup,
  collectDisplayedWidgets,
  computePasteRect,
  defaultWidgetData,
  type ExecuteActionButtonTriggerResponse,
  findFlowForTrigger,
  Folder as DomainFolder,
  FolderContextMenu,
  type FolderCreatedEvent,
  type FolderDeletedEvent,
  folderFromWire,
  FolderMoveDirection,
  FolderMoveRequest,
  type FolderNavigationEvent,
  type FoldersReorderedEvent,
  type FolderUpdatedEvent,
  GridRect,
  GridWidget,
  type IpcFolder,
  type IpcFolderPlacement,
  type IpcWidget,
  type IpcWidgetType,
  isFailureExecutionStatus,
  isWidgetGridView,
  type LocalizedText,
  parseWidgetData,
  PinScope,
  Profile,
  resolveLocalDeckNavigation,
  resolveLocalizedText,
  type Result,
  serializeWidgetData,
  type SetWidgetPinnedRequest,
  type SetWidgetsPinnedRequest,
  WIDGET_GRID_VIEW_ID,
  WIDGET_REFERENCE_GAP,
  WidgetClipboardEntry,
  type WidgetCreatedEvent,
  WidgetData,
  type WidgetDeletedEvent,
  widgetFromWire,
  type WidgetPositionsUpdatedEvent,
  type WidgetsCreatedEvent,
  type WidgetsDeletedEvent,
  type WidgetsUpdatedEvent,
  WidgetType as DomainWidgetType,
  type WidgetUpdatedEvent,
} from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';
import { WidgetRegistryService } from './widget-registry.service';
import { ProfileService } from './profile.service';
import { ActionExecutionService } from './action-execution.service';
import { ToastService } from './toast.service';

interface FolderLoad {
  profileId: string | null;
  createdFolders: Map<string, DomainFolder>;
  updatedFolders: Map<string, DomainFolder>;
  deletedFolderIds: Set<string>;
  reorderedPlacements: Map<string, IpcFolderPlacement>;
  promise: Promise<void>;
}

@Injectable({
  providedIn: 'root'
})
export class FolderService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly widgetRegistry = inject(WidgetRegistryService);
  private readonly profileService = inject(ProfileService);
  private readonly actionExecution = inject(ActionExecutionService);
  private readonly toasts = inject(ToastService);

  private readonly DEFAULT_COLS = 5;
  private readonly DEFAULT_ROWS = 3;
  private readonly DEFAULT_BACKGROUND = '';
  private readonly LEGACY_DEFAULT_BACKGROUND = 'rgba(18, 18, 18, 0.6)';

  readonly folders = signal<DomainFolder[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly selectedFolderId = signal<string | null>(null);

  private loadedProfileId: string | null = null;
  private activeFolderLoad: FolderLoad | null = null;

  constructor() {
    this.subscribeToEvents();

    effect(() => {
      const profileId = this.profileService.selectedProfileId();
      if (profileId && profileId !== this.loadedProfileId) {
        void this.loadFolders(profileId);
      }
    });
  }

  private subscribeToEvents(): void {
    this.api.onNotification<FolderCreatedEvent>('FolderCreatedEvent').subscribe(event => {
      if (event.folder) {
        const folder = this.mapIpcFolder(event.folder);
        if (!this.isInActiveFolderScope(event.folder.profileId)) return;

        const activeLoad = this.activeFolderLoad;
        if (activeLoad && (activeLoad.profileId === null || activeLoad.profileId === event.folder.profileId)) {
          activeLoad.createdFolders.set(folder.id, folder);
          activeLoad.deletedFolderIds.delete(folder.id);
        }

        if (!activeLoad || activeLoad.profileId === this.loadedProfileId) {
          this.addCreatedFolder(folder);
        }
      }
    });

    this.api.onNotification<FolderUpdatedEvent>('FolderUpdatedEvent').subscribe(event => {
      if (event.folder) {
        const updated = this.mapIpcFolder(event.folder);
        if (!this.isInActiveFolderScope(event.folder.profileId)) return;

        const activeLoad = this.activeFolderLoad;
        if (activeLoad && (activeLoad.profileId === null || activeLoad.profileId === event.folder.profileId)) {
          activeLoad.updatedFolders.set(updated.id, updated);
        }

        if (activeLoad && activeLoad.profileId !== this.loadedProfileId) return;
        this.folders.update(folders =>
          folders.map(f => f.id === updated.id ? { ...updated, isExpanded: f.isExpanded } : f)
        );
        if (updated.isDefault && updated.parentId === null && this.selectedFolderId() === null) {
          this.selectedFolderId.set(updated.id);
        }
      }
    });

    this.api.onNotification<FolderDeletedEvent>('FolderDeletedEvent').subscribe(event => {
      if (event.folderId) {
        const activeLoad = this.activeFolderLoad;
        if (activeLoad) {
          activeLoad.deletedFolderIds.add(event.folderId);
          activeLoad.createdFolders.delete(event.folderId);
          activeLoad.updatedFolders.delete(event.folderId);
        }

        // FolderDeletedEvent intentionally only transports an id. While replacing one profile tree
        // with another, an unrelated deletion must not mutate the old tree shown behind the loader.
        if (activeLoad && activeLoad.profileId !== this.loadedProfileId) return;

        this.navHistory = this.navHistory.filter(entry => entry.folderId !== event.folderId);
        this.navDepth.set(this.navHistory.length);

        const remaining = this.folders().filter(folder => folder.id !== event.folderId);
        this.folders.set(remaining);
        if (this.selectedFolderId() === event.folderId) {
          this.selectKnownStartOrReload(remaining);
        }
      }
    });

    this.api.onNotification<FoldersReorderedEvent>('FoldersReorderedEvent').subscribe(event => {
      if (!event.folders?.length || !this.isInActiveFolderScope(event.profileId)) return;

      const activeLoad = this.activeFolderLoad;
      if (activeLoad && (activeLoad.profileId === null || activeLoad.profileId === event.profileId)) {
        for (const placement of event.folders) {
          activeLoad.reorderedPlacements.set(placement.id, placement);
        }
      }

      if (activeLoad && activeLoad.profileId !== this.loadedProfileId) return;
      this.applyCanonicalPlacements(event.folders);
    });

    this.api.onNotification<WidgetCreatedEvent>('WidgetCreatedEvent').subscribe(event => {
      if (event.folderId && event.widget) {
        const widget = this.mapIpcWidget(event.widget, event.folderId);
        this.folders.update(folders =>
          folders.map(f => f.id === event.folderId
            ? { ...f, widgets: f.widgets.some(w => w.id === widget.id) ? f.widgets : [...f.widgets, widget] }
            : f
          )
        );
      }
    });

    this.api.onNotification<WidgetUpdatedEvent>('WidgetUpdatedEvent').subscribe(event => {
      if (event.folderId && event.widget) {
        const widget = this.mapIpcWidget(event.widget, event.folderId);
        this.folders.update(folders =>
          folders.map(f => f.id === event.folderId
            ? {
                ...f,
                // A push that carries no data (a pin flag flip) must not blank what we already have.
                widgets: f.widgets.map(w => w.id === widget.id
                  ? { ...widget, data: event.widget.data ? widget.data : w.data }
                  : w
                )
              }
            : f
          )
        );
      }
    });

    this.api.onNotification<WidgetPositionsUpdatedEvent>('WidgetPositionsUpdatedEvent').subscribe(event => {
      if (!event.folderId || !event.widgets?.length) return;
      const rectsById = new Map(event.widgets.map(w => [
        w.id,
        { x: w.positionX, y: w.positionY, w: w.width, h: w.height }
      ]));
      this.folders.update(folders =>
        folders.map(f => f.id === event.folderId
          ? {
              ...f,
              widgets: f.widgets.map(w => {
                const rect = rectsById.get(w.id);
                return rect ? { ...w, ...rect } : w;
              })
            }
          : f
        )
      );
    });

    this.api.onNotification<WidgetDeletedEvent>('WidgetDeletedEvent').subscribe(event => {
      if (event.folderId && event.widgetId) {
        this.folders.update(folders =>
          folders.map(f => f.id === event.folderId
            ? { ...f, widgets: f.widgets.filter(w => w.id !== event.widgetId) }
            : f
          )
        );
      }
    });

    // Batched echo of a create/paste-group batch (issue #213). Appends every widget not already
    // present - mirrors WidgetCreatedEvent's dedupe, just over the whole list at once.
    this.api.onNotification<WidgetsCreatedEvent>('WidgetsCreatedEvent').subscribe(event => {
      if (!event.folderId || !event.widgets?.length) return;
      const created = event.widgets.map(w => this.mapIpcWidget(w, event.folderId));
      this.folders.update(folders =>
        folders.map(f => f.id === event.folderId
          ? { ...f, widgets: [...f.widgets, ...created.filter(w => !f.widgets.some(existing => existing.id === w.id))] }
          : f
        )
      );
    });

    // Batched echo of a pin/unpin/re-scope batch (issue #213). Same per-widget merge rule as the
    // single WidgetUpdatedEvent handler: a push carrying no data must not blank what we already have.
    this.api.onNotification<WidgetsUpdatedEvent>('WidgetsUpdatedEvent').subscribe(event => {
      if (!event.folderId || !event.widgets?.length) return;
      const updatedById = new Map(event.widgets.map(w => [w.id, w]));
      this.folders.update(folders =>
        folders.map(f => f.id === event.folderId
          ? {
              ...f,
              widgets: f.widgets.map(w => {
                const updated = updatedById.get(w.id);
                if (!updated) return w;
                const mapped = this.mapIpcWidget(updated, event.folderId);
                return { ...mapped, data: updated.data ? mapped.data : w.data };
              })
            }
          : f
        )
      );
    });

    // Batched echo of a delete batch (issue #213): one filter over the whole id set.
    this.api.onNotification<WidgetsDeletedEvent>('WidgetsDeletedEvent').subscribe(event => {
      if (!event.folderId || !event.widgetIds?.length) return;
      const deletedIds = new Set(event.widgetIds);
      this.folders.update(folders =>
        folders.map(f => f.id === event.folderId
          ? { ...f, widgets: f.widgets.filter(w => !deletedIds.has(w.id)) }
          : f
        )
      );
    });

    this.api.onNotification<FolderNavigationEvent>('FolderNavigationEvent').subscribe(event => {
      this.handleNavigation(event);
    });

    // One report per actual folder change, from the single signal every navigation path ends at -
    // the folder tree, a deck-navigation action resolved locally, a host push, a profile switch.
    // The host cannot infer this: it only ever *asks* a client to navigate, and the common case is
    // resolved entirely client-side and never reaches it. Fire and forget; a failed report costs
    // nothing but a missed "Folder Changed" event.
    effect(() => {
      const folderId = this.selectedFolderId();
      if (!folderId || folderId === this.lastReportedFolderId) return;
      this.lastReportedFolderId = folderId;
      // Echo the pending device-targeted navigation's token only when it is still this folder -
      // a faster subsequent navigation must not attach a stale token to the wrong report.
      const pending = this.pendingNavigation;
      const token = pending?.folderId === folderId ? pending.token : undefined;
      this.pendingNavigation = null;
      this.api.reportFolderChanged(folderId, token);
    });

    effect(() => {
      if (this.api.connectionStateSignal() !== 'connected') return;
      const folderId = untracked(this.selectedFolderId);
      if (folderId) {
        this.api.reportFolderChanged(folderId, undefined, true);
      }
    });
  }

  private lastReportedFolderId: string | null = null;

  private pendingNavigation: { folderId: string; token: string } | null = null;

  private navHistory: Array<{ folderId: string; profileId: string | null }> = [];

  private readonly navDepth = signal(0);

  readonly canGoBack = computed(() => this.navDepth() > 0 || !!this.selectedFolder()?.parentId);

  goBack(): void {
    this.handleNavigation({ command: this.navDepth() > 0 ? 'back' : 'parent' });
  }

  private handleNavigation(event: FolderNavigationEvent): void {
    switch (event.command) {
      case 'changeTo':
        if (event.folderId) {
          if (event.navigationToken) {
            this.pendingNavigation = { folderId: event.folderId, token: event.navigationToken };
          }
          this.pushNavHistory();
          this.navigateTo(event.folderId, event.profileId ?? null);
        }
        break;
      case 'parent': {
        const parentId = this.selectedFolder()?.parentId;
        if (parentId) {
          this.pushNavHistory();
          this.navigateTo(parentId, this.profileService.selectedProfileId());
        }
        break;
      }
      case 'back': {
        const previous = this.navHistory.pop();
        this.navDepth.set(this.navHistory.length);
        if (previous) {
          this.navigateTo(previous.folderId, previous.profileId);
        }
        break;
      }
    }
  }

  private pushNavHistory(): void {
    const current = this.selectedFolderId();
    if (current) {
      this.navHistory.push({ folderId: current, profileId: this.profileService.selectedProfileId() });
      if (this.navHistory.length > 50) {
        this.navHistory.shift();
      }

      this.navDepth.set(this.navHistory.length);
    }
  }

  private navigateTo(folderId: string, profileId: string | null): void {
    // Switching profiles reloads its folders (FolderService effect); the selection is preserved if
    // the target folder is part of that profile.
    if (profileId && profileId !== this.profileService.selectedProfileId()) {
      this.profileService.selectProfile(profileId);
    }
    this.selectedFolderId.set(folderId);
  }

  loadFolders(profileId?: string): Promise<void> {
    const targetProfileId = profileId ?? this.profileService.selectedProfileId() ?? null;
    if (this.activeFolderLoad?.profileId === targetProfileId) {
      return this.activeFolderLoad.promise;
    }

    let resolveLoad: (() => void) | undefined;
    const promise = new Promise<void>(resolve => {
      resolveLoad = resolve;
    });
    const load: FolderLoad = {
      profileId: targetProfileId,
      createdFolders: new Map(),
      updatedFolders: new Map(),
      deletedFolderIds: new Set(),
      reorderedPlacements: new Map(),
      promise,
    };
    this.activeFolderLoad = load;
    void this.performFolderLoad(load).finally(() => resolveLoad?.());
    return promise;
  }

  private async performFolderLoad(load: FolderLoad): Promise<void> {
    const targetProfileId = load.profileId ?? undefined;

    this.isLoading.set(true);
    this.loadError.set(null);

    try {
      const response = await this.api.getFolders(targetProfileId);
      if (this.activeFolderLoad !== load) return;

      const folders = this.mergeFolderLoadResponse((response.folders ?? []).map(f => this.mapIpcFolder(f)), load);
      this.loadedProfileId = load.profileId ?? response.folders?.[0]?.profileId ?? null;
      this.folders.set(folders);

      const currentSelection = this.selectedFolderId();
      if (!currentSelection || !folders.some(f => f.id === currentSelection)) {
        this.selectedFolderId.set(this.findStartFolderId(folders));
      }
    } catch (error) {
      if (this.activeFolderLoad !== load) return;
      console.error('Failed to load folders:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.LoadFailed));
    } finally {
      if (this.activeFolderLoad === load) {
        this.activeFolderLoad = null;
        this.isLoading.set(false);
      }
    }
  }

  private mapIpcFolder(ipc: IpcFolder): DomainFolder {
    return folderFromWire(ipc);
  }

  private mapIpcWidget(ipc: IpcWidget, folderId: string): GridWidget {
    return widgetFromWire(ipc, folderId);
  }

  private parseWidgetData(type: DomainWidgetType, data: string | undefined): WidgetData {
    return parseWidgetData(type, data);
  }

  private getDefaultWidgetData(type: DomainWidgetType): WidgetData {
    return defaultWidgetData(type);
  }

  private serializeWidgetData(type: DomainWidgetType, data: WidgetData): string {
    return serializeWidgetData(type, data);
  }

  readonly selectedFolder = computed(() => {
    return this.folders().find(f => f.id === this.selectedFolderId());
  });

  readonly currentFolderName = computed(() => this.selectedFolder()?.name ?? 'Deck');
  readonly currentCols = computed(() =>
    this.resolveInherited(this.selectedFolder(), f => f.cols, p => p.defaultColumns) ?? this.DEFAULT_COLS);
  readonly currentRows = computed(() =>
    this.resolveInherited(this.selectedFolder(), f => f.rows, p => p.defaultRows) ?? this.DEFAULT_ROWS);
  readonly currentBackground = computed(() =>
    this.selectedFolder()?.background || this.profileService.selectedProfile()?.defaultBackground || this.DEFAULT_BACKGROUND);
  readonly currentWidgets = computed(() => collectDisplayedWidgets(this.folders(), this.selectedFolderId()));

  readonly currentSpacing = computed(() =>
    this.resolveInherited(this.selectedFolder(), f => f.spacing, p => p.defaultSpacing) ?? WIDGET_REFERENCE_GAP);

  readonly currentBorderRadius = computed(() =>
    this.resolveInherited(this.selectedFolder(), f => f.borderRadius, p => p.defaultBorderRadius));

  readonly currentViewId = computed(() => this.selectedFolder()?.viewId ?? WIDGET_GRID_VIEW_ID);

  readonly currentIsWidgetGrid = computed(() => isWidgetGridView(this.currentViewId()));

  getEffectiveSpacing(folder: DomainFolder): number {
    return this.resolveInherited(folder, f => f.spacing, p => p.defaultSpacing) ?? WIDGET_REFERENCE_GAP;
  }

  getEffectiveCols(folder: DomainFolder): number {
    return this.resolveInherited(folder, f => f.cols, p => p.defaultColumns) ?? this.DEFAULT_COLS;
  }

  getEffectiveRows(folder: DomainFolder): number {
    return this.resolveInherited(folder, f => f.rows, p => p.defaultRows) ?? this.DEFAULT_ROWS;
  }

  private resolveInherited(
    folder: DomainFolder | undefined,
    pick: (f: DomainFolder) => number | null,
    pickProfile: (p: Profile) => number | null
  ): number | null {
    const folders = this.folders();
    const visited = new Set<string>();
    let current = folder;
    while (current && !visited.has(current.id)) {
      const value = pick(current);
      if (value !== null) return value;
      visited.add(current.id);
      current = current.parentId !== null
        ? folders.find(f => f.id === current!.parentId)
        : undefined;
    }
    const profile = this.profileService.selectedProfile();
    return profile ? pickProfile(profile) : null;
  }

  private readonly gridConstraint = computed(() => this.profileService.selectedProfile()?.layout.constraint ?? null);

  readonly minCols = computed(() => {
    const widgets = this.currentWidgets();
    const widgetMin = widgets.length === 0 ? 1 : Math.max(...widgets.map(w => w.x + w.w));
    return Math.max(widgetMin, this.gridConstraint()?.minColumns ?? 1);
  });

  readonly minRows = computed(() => {
    const widgets = this.currentWidgets();
    const widgetMin = widgets.length === 0 ? 1 : Math.max(...widgets.map(w => w.y + w.h));
    return Math.max(widgetMin, this.gridConstraint()?.minRows ?? 1);
  });

  readonly maxCols = computed(() => this.gridConstraint()?.maxColumns ?? 12);

  readonly maxRows = computed(() => this.gridConstraint()?.maxRows ?? 8);

  readonly colsLocked = computed(() => this.gridConstraint()?.columnsLocked ?? false);

  readonly rowsLocked = computed(() => this.gridConstraint()?.rowsLocked ?? false);

  readonly spacingHonoured = computed(() => this.gridConstraint()?.widgetSpacingHonoured ?? true);

  readonly cornerRadiusHonoured = computed(() => this.gridConstraint()?.cornerRadiusHonoured ?? true);

  readonly customFolderViewsSupported = computed(() =>
    this.gridConstraint()?.customFolderViewsSupported ?? true);

  readonly contextMenu = signal<FolderContextMenu>({
    isOpen: false,
    folderId: null,
    x: 0,
    y: 0
  });

  readonly rootFolders = computed(() => {
    return this.folders()
      .filter(f => f.parentId === null)
      .sort((a, b) => a.order - b.order || a.id.localeCompare(b.id));
  });

  readonly startFolderId = computed<string | null>(() => this.findStartFolderId(this.folders()));

  async setStartFolder(id: string): Promise<void> {
    const folder = this.getFolderById(id);
    if (!folder || folder.parentId !== null) {
      console.error('Only a root folder can become the start folder.');
      return;
    }

    const previousFolders = this.folders();
    this.folders.update(folders =>
      folders.map(f => ({ ...f, isDefault: f.id === id }))
    );
    try {
      const response = await this.api.updateFolder({ id, isDefault: true });
      if (!response.success) {
        this.folders.set(previousFolders);
        console.error('Failed to set start folder:', response.error?.message);
      }
    } catch (error) {
      this.folders.set(previousFolders);
      console.error('Failed to set start folder:', error);
    }
  }

  getChildFolders(parentId: string): DomainFolder[] {
    return this.folders()
      .filter(f => f.parentId === parentId)
      .sort((a, b) => a.order - b.order);
  }

  getFolderById(id: string): DomainFolder | undefined {
    return this.folders().find(f => f.id === id);
  }

  findWidget(widgetId: string): GridWidget | undefined {
    for (const folder of this.folders()) {
      const widget = folder.widgets.find(w => w.id === widgetId);
      if (widget) return widget;
    }
    return undefined;
  }

  private resolveFolderId(widgetId: string): string | null {
    return this.findWidget(widgetId)?.folderId ?? this.selectedFolderId();
  }

  selectFolder(id: string): void {
    this.closeContextMenu();
    this.selectedFolderId.set(id);
  }

  toggleFolderExpanded(id: string): void {
    this.closeContextMenu();
    this.folders.update(folders =>
      folders.map(f => f.id === id ? { ...f, isExpanded: !f.isExpanded } : f)
    );
  }

  expandFolder(id: string): void {
    this.folders.update(folders =>
      folders.map(f => f.id === id ? { ...f, isExpanded: true } : f)
    );
  }

  async setGridDimensions(cols: number | null, rows: number | null): Promise<Result> {
    const folderId = this.selectedFolderId();
    if (!folderId) return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderSelected) } };

    const previous = this.getFolderById(folderId);
    const rollback = () => {
      if (!previous) return;
      this.folders.update(folders =>
        folders.map(f => f.id === folderId ? { ...f, cols: previous.cols, rows: previous.rows } : f)
      );
    };

    this.folders.update(folders =>
      folders.map(f => f.id === folderId ? { ...f, cols, rows } : f)
    );

    try {
      const response = await this.api.updateFolder({ id: folderId, columns: cols ?? -1, rows: rows ?? -1 });
      if (!response.success) {
        rollback();
        return { success: false, error: response.error };
      }
      return { success: true };
    } catch (error) {
      rollback();
      console.error(`Failed to persist grid dimensions for folder ${folderId}:`, error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.ResizeGridFailed)
        }
      };
    }
  }

  async setBackground(background: string): Promise<void> {
    const folderId = this.selectedFolderId();
    if (!folderId) return;

    this.folders.update(folders =>
      folders.map(f => f.id === folderId ? { ...f, background } : f)
    );

    try {
      await this.api.updateFolder({ id: folderId, backgroundColor: background });
    } catch (error) {
      console.error(`Failed to persist background color for folder ${folderId}:`, error);
    }
  }

  async setWidgetSpacing(spacing: number | null): Promise<void> {
    const folderId = this.selectedFolderId();
    if (!folderId) return;

    this.folders.update(folders =>
      folders.map(f => f.id === folderId ? { ...f, spacing } : f)
    );

    try {
      await this.api.updateFolder({ id: folderId, widgetSpacing: spacing ?? -1 });
    } catch (error) {
      console.error(`Failed to persist widget spacing for folder ${folderId}:`, error);
    }
  }

  async setWidgetBorderRadius(borderRadius: number | null): Promise<void> {
    const folderId = this.selectedFolderId();
    if (!folderId) return;

    this.folders.update(folders =>
      folders.map(f => f.id === folderId ? { ...f, borderRadius } : f)
    );

    try {
      await this.api.updateFolder({ id: folderId, widgetBorderRadius: borderRadius ?? -1 });
    } catch (error) {
      console.error(`Failed to persist widget border radius for folder ${folderId}:`, error);
    }
  }

  async setFolderView(
    folderId: string,
    folderViewId: string,
    folderViewConfiguration: string | null = null,
  ): Promise<Result<DomainFolder>> {
    try {
      const response = await this.api.updateFolder({
        id: folderId,
        folderViewId,
        folderViewConfiguration: folderViewConfiguration ?? undefined,
      });

      if (!response.success || !response.folder) {
        return { success: false, error: response.error };
      }

      const folder = this.mapIpcFolder(response.folder);
      this.folders.update(folders => folders.map(f => f.id === folder.id ? folder : f));

      return { success: true, data: folder };
    } catch (error) {
      console.error(`Failed to set the folder view for folder ${folderId}:`, error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderReturned),
        },
      };
    }
  }

  async addWidgetAt(x: number, y: number, type: DomainWidgetType): Promise<GridWidget | null> {
    const folderId = this.selectedFolderId();
    if (!folderId) return null;
    const folder = this.selectedFolder();
    if (!folder) return null;

    const isOccupied = this.currentWidgets().some(w =>
      x >= w.x && x < w.x + w.w && y >= w.y && y < w.y + w.h
    );
    if (isOccupied) return null;

    const tempWidgetId = this.generateId();
    const newWidget: GridWidget = {
      id: tempWidgetId,
      folderId,
      x,
      y,
      w: 1,
      h: 1,
      type,
      data: this.getDefaultDataForType(type)
    };

    this.folders.update(folders =>
      folders.map(f => f.id === folderId
        ? { ...f, widgets: [...f.widgets, newWidget] }
        : f
      )
    );

    try {
      const response = await this.api.createWidget({
        folderId,
        type,
        positionX: x,
        positionY: y,
        width: 1,
        height: 1,
        data: this.serializeWidgetData(type, newWidget.data)
      });

      if (response.success && response.widget) {
        const createdWidget = this.mapIpcWidget(response.widget, folderId);
        this.folders.update(folders =>
          folders.map(f => f.id === folderId
            ? {
                ...f,
                widgets: [
                  ...f.widgets.filter(w => w.id !== tempWidgetId && w.id !== createdWidget.id),
                  createdWidget
                ]
              }
            : f
          )
        );
        return createdWidget;
      }

      console.error('Failed to create widget:', response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Folder.NoWidgetReturned));
    } catch (error) {
      console.error('Failed to create widget:', error);
    }

    this.folders.update(folders =>
      folders.map(f => f.id === folderId
        ? { ...f, widgets: f.widgets.filter(w => w.id !== tempWidgetId) }
        : f
      )
    );

    return null;
  }

  async addWidgetFromApplication(x: number, y: number, path: string): Promise<Result<GridWidget>> {
    const folderId = this.selectedFolderId();
    if (!folderId) {
      return { success: false, error: { code: 'NoFolder', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderOpen) } };
    }

    try {
      const response = await this.api.createWidgetFromApplication({
        folderId,
        positionX: x,
        positionY: y,
        path
      });

      if (!response.success || !response.widget) {
        return { success: false, error: response.error };
      }

      const createdWidget = this.mapIpcWidget(response.widget, folderId);
      this.folders.update(folders =>
        folders.map(f => f.id === folderId
          ? { ...f, widgets: [...f.widgets.filter(w => w.id !== createdWidget.id), createdWidget] }
          : f
        )
      );

      return { success: true, data: createdWidget };
    } catch (error) {
      console.error('Failed to create widget from application:', error);
      return { success: false, error: { code: 'RequestFailed', message: this.localization.translateKey(AppStrings.Errors.Folder.AddApplicationFailed) } };
    }
  }

  private getDefaultDataForType(type: DomainWidgetType): WidgetData {
    return this.widgetRegistry.getDefaultData(type) ?? ({} as WidgetData);
  }

  updateWidgetData(widgetId: string, dataUpdates: Partial<WidgetData>): void {
    const folderId = this.resolveFolderId(widgetId);
    if (!folderId) return;
    this.folders.update(folders =>
      folders.map(f => f.id === folderId
        ? {
            ...f,
            widgets: f.widgets.map(w =>
              w.id === widgetId
                ? { ...w, data: { ...w.data, ...dataUpdates } }
                : w
            )
          }
        : f
      )
    );
  }

  async updateWidgetRuntimeData(widgetId: string, dataUpdates: Partial<WidgetData>): Promise<void> {
    const folderId = this.resolveFolderId(widgetId);
    if (!folderId) return;

    this.updateWidgetData(widgetId, dataUpdates);

    if (!this.isGuid(widgetId) || !this.isGuid(folderId)) return;

    try {
      await this.api.updateWidgetData({ widgetId, folderId, data: JSON.stringify(dataUpdates) });
    } catch (error) {
      console.error('Failed to update widget data:', error);
    }
  }

  async removeWidget(widgetId: string, folderId?: string): Promise<void> {
    const targetFolderId = folderId ?? this.resolveFolderId(widgetId);
    if (!targetFolderId) return;

    this.folders.update(folders =>
      folders.map(f => f.id === targetFolderId
        ? { ...f, widgets: f.widgets.filter(w => w.id !== widgetId) }
        : f
      )
    );

    try {
      await this.api.deleteWidget({ id: widgetId, folderId: targetFolderId });
    } catch (error) {
      console.error('Failed to delete widget:', error);
    }
  }

  async removeWidgets(widgetIds: readonly string[], folderId?: string): Promise<Result> {
    const targetFolderId = folderId ?? this.selectedFolderId();
    if (!targetFolderId) {
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderSelected) } };
    }
    if (widgetIds.length === 0) return { success: true };

    const previousWidgets = this.getFolderById(targetFolderId)?.widgets;
    const idSet = new Set(widgetIds);
    const rollback = () => {
      if (!previousWidgets) return;
      this.folders.update(folders =>
        folders.map(f => f.id === targetFolderId ? { ...f, widgets: previousWidgets } : f)
      );
    };

    this.folders.update(folders =>
      folders.map(f => f.id === targetFolderId
        ? { ...f, widgets: f.widgets.filter(w => !idSet.has(w.id)) }
        : f
      )
    );

    try {
      const response = await this.api.deleteWidgets({ folderId: targetFolderId, ids: [...widgetIds] });
      if (!response.success) {
        rollback();
        console.error('Failed to delete widgets:', response.error?.message);
        return { success: false, error: response.error };
      }
      return { success: true };
    } catch (error) {
      rollback();
      console.error('Failed to delete widgets:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.DeleteWidgetsFailed)
        }
      };
    }
  }

  async pasteWidget(entry: WidgetClipboardEntry, anchorX: number, anchorY: number): Promise<GridWidget | null> {
    const folderId = this.selectedFolderId();
    if (!folderId) return null;
    const folder = this.selectedFolder();
    if (!folder) return null;

    // A same-folder cut source will be removed, so it must not block or shrink the fit.
    // Pinned widgets from other folders render here, so they occupy cells like local ones.
    const excludeId = entry.isCut && entry.origin?.folderId === folderId ? entry.origin.widgetId : null;
    const occupied: GridRect[] = this.currentWidgets()
      .filter(w => w.id !== excludeId)
      .map(w => ({ x: w.x, y: w.y, w: w.w, h: w.h }));

    const rect = computePasteRect(
      anchorX, anchorY, entry.w, entry.h, this.getEffectiveCols(folder), this.getEffectiveRows(folder), occupied);
    if (!rect) return null;

    const data = JSON.parse(JSON.stringify(entry.data)) as WidgetData;
    const tempWidgetId = this.generateId();
    const newWidget: GridWidget = {
      id: tempWidgetId,
      folderId,
      x: rect.x,
      y: rect.y,
      w: rect.w,
      h: rect.h,
      type: entry.type,
      data
    };

    this.folders.update(folders =>
      folders.map(f => f.id === folderId
        ? { ...f, widgets: [...f.widgets, newWidget] }
        : f
      )
    );

    try {
      const response = await this.api.createWidget({
        folderId,
        type: entry.type,
        positionX: rect.x,
        positionY: rect.y,
        width: rect.w,
        height: rect.h,
        data: this.serializeWidgetData(entry.type, data),
        ...(entry.origin ? { sourceWidgetId: entry.origin.widgetId } : {})
      });

      if (response.success && response.widget) {
        const createdWidget = this.mapIpcWidget(response.widget, folderId);
        this.folders.update(folders =>
          folders.map(f => f.id === folderId
            ? {
                ...f,
                widgets: [
                  ...f.widgets.filter(w => w.id !== tempWidgetId && w.id !== createdWidget.id),
                  createdWidget
                ]
              }
            : f
          )
        );
        return createdWidget;
      }

      console.error('Failed to paste widget:', response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Folder.NoWidgetReturned));
    } catch (error) {
      console.error('Failed to paste widget:', error);
    }

    this.folders.update(folders =>
      folders.map(f => f.id === folderId
        ? { ...f, widgets: f.widgets.filter(w => w.id !== tempWidgetId) }
        : f
      )
    );

    return null;
  }

  async pasteWidgets(
    entries: readonly WidgetClipboardEntry[],
    anchorX: number,
    anchorY: number
  ): Promise<Result<{ replacedWidgetIds: string[] }>> {
    const folderId = this.selectedFolderId();
    if (!folderId) return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderSelected) } };
    const folder = this.selectedFolder();
    if (!folder) return { success: false, error: { code: 'NOT_FOUND', message: this.localization.translateKey(AppStrings.Errors.Folder.NotFound) } };
    if (entries.length === 0) return { success: true };

    const rects: GridRect[] = entries.map(entry => (
      { x: anchorX + entry.dx, y: anchorY + entry.dy, w: entry.w, h: entry.h }
    ));

    // A same-folder cut source will be removed, so it must not block the fit. Pinned widgets from
    // other folders render here, so they occupy cells like local ones - same rule as pasteWidget.
    const replaceIds = entries
      .filter(e => e.isCut && e.origin?.folderId === folderId)
      .map(e => e.origin!.widgetId);
    const excludeIds = new Set(replaceIds);
    const occupied: GridRect[] = this.currentWidgets()
      .filter(w => !excludeIds.has(w.id))
      .map(w => ({ x: w.x, y: w.y, w: w.w, h: w.h }));

    if (!canPlaceGroup(rects, this.getEffectiveCols(folder), this.getEffectiveRows(folder), occupied)) {
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.GroupDoesNotFit) } };
    }

    const previousWidgets = folder.widgets;
    const tempIds = entries.map(() => this.generateId());
    const newWidgets: GridWidget[] = entries.map((entry, i) => ({
      id: tempIds[i],
      folderId,
      x: rects[i].x,
      y: rects[i].y,
      w: entry.w,
      h: entry.h,
      type: entry.type,
      data: JSON.parse(JSON.stringify(entry.data)) as WidgetData
    }));

    const rollback = () => {
      this.folders.update(folders =>
        folders.map(f => f.id === folderId ? { ...f, widgets: previousWidgets } : f)
      );
    };

    this.folders.update(folders =>
      folders.map(f => f.id === folderId ? { ...f, widgets: [...f.widgets, ...newWidgets] } : f)
    );

    try {
      const response = await this.api.createWidgets({
        folderId,
        widgets: entries.map((entry, i) => ({
          type: entry.type,
          positionX: rects[i].x,
          positionY: rects[i].y,
          width: entry.w,
          height: entry.h,
          data: this.serializeWidgetData(entry.type, newWidgets[i].data),
          ...(entry.origin ? { sourceWidgetId: entry.origin.widgetId } : {})
        })),
        ...(replaceIds.length > 0 ? { replaceIds } : {})
      });

      if (!response.success || !response.widgets?.length) {
        rollback();
        console.error('Failed to paste widgets:', response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Folder.NoWidgetsReturned));
        return { success: false, error: response.error };
      }

      const createdWidgets = response.widgets.map(w => this.mapIpcWidget(w, folderId));
      const createdIds = new Set(createdWidgets.map(w => w.id));
      this.folders.update(folders =>
        folders.map(f => f.id === folderId
          ? {
              ...f,
              widgets: [
                ...f.widgets.filter(w =>
                  !tempIds.includes(w.id) && !createdIds.has(w.id) && !excludeIds.has(w.id)),
                ...createdWidgets
              ]
            }
          : f
        )
      );
      return { success: true, data: { replacedWidgetIds: replaceIds } };
    } catch (error) {
      rollback();
      console.error('Failed to paste widgets:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.PasteWidgetsFailed)
        }
      };
    }
  }

  async setWidgetPinned(widgetId: string, pinned: boolean, scope?: PinScope): Promise<Result> {
    const widget = this.findWidget(widgetId);
    if (!widget) {
      return { success: false, error: { code: 'NOT_FOUND', message: this.localization.translateKey(AppStrings.Errors.Folder.WidgetNotFound) } };
    }

    try {
      const request: SetWidgetPinnedRequest = { widgetId, folderId: widget.folderId, pinned };
      if (scope !== undefined) {
        request.scope = scope;
      }
      const response = await this.api.setWidgetPinned(request);
      if (!response.success) {
        return { success: false, error: response.error };
      }

      const responseWidget = response.widget;
      this.folders.update(folders =>
        folders.map(f => f.id === widget.folderId
          ? {
              ...f,
              widgets: f.widgets.map(w => {
                if (w.id !== widgetId) return w;
                if (responseWidget) {
                  const mapped = this.mapIpcWidget(responseWidget, widget.folderId);
                  // A response that carries no data must not blank what we already have.
                  return { ...mapped, data: responseWidget.data ? mapped.data : w.data };
                }
                return { ...w, isPinned: pinned, pinScope: pinned ? (scope ?? w.pinScope ?? 'Profile') : 'Profile' };
              })
            }
          : f
        )
      );
      return { success: true };
    } catch (error) {
      console.error('Failed to change the widget pin state:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.ChangeWidgetPinFailed)
        }
      };
    }
  }

  async setWidgetsPinned(widgetIds: readonly string[], pinned: boolean, scope?: PinScope): Promise<Result> {
    if (widgetIds.length === 0) return { success: true };
    const folderId = this.selectedFolderId();
    if (!folderId) return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderSelected) } };

    try {
      const request: SetWidgetsPinnedRequest = { folderId, widgetIds: [...widgetIds], pinned };
      if (scope !== undefined) {
        request.scope = scope;
      }
      const response = await this.api.setWidgetsPinned(request);
      if (!response.success) {
        return { success: false, error: response.error };
      }

      const responseWidgets = response.widgets;
      if (responseWidgets?.length) {
        const byId = new Map(responseWidgets.map(w => [w.id, w]));
        this.folders.update(folders =>
          folders.map(f => f.id === folderId
            ? {
                ...f,
                widgets: f.widgets.map(w => {
                  const updated = byId.get(w.id);
                  if (!updated) return w;
                  const mapped = this.mapIpcWidget(updated, folderId);
                  // A response that carries no data must not blank what we already have.
                  return { ...mapped, data: updated.data ? mapped.data : w.data };
                })
              }
            : f
          )
        );
      } else {
        const idSet = new Set(widgetIds);
        this.folders.update(folders =>
          folders.map(f => f.id === folderId
            ? {
                ...f,
                widgets: f.widgets.map(w => idSet.has(w.id)
                  ? { ...w, isPinned: pinned, pinScope: pinned ? (scope ?? w.pinScope ?? 'Profile') : 'Profile' }
                  : w
                )
              }
            : f
          )
        );
      }
      return { success: true };
    } catch (error) {
      console.error('Failed to change the widgets pin state:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.ChangeWidgetsPinFailed)
        }
      };
    }
  }

  async updateWidget(widgetId: string, updates: Partial<GridWidget>): Promise<boolean> {
    const folderId = this.resolveFolderId(widgetId);
    if (!folderId) return false;

    const existingWidget = this.findWidget(widgetId);

    this.folders.update(folders =>
      folders.map(f => f.id === folderId
        ? { ...f, widgets: f.widgets.map(w => w.id === widgetId ? { ...w, ...updates } : w) }
        : f
      )
    );

    if (!existingWidget) return false;

    if (!this.isGuid(widgetId) || !this.isGuid(folderId)) {
      console.error('Cannot update widget: widget or folder ID is not a valid GUID', { widgetId, folderId });
      return false;
    }

    const rollback = () => {
      this.folders.update(folders =>
        folders.map(f => f.id === folderId
          ? { ...f, widgets: f.widgets.map(w => w.id === widgetId ? existingWidget : w) }
          : f
        )
      );
    };

    const merged = { ...existingWidget, ...updates };
    try {
      const response = await this.api.updateWidget({
        id: widgetId,
        folderId,
        type: merged.type,
        positionX: merged.x,
        positionY: merged.y,
        width: merged.w,
        height: merged.h,
        data: this.serializeWidgetData(merged.type, merged.data)
      });
      if (!response.success) {
        rollback();
        console.error('Failed to update widget:', response.error?.message);
        return false;
      }
      return true;
    } catch (error) {
      rollback();
      console.error('Failed to update widget:', error);
      return false;
    }
  }

  async commitWidgetLayout(changes: ReadonlyMap<string, GridRect>): Promise<Result> {
    const folderId = this.selectedFolderId();
    if (!folderId || changes.size === 0) return { success: true };
    if (!this.isGuid(folderId)) {
      console.error('Cannot commit widget layout: folder ID is not a valid GUID', { folderId });
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.InvalidFolderId) } };
    }

    const folder = this.getFolderById(folderId);
    if (!folder) return { success: false, error: { code: 'NOT_FOUND', message: this.localization.translateKey(AppStrings.Errors.Folder.NotFound) } };

    const previousRects = new Map<string, GridRect>();
    for (const widget of folder.widgets) {
      if (changes.has(widget.id)) {
        previousRects.set(widget.id, { x: widget.x, y: widget.y, w: widget.w, h: widget.h });
      }
    }

    const applyRects = (rects: ReadonlyMap<string, GridRect>) => {
      this.folders.update(folders =>
        folders.map(f => f.id === folderId
          ? {
              ...f,
              widgets: f.widgets.map(w => {
                const rect = rects.get(w.id);
                return rect ? { ...w, x: rect.x, y: rect.y, w: rect.w, h: rect.h } : w;
              })
            }
          : f
        )
      );
    };

    applyRects(changes);

    try {
      const response = await this.api.updateWidgetPositions({
        folderId,
        positions: [...changes.entries()].map(([id, rect]) => ({
          id,
          positionX: rect.x,
          positionY: rect.y,
          width: rect.w,
          height: rect.h
        }))
      });
      if (!response.success) {
        applyRects(previousRects);
        console.error('Failed to commit widget layout:', response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Folder.UnknownError));
        return { success: false, error: response.error };
      }
      return { success: true };
    } catch (error) {
      applyRects(previousRects);
      console.error('Failed to commit widget layout:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.CommitLayoutFailed)
        }
      };
    }
  }

  async executeActionButtonTrigger(widget: GridWidget, triggerType: ActionButtonTriggerType): Promise<void> {
    const folderId = widget.folderId || this.selectedFolderId();
    if (!folderId) return;

    // Types whose tree claims the press itself fire through the generic widget-tree event pipeline
    // instead (#748), and would run their flows twice if they also came through here. Everything else
    // - including a type this client has never heard of - takes the tile's own trigger lifecycle.
    const claimsOwnGesture: readonly DomainWidgetType[] =
      [DomainWidgetType.ActionButton, DomainWidgetType.Slider];
    if (claimsOwnGesture.includes(widget.type)) return;

    const data = widget.data as { flows?: ActionFlow[] };
    const flows = data.flows;
    if (Array.isArray(flows)) {
      const localNavigation = resolveLocalDeckNavigation(flows, triggerType);
      if (localNavigation) {
        if (localNavigation.command !== 'changeTo' || this.getFolderById(localNavigation.folderId)) {
          this.handleNavigation({
            command: localNavigation.command,
            folderId: localNavigation.command === 'changeTo' ? localNavigation.folderId : undefined
          });
          return;
        }
      } else {
        const flow = findFlowForTrigger(flows, triggerType);
        if (!flow || flow.children.length === 0) return;
      }
    }

    if (!this.isGuid(widget.id) || !this.isGuid(folderId)) {
      console.error('Cannot execute action trigger: widget or folder ID is not a valid GUID', {
        widgetId: widget.id,
        folderId,
        triggerType
      });
      return;
    }

    try {
      const response = await this.api.executeActionButtonTrigger({
        widgetId: widget.id,
        folderId,
        triggerType,
        clientId: this.api.clientId
      });

      this.handleExecutionOutcome(response);
    } catch (error) {
      console.error('Failed to execute action-button trigger:', error);
    }
  }

  private handleExecutionOutcome(response: ExecuteActionButtonTriggerResponse): void {
    if (response.executionId) {
      this.actionExecution.claim(response.executionId);
    }

    if (response.status === 'Accepted') {
      if (!response.executionId) return;
      this.actionExecution.waitFor(response.executionId).then(result => {
        if (result && isFailureExecutionStatus(result.status)) {
          this.toastExecutionFailure(result.error);
        }
      });
      return;
    }

    if (isFailureExecutionStatus(response.status) || !response.success) {
      this.toastExecutionFailure(response.error);
    }
  }

  private toastExecutionFailure(error?: { code: string; message: LocalizedText }): void {
    // `||`, not `??` - resolveLocalizedText returns '' (not null/undefined) for unresolvable
    // input, and an empty toast is worse than the generic fallback.
    this.toasts.show(
      resolveLocalizedText(error?.message, this.localization) || this.localization.translateKey(AppStrings.Errors.Folder.ActionRunFailed),
      { variant: 'error' },
    );
  }

  async createFolder(
    name: string,
    parentId: string | null = null,
    folderViewId: string | null = null,
    folderViewConfiguration: string | null = null,
  ): Promise<Result<DomainFolder>> {
    const profileId = this.profileService.selectedProfileId();
    if (!profileId) {
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoProfileSelected) } };
    }

    try {
      const response = await this.api.createFolder({
        profileId,
        name,
        parentId: parentId ?? undefined,
        folderViewId: folderViewId ?? undefined,
        folderViewConfiguration: folderViewConfiguration ?? undefined,
      });

      if (!response.success) {
        return { success: false, error: response.error };
      }

      if (!response.folder) {
        return { success: false, error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderReturned) } };
      }

      const folder = this.mapIpcFolder(response.folder);

      if (parentId) {
        this.expandFolder(parentId);
      }

      this.selectedFolderId.set(folder.id);
      return { success: true, data: folder };
    } catch (error) {
      console.error('Failed to create folder:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.CreateFailed) }
      };
    }
  }

  async renameFolder(id: string, newName: string): Promise<Result<DomainFolder>> {
    try {
      const response = await this.api.updateFolder({ id, name: newName });

      if (!response.success) {
        return { success: false, error: response.error };
      }

      this.folders.update(folders =>
        folders.map(f => f.id === id ? { ...f, name: newName } : f)
      );

      return { success: true, data: this.getFolderById(id) };
    } catch (error) {
      console.error('Failed to rename folder:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.RenameFailed) }
      };
    }
  }

  canDeleteFolder(id: string): boolean {
    const folders = this.folders();
    if (folders.length <= 1) return false;

    const idsToDelete = this.getAllDescendantIds(id);
    idsToDelete.push(id);

    const remainingFolders = folders.filter(f => !idsToDelete.includes(f.id));
    return remainingFolders.length > 0;
  }

  deletionScope(folderId: string): { name: string; subfolderCount: number; widgetCount: number; pinnedCount: number } | null {
    const folder = this.getFolderById(folderId);
    if (!folder) return null;

    const descendantIds = this.getAllDescendantIds(folderId);
    const subtreeIds = new Set([folderId, ...descendantIds]);
    const widgets = this.folders()
      .filter(f => subtreeIds.has(f.id))
      .flatMap(f => f.widgets);

    return {
      name: folder.name,
      subfolderCount: descendantIds.length,
      widgetCount: widgets.length,
      pinnedCount: widgets.filter(w => w.isPinned).length
    };
  }

  async deleteFolder(id: string): Promise<Result> {
    if (!this.canDeleteFolder(id)) {
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.CannotDeleteLastFolder) } };
    }

    try {
      const response = await this.api.deleteFolder({ id });

      if (!response.success) {
        return { success: false, error: response.error };
      }

      const idsToDelete = this.getAllDescendantIds(id);
      idsToDelete.push(id);

      const currentSelectedId = this.selectedFolderId();
      if (currentSelectedId && idsToDelete.includes(currentSelectedId)) {
        const folder = this.getFolderById(id);
        if (folder?.parentId) {
          this.selectedFolderId.set(folder.parentId);
        } else {
          const remaining = this.folders().filter(folder => !idsToDelete.includes(folder.id));
          this.selectKnownStartOrReload(remaining);
        }
      }

      this.folders.update(folders => folders.filter(f => !idsToDelete.includes(f.id)));
      return { success: true };
    } catch (error) {
      console.error('Failed to delete folder:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.DeleteFailed) }
      };
    }
  }

  async duplicateFolder(id: string): Promise<Result<DomainFolder>> {
    try {
      const response = await this.api.duplicateFolder({ id });

      if (!response.success) {
        return { success: false, error: response.error };
      }

      if (!response.folder) {
        return { success: false, error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.NoFolderReturned) } };
      }

      const folder = this.mapIpcFolder(response.folder);
      this.folders.update(folders =>
        folders.some(f => f.id === folder.id) ? folders : [...folders, folder]
      );
      this.selectedFolderId.set(folder.id);

      return { success: true, data: folder };
    } catch (error) {
      console.error('Failed to duplicate folder:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.DuplicateFailed) }
      };
    }
  }

  private getAllDescendantIds(folderId: string): string[] {
    const children = this.getChildFolders(folderId);
    let ids: string[] = [];
    for (const child of children) {
      ids.push(child.id);
      ids = ids.concat(this.getAllDescendantIds(child.id));
    }
    return ids;
  }

  async moveFolder(request: FolderMoveRequest): Promise<Result<void>> {
    const folder = this.getFolderById(request.folderId);
    const target = this.getFolderById(request.targetId);
    if (!folder || !target) {
      return { success: false, error: { code: 'NOT_FOUND', message: this.localization.translateKey(AppStrings.Errors.Folder.NotFound) } };
    }
    if (request.targetId === request.folderId) {
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.CannotMoveOntoItself) } };
    }

    const newParentId = request.position === 'inside' ? target.id : target.parentId;
    if (newParentId !== null) {
      const descendants = this.getAllDescendantIds(folder.id);
      if (newParentId === folder.id || descendants.includes(newParentId)) {
        return {
          success: false,
          error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Folder.CannotMoveIntoDescendant) }
        };
      }
    }

    const previousFolders = this.folders();

    const deferStartFolderMove = folder.isDefault && newParentId !== null;
    if (!deferStartFolderMove) {
      this.folders.set(this.applyLocalPlacement(previousFolders, folder, target, request.position));
    }
    if (request.position === 'inside') {
      this.expandFolder(target.id);
    }

    try {
      const response = await this.api.moveFolder({
        id: request.folderId,
        targetId: request.targetId,
        position: request.position
      });
      if (!response.success) {
        this.folders.set(previousFolders);
        return { success: false, error: response.error };
      }

      if (response.folders?.length) {
        this.applyCanonicalPlacements(response.folders);
      }
      return { success: true };
    } catch (error) {
      this.folders.set(previousFolders);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Folder.MoveFailed) }
      };
    }
  }

  resolveMove(folderId: string, direction: FolderMoveDirection): FolderMoveRequest | null {
    const folder = this.getFolderById(folderId);
    if (!folder) return null;

    const siblings = folder.parentId === null ? this.rootFolders() : this.getChildFolders(folder.parentId);
    const index = siblings.findIndex(f => f.id === folderId);
    if (index === -1) return null;

    switch (direction) {
      case 'up': {
        const previous = siblings[index - 1];
        return previous ? { folderId, targetId: previous.id, position: 'before' } : null;
      }
      case 'down': {
        const next = siblings[index + 1];
        return next ? { folderId, targetId: next.id, position: 'after' } : null;
      }
      case 'into': {
        const previous = siblings[index - 1];
        return previous ? { folderId, targetId: previous.id, position: 'inside' } : null;
      }
      case 'out':
        return folder.parentId !== null ? { folderId, targetId: folder.parentId, position: 'after' } : null;
    }
  }

  private applyLocalPlacement(
    folders: DomainFolder[],
    folder: DomainFolder,
    target: DomainFolder,
    position: FolderMoveRequest['position']
  ): DomainFolder[] {
    const newParentId = position === 'inside' ? target.id : target.parentId;
    const oldParentId = folder.parentId;

    const destination = folders
      .filter(f => f.parentId === newParentId && f.id !== folder.id)
      .sort((a, b) => a.order - b.order || a.id.localeCompare(b.id));

    let index: number;
    if (position === 'inside') {
      index = destination.length;
    } else {
      const targetIndex = destination.findIndex(f => f.id === target.id);
      const fallback = destination.length;
      index = targetIndex === -1 ? fallback : position === 'before' ? targetIndex : targetIndex + 1;
    }
    destination.splice(Math.max(0, Math.min(index, destination.length)), 0, folder);

    const orderById = new Map<string, number>();
    destination.forEach((f, i) => orderById.set(f.id, i));

    if (oldParentId !== newParentId) {
      const source = folders
        .filter(f => f.parentId === oldParentId && f.id !== folder.id)
        .sort((a, b) => a.order - b.order || a.id.localeCompare(b.id));
      source.forEach((f, i) => orderById.set(f.id, i));
    }

    return folders.map(f => {
      if (f.id === folder.id) {
        return { ...f, parentId: newParentId, order: orderById.get(f.id) ?? f.order };
      }
      const order = orderById.get(f.id);
      return order !== undefined ? { ...f, order } : f;
    });
  }

  private applyCanonicalPlacements(placements: IpcFolderPlacement[]): void {
    const byId = new Map(placements.map(p => [p.id, p]));
    this.folders.update(folders =>
      folders.map(f => {
        const placement = byId.get(f.id);
        return placement
          ? { ...f, parentId: placement.parentId, order: placement.order, isDefault: placement.isDefault }
          : f;
      })
    );
  }

  openContextMenu(folderId: string, x: number, y: number): void {
    this.selectedFolderId.set(folderId);
    this.contextMenu.set({ isOpen: true, folderId, x, y });
  }

  closeContextMenu(): void {
    this.contextMenu.set({ isOpen: false, folderId: null, x: 0, y: 0 });
  }

  private findStartFolderId(folders: DomainFolder[]): string | null {
    const roots = folders
      .filter(folder => folder.parentId === null)
      .sort((left, right) => left.order - right.order || (left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
    return roots.find(folder => folder.isDefault)?.id ?? roots[0]?.id ?? null;
  }

  private isInActiveFolderScope(profileId: string | undefined): boolean {
    const scopedProfileId = this.activeFolderLoad?.profileId ?? this.loadedProfileId;
    // Older hosts can omit the profile id only on an unscoped deck response. Once a client has a
    // profile-scoped tree, accepting such an event would be unsafe because it cannot be attributed.
    return scopedProfileId === null || profileId === scopedProfileId;
  }

  private addCreatedFolder(folder: DomainFolder): void {
    this.folders.update(folders => {
      if (folders.some(current => current.id === folder.id)) return folders;
      return [...folders, folder];
    });
  }

  private mergeFolderLoadResponse(folders: DomainFolder[], load: FolderLoad): DomainFolder[] {
    const merged = new Map(folders.map(folder => [folder.id, folder]));
    for (const folder of load.createdFolders.values()) {
      merged.set(folder.id, folder);
    }
    for (const updated of load.updatedFolders.values()) {
      const existing = merged.get(updated.id);
      if (existing) {
        merged.set(updated.id, { ...updated, isExpanded: existing.isExpanded });
      }
    }
    for (const id of load.deletedFolderIds) {
      merged.delete(id);
    }
    for (const placement of load.reorderedPlacements.values()) {
      const existing = merged.get(placement.id);
      if (existing) {
        merged.set(placement.id, {
          ...existing,
          parentId: placement.parentId,
          order: placement.order,
          isDefault: placement.isDefault
        });
      }
    }
    return [...merged.values()];
  }

  private selectKnownStartOrReload(folders: DomainFolder[]): void {
    const startFolder = folders.find(folder => folder.parentId === null && folder.isDefault);
    if (startFolder) {
      this.selectedFolderId.set(startFolder.id);
      return;
    }

    this.selectedFolderId.set(null);
    void this.loadFolders();
  }

  private generateId(): string {
    return `temp-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
  }

  private isGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
  }
}
