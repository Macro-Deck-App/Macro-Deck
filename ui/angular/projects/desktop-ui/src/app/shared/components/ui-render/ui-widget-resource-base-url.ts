import { Injectable, inject } from '@angular/core';

import { HOST_URL_RESOLVER } from '../../transport';

@Injectable({ providedIn: 'root' })
export class UiWidgetResourceBaseUrl {
  private readonly resolveHostUrl = inject(HOST_URL_RESOLVER);
  private pending: Promise<string | null> | null = null;

  private resolved: string | null = null;

  get(): Promise<string | null> {
    if (this.pending === null) {
      this.pending = this.resolveHostUrl().then(url => {
        this.resolved = url;
        return url;
      });
    }
    return this.pending;
  }

  get current(): string | null {
    return this.resolved;
  }
}
