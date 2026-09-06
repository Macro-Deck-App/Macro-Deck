import { Folder } from '../domain/folder.interface';
import { collectDisplayedWidgets } from '../domain/pinned-widget.util';
import { GridWidget } from '../domain/widget.interface';
import { store, ReadableStore, WritableStore } from '../store/store';

export interface DeckLocation {
  folderId: string | null;
  history: readonly string[];
}

export class DeckState {
  private readonly foldersStore: WritableStore<readonly Folder[]> = store<readonly Folder[]>([]);
  private readonly locationStore: WritableStore<DeckLocation> =
    store<DeckLocation>({ folderId: null, history: [] });

  get folders(): ReadableStore<readonly Folder[]> {
    return this.foldersStore;
  }

  get location(): ReadableStore<DeckLocation> {
    return this.locationStore;
  }

  get currentFolder(): Folder | undefined {
    const id = this.locationStore.get().folderId;
    return id === null ? undefined : this.findFolder(id);
  }

  get displayedWidgets(): GridWidget[] {
    return collectDisplayedWidgets(this.foldersStore.get(), this.locationStore.get().folderId);
  }

  widget(widgetId: string): GridWidget | undefined {
    const displayed = this.displayedWidgets;
    for (let index = 0; index < displayed.length; index++) {
      if (displayed[index].id === widgetId) return displayed[index];
    }
    return undefined;
  }

  folder(folderId: string): Folder | undefined {
    return this.findFolder(folderId);
  }

  load(folders: readonly Folder[], openFolderId?: string): void {
    this.foldersStore.set(folders.slice());

    if (openFolderId !== undefined && this.findFolder(openFolderId)) {
      // No trail: the way back leads into the deck that was just replaced.
      this.locationStore.set({ folderId: openFolderId, history: [] });
      return;
    }

    const current = this.locationStore.get().folderId;
    if (current !== null && this.findFolder(current)) return;

    const start = folders.find(folder => folder.isDefault) ?? folders[0];
    this.locationStore.set({ folderId: start ? start.id : null, history: [] });
  }

  openFolder(folderId: string): void {
    const location = this.locationStore.get();
    if (location.folderId === folderId) return;

    const history = location.folderId === null
      ? location.history
      : location.history.concat([location.folderId]);
    this.locationStore.set({ folderId, history });
  }

  get canGoBack(): boolean {
    return this.locationStore.get().history.length > 0;
  }

  back(): void {
    const location = this.locationStore.get();
    if (location.history.length === 0) return;

    const history = location.history.slice();
    const previous = history.pop()!;
    this.locationStore.set({ folderId: previous, history });
  }

  parent(): void {
    const folder = this.currentFolder;
    if (!folder || folder.parentId === null) return;
    this.locationStore.set({ folderId: folder.parentId, history: [] });
  }

  folderUpserted(folder: Folder): void {
    const folders = this.foldersStore.get();
    const at = folders.findIndex(candidate => candidate.id === folder.id);
    if (at < 0) {
      this.foldersStore.set(folders.concat([folder]));
      return;
    }
    // The echo carries the whole folder, so it replaces rather than merges.
    const next = folders.slice();
    next[at] = folder;
    this.foldersStore.set(next);
  }

  folderDeleted(folderId: string): void {
    this.foldersStore.set(this.foldersStore.get().filter(folder => folder.id !== folderId));

    const location = this.locationStore.get();
    const history = location.history.filter(id => id !== folderId);
    if (location.folderId !== folderId) {
      if (history.length !== location.history.length) {
        this.locationStore.set({ folderId: location.folderId, history });
      }
      return;
    }

    // The folder underfoot is gone; fall back the way the user came, then to the default.
    const fallback = history.length > 0
      ? history[history.length - 1]
      : (this.foldersStore.get().find(folder => folder.isDefault) ?? this.foldersStore.get()[0])?.id
        ?? null;
    this.locationStore.set({
      folderId: fallback,
      history: history.length > 0 ? history.slice(0, -1) : [],
    });
  }

  widgetsUpserted(folderId: string, widgets: readonly GridWidget[]): void {
    this.mutateFolder(folderId, folder => {
      const next = folder.widgets.slice();
      for (let index = 0; index < widgets.length; index++) {
        const widget = widgets[index];
        const at = next.findIndex(candidate => candidate.id === widget.id);
        if (at < 0) next.push(widget);
        else next[at] = widget;
      }
      return { ...folder, widgets: next };
    });
  }

  widgetDeleted(folderId: string, widgetId: string): void {
    this.mutateFolder(folderId, folder => ({
      ...folder,
      widgets: folder.widgets.filter(widget => widget.id !== widgetId),
    }));
  }

  private mutateFolder(folderId: string, change: (folder: Folder) => Folder): void {
    const folders = this.foldersStore.get();
    const at = folders.findIndex(folder => folder.id === folderId);
    if (at < 0) return;

    const next = folders.slice();
    next[at] = change(folders[at]);
    this.foldersStore.set(next);
  }

  private findFolder(folderId: string): Folder | undefined {
    const folders = this.foldersStore.get();
    for (let index = 0; index < folders.length; index++) {
      if (folders[index].id === folderId) return folders[index];
    }
    return undefined;
  }
}
