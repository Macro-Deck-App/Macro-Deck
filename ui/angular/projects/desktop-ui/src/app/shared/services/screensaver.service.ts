import { Injectable, inject, signal } from '@angular/core';

import { ApiService } from '../transport';
import { type IpcScreenSaver } from '@macro-deck/runtime';

@Injectable({ providedIn: 'root' })
export class ScreenSaverService {
  private readonly api = inject(ApiService);

  private readonly catalog = signal<IpcScreenSaver[]>([]);
  private loaded = false;

  readonly screenSavers = this.catalog.asReadonly();

  constructor() {
    this.api.onScreenSaverCatalogChanged().subscribe(event => this.catalog.set(event.screenSavers ?? []));
  }

  async load(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;

    const response = await this.api.getScreenSavers();
    if (response) this.catalog.set(response.screenSavers ?? []);
  }

  find(screenSaverId: string | null | undefined): IpcScreenSaver | undefined {
    if (!screenSaverId) return undefined;
    return this.catalog().find(entry => entry.id === screenSaverId);
  }
}
