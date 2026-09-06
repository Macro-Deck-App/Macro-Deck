import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, ToastService } from '@shared';
import { ArchiveDropKind, archiveDropKind } from '../domain/archive-drop.util';
import { IconPackService } from './icon-pack.service';
import { shellBridge } from '../util/shell-bridge';

export interface PendingFileOpen {
  kind: ArchiveDropKind;
  path: string;
}

@Injectable({ providedIn: 'root' })
export class FileOpenService {
  private readonly router = inject(Router);
  private readonly iconPacks = inject(IconPackService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly pending = signal<readonly PendingFileOpen[]>([]);

  private started = false;

  start(): void {
    const bridge = shellBridge();
    if (this.started || !bridge?.onFileOpen) {
      return;
    }

    this.started = true;
    // The shell only signals that something is waiting; taking the queue is what delivers it, so a
    // file opened before this subscription existed is picked up by the very first take.
    void bridge.onFileOpen(() => void this.take());
    void this.take();
  }

  private async take(): Promise<void> {
    const paths = await shellBridge()?.takeOpenedFiles?.();
    if (paths?.length) {
      await this.accept(paths);
    }
  }

  claim(kind: ArchiveDropKind): PendingFileOpen | null {
    const waiting = this.pending();
    const index = waiting.findIndex(entry => entry.kind === kind);
    if (index < 0) {
      return null;
    }

    this.pending.set(waiting.filter((_, position) => position !== index));
    return waiting[index];
  }

  async accept(paths: readonly string[]): Promise<void> {
    for (const path of paths) {
      const kind = archiveDropKind(path);
      if (!kind) {
        continue;
      }

      if (kind === 'iconPack') {
        await this.importIconPack(path);
        continue;
      }

      // Queued before navigating, never after: the page that claims it is built during the
      // navigation, and navigating to the route the user is already on does nothing at all.
      this.pending.set([...this.pending(), { kind, path }]);

      if (kind === 'plugin') {
        await this.router.navigate(['/integrations']);
        continue;
      }

      if (kind !== 'profile') {
        await this.router.navigate(['/deck']);
      }
    }
  }

  private async importIconPack(path: string): Promise<void> {
    const name = await this.iconPacks.restoreFromPath(path);
    if (name) {
      this.toasts.show(this.localization.translateKey(AppStrings.Shell.FileOpen.IconPackImported), { detail: name });
    } else {
      this.toasts.show(this.localization.translateKey(AppStrings.Shell.FileOpen.IconPackImportFailed), { variant: 'error' });
    }
  }
}
