import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../transport';

export interface IconImageVersion {
  id: string;
  contentHash?: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class IconImageService {
  private readonly api = inject(ApiService);
  private readonly versions = signal<ReadonlyMap<string, string>>(new Map());

  getIconUrl(iconId: string | null | undefined, size?: number, version?: string | null): string | null {
    return iconId ? this.api.getIconImageUrl(iconId, size, version ?? this.versions().get(iconId)) : null;
  }

  rememberVersions(icons: readonly IconImageVersion[]): void {
    const changed = icons.filter(icon => (icon.contentHash ?? undefined) !== this.versions().get(icon.id));
    if (changed.length === 0) {
      return;
    }

    this.versions.update(current => {
      const next = new Map(current);
      for (const icon of changed) {
        if (icon.contentHash) {
          next.set(icon.id, icon.contentHash);
        } else {
          next.delete(icon.id);
        }
      }
      return next;
    });
  }
}
