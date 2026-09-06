import { Injectable, Signal, WritableSignal, inject, signal, untracked } from '@angular/core';
import { AppStrings, IconDeletedEvent, IconImportBatchState, IconImportProgressEvent, IconPackCreatedEvent, IconPackDeletedEvent, IconPackUpdatedEvent, IconProcessingState, IconUpdatedEvent, IconsAddedEvent, IpcIcon, IpcIconImportBatch, IpcIconPack } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { FileSaveService } from './file-save.service';

export interface IconPackModel {
  id: string;
  name: string;
  description?: string;
  author?: string;
  version?: string;
  isDefault: boolean;
  isReadOnly: boolean;
  sourceType: string;
  createdAt: string;
  updatedAt: string;
  iconCount: number;
  ownerKind: string;
  canDelete: boolean;
}

export interface IconModel {
  id: string;
  packId: string;
  name: string;
  width?: number;
  height?: number;
  isAnimated: boolean;
  processingState: IconProcessingState;
  processingError?: string;
  availableSizes: number[];
  originalFileName?: string;
}

export interface IconImportBatchModel {
  id: string;
  packId: string;
  state: IconImportBatchState;
  sourceName?: string;
  total?: number;
  processed: number;
  failed: number;
  error?: string;
}

export interface IconPackChanges {
  name: string;
  description?: string;
  author?: string;
  version?: string;
}

export interface IconPackExportResult {
  ok: boolean;
  canceled?: boolean;
  fileName?: string;
  path?: string;
  error?: string;
}

const TERMINAL_BATCH_STATES: IconImportBatchState[] = [
  'Completed',
  'CompletedWithErrors',
  'Failed',
  'Cancelled',
];

const ICON_READY_TIMEOUT_MS = 15000;

@Injectable({
  providedIn: 'root'
})
export class IconPackService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly fileSave = inject(FileSaveService);

  readonly packs = signal<IconPackModel[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly activeBatches = signal<ReadonlyMap<string, IconImportBatchModel>>(new Map());

  private readonly iconsByPack = new Map<string, WritableSignal<IconModel[]>>();
  private readonly loadedPacks = new Set<string>();
  private readonly loadingPacks = new Set<string>();
  private readonly readyWaiters = new Map<string, ((icon: IconModel | null) => void)[]>();

  constructor() {
    this.subscribeToEvents();
  }

  async loadPacks(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const response = await this.api.getIconPacks();
      this.packs.set((response.packs ?? []).map(mapPack).sort(comparePacks));
    } catch (error) {
      console.error('Failed to load icon packs:', error);
      this.loadError.set(this.localization.translateKey(AppStrings.Errors.IconPack.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  iconsFor(packId: string): Signal<IconModel[]> {
    const icons = this.iconsSignal(packId);
    if (!this.loadedPacks.has(packId) && !this.loadingPacks.has(packId)) {
      untracked(() => void this.loadIcons(packId));
    }

    return icons.asReadonly();
  }

  async loadIcons(packId: string, force = false): Promise<void> {
    if (this.loadingPacks.has(packId) || (!force && this.loadedPacks.has(packId))) {
      return;
    }

    this.loadingPacks.add(packId);
    try {
      const response = await this.api.getIcons({ packId });
      this.iconsSignal(packId).set((response.icons ?? []).map(mapIcon));
      this.loadedPacks.add(packId);
    } catch (error) {
      console.error(`Failed to load icons of pack ${packId}:`, error);
    } finally {
      this.loadingPacks.delete(packId);
    }
  }

  async createPack(changes: IconPackChanges): Promise<IconPackModel | null> {
    try {
      const response = await this.api.createIconPack(changes);
      if (!response.success || !response.pack) {
        return null;
      }

      const pack = mapPack(response.pack);
      this.upsertPack(pack);
      return pack;
    } catch (error) {
      console.error('Failed to create icon pack:', error);
      return null;
    }
  }

  async updatePack(packId: string, changes: IconPackChanges): Promise<boolean> {
    const previous = this.packs();
    const pack = previous.find(p => p.id === packId);
    if (!pack) {
      return false;
    }

    this.upsertPack({ ...pack, ...changes });
    try {
      const response = await this.api.updateIconPack(packId, changes);
      if (!response.success) {
        this.packs.set(previous);
        return false;
      }

      if (response.pack) {
        this.upsertPack(mapPack(response.pack));
      }

      return true;
    } catch (error) {
      console.error('Failed to update icon pack:', error);
      this.packs.set(previous);
      return false;
    }
  }

  async deletePack(packId: string): Promise<boolean> {
    const previous = this.packs();
    this.packs.set(previous.filter(p => p.id !== packId));
    try {
      const response = await this.api.deleteIconPack(packId);
      if (!response.success) {
        this.packs.set(previous);
        return false;
      }

      this.dropPackIcons(packId);
      return true;
    } catch (error) {
      console.error('Failed to delete icon pack:', error);
      this.packs.set(previous);
      return false;
    }
  }

  async renameIcon(iconId: string, packId: string, name: string): Promise<boolean> {
    const icons = this.iconsSignal(packId);
    const previous = icons();
    icons.set(previous.map(i => (i.id === iconId ? { ...i, name } : i)));
    try {
      const response = await this.api.renameIcon(iconId, { name });
      if (!response.success) {
        icons.set(previous);
        return false;
      }

      return true;
    } catch (error) {
      console.error('Failed to rename icon:', error);
      icons.set(previous);
      return false;
    }
  }

  async deleteIcon(iconId: string, packId: string): Promise<boolean> {
    const icons = this.iconsSignal(packId);
    const previous = icons();
    icons.set(previous.filter(i => i.id !== iconId));
    try {
      const response = await this.api.deleteIcon(iconId);
      if (!response.success) {
        icons.set(previous);
        return false;
      }

      this.adjustIconCount(packId, -1);
      return true;
    } catch (error) {
      console.error('Failed to delete icon:', error);
      icons.set(previous);
      return false;
    }
  }

  async deleteIcons(iconIds: string[], packId: string): Promise<boolean> {
    if (iconIds.length === 0) {
      return true;
    }

    const idSet = new Set(iconIds);
    const icons = this.iconsSignal(packId);
    const previous = icons();
    icons.set(previous.filter(i => !idSet.has(i.id)));
    try {
      const response = await this.api.deleteIcons({ ids: iconIds });
      if (!response.success) {
        icons.set(previous);
        return false;
      }

      this.adjustIconCount(packId, -response.deletedCount);
      return true;
    } catch (error) {
      console.error('Failed to delete icons:', error);
      icons.set(previous);
      return false;
    }
  }

  async import(packId: string | null, files: File[]): Promise<IconImportBatchModel | null> {
    if (files.length === 0) {
      return null;
    }

    try {
      const response = await this.api.uploadIcons(packId, files);
      if (!response.success || !response.batch) {
        console.error('Icon import failed:', response.error?.message);
        return null;
      }

      return this.trackBatch(mapBatch(response.batch));
    } catch (error) {
      console.error('Failed to import icons:', error);
      return null;
    }
  }

  async importPacks(files: File[]): Promise<boolean> {
    if (files.length === 0) {
      return false;
    }

    try {
      const response = await this.api.importIconPacks(files);
      if (!response.success) {
        console.error('Icon pack import failed:', response.error?.message);
        return false;
      }

      for (const pack of response.packs ?? []) {
        this.upsertPack(mapPack(pack));
      }

      if (response.batch) {
        this.trackBatch(mapBatch(response.batch));
      }

      return true;
    } catch (error) {
      console.error('Failed to import icon packs:', error);
      return false;
    }
  }

  async restoreFromPath(path: string): Promise<string | null> {
    try {
      const response = await this.api.restoreIconPackFromPath(path);
      if (!response.success) {
        console.error('Icon pack restore failed:', response.error?.message);
        return null;
      }

      const restored = (response.packs ?? []).map(mapPack);
      for (const pack of restored) {
        this.upsertPack(pack);
      }

      return restored[0]?.name ?? null;
    } catch (error) {
      console.error('Failed to restore icon pack from path:', error);
      return null;
    }
  }

  async exportPack(packId: string): Promise<IconPackExportResult> {
    try {
      const { blob, fileName } = await this.api.exportIconPack(packId);
      const result = await this.fileSave.save(blob, fileName);
      if (result.status === 'canceled') {
        return { ok: true, canceled: true, fileName };
      }
      if (result.status === 'error') {
        console.error('Failed to export icon pack:', result.message);
        return { ok: false, error: result.message };
      }
      return { ok: true, fileName, path: result.path };
    } catch (error) {
      console.error('Failed to export icon pack:', error);
      return { ok: false, error: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.IconPack.ExportFailed) };
    }
  }

  async importSingleFromPath(packId: string | null, path: string): Promise<IconModel | null> {
    try {
      const response = await this.api.importSingleIconFromPath({ packId: packId ?? undefined, path });
      if (!response.success || !response.icon) {
        console.error('Single icon import failed:', response.error?.message);
        return null;
      }

      return mapIcon(response.icon);
    } catch (error) {
      console.error('Failed to import icon from path:', error);
      return null;
    }
  }

  whenIconReady(iconId: string, timeoutMs = ICON_READY_TIMEOUT_MS): Promise<IconModel | null> {
    return new Promise<IconModel | null>(resolve => {
      let timer: ReturnType<typeof setTimeout> | undefined;
      let settled = false;
      const waiter = (icon: IconModel | null): void => {
        if (settled) {
          return;
        }
        settled = true;
        clearTimeout(timer);
        this.removeReadyWaiter(iconId, waiter);
        resolve(icon);
      };

      timer = setTimeout(() => waiter(null), timeoutMs);
      const waiters = this.readyWaiters.get(iconId);
      if (waiters) {
        waiters.push(waiter);
      } else {
        this.readyWaiters.set(iconId, [waiter]);
      }
    });
  }

  async importFromPath(packId: string, paths: string[]): Promise<IconImportBatchModel | null> {
    try {
      const response = await this.api.importIconsFromPath(packId, { paths });
      if (!response.success || !response.batch) {
        console.error('Icon path import failed:', response.error?.message);
        return null;
      }

      return this.trackBatch(mapBatch(response.batch));
    } catch (error) {
      console.error('Failed to import icons from path:', error);
      return null;
    }
  }

  dismissBatch(batchId: string): void {
    this.activeBatches.update(batches => {
      if (!batches.has(batchId)) {
        return batches;
      }

      const next = new Map(batches);
      next.delete(batchId);
      return next;
    });
  }

  async cancelBatch(batchId: string): Promise<boolean> {
    try {
      const response = await this.api.cancelIconImportBatch(batchId);
      return response.cancelled;
    } catch (error) {
      console.error('Failed to cancel icon import:', error);
      return false;
    }
  }

  private subscribeToEvents(): void {
    this.api.onNotification<IconPackCreatedEvent>('IconPackCreatedEvent').subscribe(event => {
      this.upsertPack(mapPack(event.pack));
    });

    this.api.onNotification<IconPackUpdatedEvent>('IconPackUpdatedEvent').subscribe(event => {
      this.upsertPack(mapPack(event.pack));
    });

    this.api.onNotification<IconPackDeletedEvent>('IconPackDeletedEvent').subscribe(event => {
      this.packs.update(packs => packs.filter(p => p.id !== event.packId));
      this.dropPackIcons(event.packId);
    });

    this.api.onNotification<IconsAddedEvent>('IconsAddedEvent').subscribe(event => {
      const added = event.icons.map(mapIcon);
      if (this.loadedPacks.has(event.packId)) {
        const icons = this.iconsSignal(event.packId);
        const known = new Set(icons().map(i => i.id));
        const fresh = added.filter(i => !known.has(i.id));
        if (fresh.length > 0) {
          icons.update(current => [...current, ...fresh]);
        }
      }

      this.adjustIconCount(event.packId, added.length);
    });

    this.api.onNotification<IconUpdatedEvent>('IconUpdatedEvent').subscribe(event => {
      const icon = mapIcon(event.icon);
      this.resolveReadyWaiters(icon);
      if (!this.loadedPacks.has(icon.packId)) {
        return;
      }

      this.iconsSignal(icon.packId).update(icons =>
        icons.map(i => (i.id === icon.id ? icon : i))
      );
    });

    this.api.onNotification<IconDeletedEvent>('IconDeletedEvent').subscribe(event => {
      if (this.loadedPacks.has(event.packId)) {
        this.iconsSignal(event.packId).update(icons => {
          if (!icons.some(i => i.id === event.iconId)) {
            return icons;
          }

          this.adjustIconCount(event.packId, -1);
          return icons.filter(i => i.id !== event.iconId);
        });
      }
    });

    this.api.onNotification<IconImportProgressEvent>('IconImportProgressEvent').subscribe(event => {
      this.trackBatch({
        id: event.batchId,
        packId: event.packId,
        state: event.state,
        sourceName: event.sourceName,
        total: event.total,
        processed: event.processed,
        failed: event.failed,
        error: event.error,
      });
    });
  }

  private resolveReadyWaiters(icon: IconModel): void {
    if (icon.processingState === 'Pending' || icon.processingState === 'Processing') {
      return;
    }

    const waiters = this.readyWaiters.get(icon.id);
    if (!waiters) {
      return;
    }

    this.readyWaiters.delete(icon.id);
    for (const waiter of waiters) {
      waiter(icon);
    }
  }

  private removeReadyWaiter(iconId: string, waiter: (icon: IconModel | null) => void): void {
    const waiters = this.readyWaiters.get(iconId);
    if (!waiters) {
      return;
    }

    const index = waiters.indexOf(waiter);
    if (index >= 0) {
      waiters.splice(index, 1);
    }
    if (waiters.length === 0) {
      this.readyWaiters.delete(iconId);
    }
  }

  private trackBatch(batch: IconImportBatchModel): IconImportBatchModel {
    this.activeBatches.update(batches => {
      const next = new Map(batches);
      next.set(batch.id, batch);
      return next;
    });
    return batch;
  }

  private upsertPack(pack: IconPackModel): void {
    this.packs.update(packs => {
      const index = packs.findIndex(p => p.id === pack.id);
      if (index < 0) {
        return [...packs, pack].sort(comparePacks);
      }

      const next = [...packs];
      next[index] = pack;
      return next;
    });
  }

  private adjustIconCount(packId: string, delta: number): void {
    this.packs.update(packs =>
      packs.map(p => (p.id === packId ? { ...p, iconCount: Math.max(0, p.iconCount + delta) } : p))
    );
  }

  private dropPackIcons(packId: string): void {
    this.iconsByPack.delete(packId);
    this.loadedPacks.delete(packId);
    this.loadingPacks.delete(packId);
  }

  private iconsSignal(packId: string): WritableSignal<IconModel[]> {
    let icons = this.iconsByPack.get(packId);
    if (!icons) {
      icons = signal<IconModel[]>([]);
      this.iconsByPack.set(packId, icons);
    }

    return icons;
  }
}

export function isTerminalBatchState(state: IconImportBatchState): boolean {
  return TERMINAL_BATCH_STATES.includes(state);
}

function comparePacks(a: IconPackModel, b: IconPackModel): number {
  return Number(b.isDefault) - Number(a.isDefault) || a.name.localeCompare(b.name);
}

function mapPack(pack: IpcIconPack): IconPackModel {
  return {
    id: pack.id,
    name: pack.name,
    description: pack.description,
    author: pack.author,
    version: pack.version,
    isDefault: pack.isDefault,
    isReadOnly: pack.isReadOnly,
    sourceType: pack.sourceType,
    createdAt: pack.createdAt,
    updatedAt: pack.updatedAt,
    iconCount: pack.iconCount ?? 0,
    ownerKind: pack.ownerKind ?? 'User',
    canDelete: pack.canDelete ?? true,
  };
}

function mapIcon(icon: IpcIcon): IconModel {
  return {
    id: icon.id,
    packId: icon.packId,
    name: icon.name,
    width: icon.width,
    height: icon.height,
    isAnimated: icon.isAnimated,
    processingState: icon.processingState,
    processingError: icon.processingError,
    availableSizes: icon.availableSizes ?? [],
    originalFileName: icon.originalFileName,
  };
}

function mapBatch(batch: IpcIconImportBatch): IconImportBatchModel {
  return {
    id: batch.id,
    packId: batch.packId,
    state: batch.state,
    sourceName: batch.sourceName,
    total: batch.total,
    processed: batch.processed,
    failed: batch.failed,
    error: batch.error,
  };
}
