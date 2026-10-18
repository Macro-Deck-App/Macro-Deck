import { DestroyRef, Injectable, inject } from '@angular/core';
import { isOpenableWebUrl } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ExternalLinkService } from './external-link.service';

// The desktop UI runs on the host machine, so the host's browser is the browser of the device the user
// is interacting with.
@Injectable({ providedIn: 'root' })
export class OriginLinkService {
  private readonly links = inject(ExternalLinkService);

  constructor() {
    const subscription = inject(ApiService).onUiOpenUrl().subscribe(event => {
      if (isOpenableWebUrl(event?.url)) this.links.open(event.url);
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }
}
