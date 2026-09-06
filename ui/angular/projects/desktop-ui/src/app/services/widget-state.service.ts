import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { WidgetStateUpdatedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';

interface StateEntry {
  stateId: WritableSignal<string | null>;
  refCount: number;
}

@Injectable({ providedIn: 'root' })
export class WidgetStateService {
  private readonly api = inject(ApiService);
  private readonly entries = new Map<string, StateEntry>();

  constructor() {
    this.api.onNotification<WidgetStateUpdatedEvent>('WidgetStateUpdatedEvent').subscribe(e => {
      this.entries.get(e.widgetId)?.stateId.set(e.stateId);
    });

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        for (const [widgetId, entry] of this.entries) {
          if (entry.refCount > 0) {
            this.invokeSubscribe(widgetId);
          }
        }
      }
    });
  }

  subscribe(widgetId: string): Signal<string | null> {
    let entry = this.entries.get(widgetId);
    if (!entry) {
      entry = { stateId: signal<string | null>(null), refCount: 0 };
      this.entries.set(widgetId, entry);
    }
    entry.refCount++;
    if (entry.refCount === 1) {
      this.invokeSubscribe(widgetId);
    }
    return entry.stateId.asReadonly();
  }

  release(widgetId: string): void {
    const entry = this.entries.get(widgetId);
    if (!entry) {
      return;
    }
    entry.refCount = Math.max(0, entry.refCount - 1);
    if (entry.refCount === 0) {
      void this.api.invoke('UnsubscribeWidgetState', widgetId);
    }
  }

  private invokeSubscribe(widgetId: string): void {
    void this.api.invokeResult<WidgetStateUpdatedEvent>('SubscribeWidgetState', widgetId).then(result => {
      if (result) {
        this.entries.get(widgetId)?.stateId.set(result.stateId);
      }
    });
  }
}
