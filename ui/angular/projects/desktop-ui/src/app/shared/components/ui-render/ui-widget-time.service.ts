import { DestroyRef, Injectable, Signal, inject, signal } from '@angular/core';

import { ServerClockService } from '../../services/server-clock.service';

@Injectable({ providedIn: 'root' })
export class UiWidgetTimeService {
  private readonly serverClock = inject(ServerClockService);
  private readonly current = signal(new Date());
  private handle: ReturnType<typeof setTimeout> | null = null;
  private subscribers = 0;

  readonly instant: Signal<Date> = this.current.asReadonly();

  attach(destroyRef: DestroyRef): void {
    this.subscribers++;
    if (this.subscribers === 1) this.start();

    destroyRef.onDestroy(() => {
      this.subscribers--;
      if (this.subscribers === 0) this.stop();
    });
  }

  private start(): void {
    const tick = () => {
      const nowMs = this.serverClock.now();
      this.current.set(new Date(nowMs));
      this.handle = setTimeout(tick, 1000 - (nowMs % 1000));
    };

    tick();
  }

  private stop(): void {
    if (this.handle !== null) clearTimeout(this.handle);
    this.handle = null;
  }
}
