import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { PostUpdateChangelogService } from './post-update-changelog.service';
import { UpdateService } from './update.service';

@Injectable({
  providedIn: 'root',
})
export class UpdateModalService {
  private readonly updates = inject(UpdateService);
  private readonly postUpdateChangelog = inject(PostUpdateChangelogService);

  readonly isOpen = signal(false);

  private announcedAutoInstallAt: number | null = null;

  constructor() {
    effect(() => {
      const autoInstallAt = this.updates.autoInstallAt();
      if (autoInstallAt === null || this.postUpdateChangelog.isOpen()) {
        return;
      }
      untracked(() => {
        if (autoInstallAt !== this.announcedAutoInstallAt) {
          this.announcedAutoInstallAt = autoInstallAt;
          this.open();
        }
      });
    });
  }

  open(): void {
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
  }
}
