import { Injectable, inject, signal } from '@angular/core';

import { ApiService } from '../transport';
import { type IpcFolderView, WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';

@Injectable({ providedIn: 'root' })
export class FolderViewService {
  private readonly api = inject(ApiService);

  private readonly catalog = signal<IpcFolderView[]>([]);
  private loaded = false;

  readonly folderViews = this.catalog.asReadonly();

  constructor() {
    this.api.onFolderViewCatalogChanged().subscribe(event => this.catalog.set(event.folderViews ?? []));
  }

  async load(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;

    const response = await this.api.getFolderViews();
    if (response) this.catalog.set(response.folderViews ?? []);
  }

  async reload(): Promise<void> {
    this.loaded = false;
    await this.load();
  }

  find(viewId: string | null | undefined): IpcFolderView | undefined {
    if (!viewId) return this.catalog().find(view => view.id === WIDGET_GRID_VIEW_ID);
    return this.catalog().find(view => view.id === viewId);
  }
}
