import { Injectable, inject, signal } from '@angular/core';
import { Announcement, AnnouncementChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';

@Injectable({ providedIn: 'root' })
export class AnnouncementService {
  private readonly api = inject(ApiService);
  private readonly _pending = signal<Announcement | null>(null);
  private generation = 0;

  readonly pending = this._pending.asReadonly();

  constructor() {
    this.api
      .onNotification<AnnouncementChangedEvent>('AnnouncementChangedEvent')
      .subscribe(event => {
        this.generation++;
        this._pending.set(event.announcement ?? null);
      });

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.load();
      }
    });
  }

  async load(): Promise<void> {
    const generation = this.generation;
    try {
      const response = await this.api.getPendingAnnouncement();
      if (generation === this.generation) {
        this._pending.set(response.announcement ?? null);
      }
    } catch {
    }
  }

  dismiss(): void {
    const announcement = this._pending();
    if (!announcement) {
      return;
    }
    this.generation++;
    this._pending.set(null);
    this.api.markAnnouncementSeen({ number: announcement.number }).catch((error: unknown) => {
      console.error('Could not mark the announcement as seen', error);
    });
  }
}
