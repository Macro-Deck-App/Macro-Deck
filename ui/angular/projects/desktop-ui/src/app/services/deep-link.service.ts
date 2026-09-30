import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AppStrings } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { isStorePackageId } from '../util/store-package-id';
import { ShellDeepLinkTarget, shellBridge } from '../util/shell-bridge';
import { STORE_DETAIL_KINDS } from './store-browse-state.service';

// A link that arrives while the registry is still loading (a cold start from the browser) is retried
// for about half a minute before the user is told, instead of failing on the first empty catalog.
export const DEEP_LINK_RETRY_DELAYS_MS: readonly number[] = [1000, 2000, 4000, 8000, 15000];

@Injectable({ providedIn: 'root' })
export class DeepLinkService {
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  private started = false;
  private generation = 0;

  start(): void {
    const bridge = shellBridge();
    if (this.started || !bridge?.onDeepLink || !bridge.takeDeepLinks) {
      return;
    }

    this.started = true;
    // Same handshake as opened files: the shell only says something is waiting, and taking the queue is
    // what delivers it, so a link that arrived before this subscription is picked up by the first take.
    void bridge.onDeepLink(() => void this.take());
    void this.take();
  }

  async open(target: ShellDeepLinkTarget): Promise<void> {
    switch (target.kind) {
      case 'store':
        await this.openStore(target.packageId);
        return;
    }
  }

  private async take(): Promise<void> {
    const targets = await shellBridge()?.takeDeepLinks?.();
    const latest = targets?.at(-1);
    if (latest) {
      await this.open(latest);
    }
  }

  private async openStore(packageId: unknown): Promise<void> {
    // The shell already validated the link; the id is checked again because it becomes a request path.
    if (!isStorePackageId(packageId)) {
      return;
    }

    // A newer link supersedes this one, also while it is still waiting for the registry.
    const generation = ++this.generation;
    for (let attempt = 0; ; attempt++) {
      try {
        const response = await this.api.resolveStoreLink(packageId);
        if (generation !== this.generation) {
          return;
        }
        if (response.kind && response.id && STORE_DETAIL_KINDS.includes(response.kind)) {
          await this.router.navigate(['/store', response.kind, response.id]);
          return;
        }

        if (response.error?.code !== 'registry_unavailable') {
          this.fail(AppStrings.Store.Page.ExtensionNotFound);
          return;
        }
      } catch (error) {
        console.error('Failed to resolve a Store link:', error);
        if (generation !== this.generation) {
          return;
        }
        this.fail(AppStrings.Store.Page.LoadFailed);
        return;
      }

      if (attempt >= DEEP_LINK_RETRY_DELAYS_MS.length) {
        this.fail(AppStrings.Store.Page.LinkRegistryUnavailable);
        return;
      }
      await new Promise(resolve => setTimeout(resolve, DEEP_LINK_RETRY_DELAYS_MS[attempt]));
      if (generation !== this.generation) {
        return;
      }
    }
  }

  private fail(key: string): void {
    this.toasts.show(this.localization.translateKey(key), { variant: 'error' });
  }
}
