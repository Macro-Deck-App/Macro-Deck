import { Injectable, inject, signal } from '@angular/core';
import { WeatherInstanceDto, WeatherInstancesChangedNotification, WeatherStateChangedNotification, WeatherStatePayload } from '@macro-deck/runtime';
import { ApiService } from '@shared';

export const UNAVAILABLE_WEATHER: WeatherStatePayload = {
  isAvailable: false,
  locationName: '',
  condition: 'unknown',
  isDay: true,
  unit: 'celsius',
  days: [],
};

@Injectable({ providedIn: 'root' })
export class WeatherService {
  private static readonly RETRY_BASE_DELAY_MS = 2000;
  private static readonly RETRY_MAX_DELAY_MS = 30000;
  private static readonly RETRY_MAX_ATTEMPTS = 5;

  private readonly api = inject(ApiService);

  readonly instances = signal<WeatherInstanceDto[]>([]);
  readonly states = signal<Record<string, WeatherStatePayload>>({});

  private started = false;

  private readonly pulled = new Set<string>();
  private readonly healed = new Set<string>();
  private loadingInstances: Promise<void> | null = null;

  private instancesGeneration = 0;

  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryAttempt = 0;

  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    this.api
      .onNotification<WeatherStateChangedNotification>('WeatherStateChangedNotification')
      .subscribe(notification => this.applyState(notification.state));

    this.api
      .onNotification<WeatherInstancesChangedNotification>('WeatherInstancesChangedNotification')
      .subscribe(notification => this.setInstances(notification.instances ?? []));

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.resync();
      }
    });
  }

  async loadInstances(): Promise<void> {
    // Single-flighted: several widgets mounting at once (or a burst of state pushes) must not each
    // issue their own hub invoke.
    this.loadingInstances ??= this.fetchInstances().finally(() => (this.loadingInstances = null));
    return this.loadingInstances;
  }

  async ensureState(instanceId: string): Promise<void> {
    if (this.pulled.has(instanceId)) {
      return;
    }
    this.pulled.add(instanceId);

    const response = await this.api.getWeatherState(instanceId);
    if (response?.state) {
      this.applyState(response.state);
      return;
    }

    this.pulled.delete(instanceId);
  }

  stateFor(instanceId: string | undefined | null): WeatherStatePayload {
    if (!instanceId) {
      return UNAVAILABLE_WEATHER;
    }
    return this.states()[instanceId] ?? UNAVAILABLE_WEATHER;
  }

  instance(instanceId: string | undefined | null): WeatherInstanceDto | undefined {
    return instanceId ? this.instances().find(i => i.instanceId === instanceId) : undefined;
  }

  private async resync(): Promise<void> {
    this.pulled.clear();
    this.healed.clear();
    this.clearRetry();
    await this.loadInstances();

    const ids = new Set([...this.instances().map(i => i.instanceId), ...Object.keys(this.states())]);
    await Promise.all([...ids].map(id => this.ensureState(id)));
  }

  private async fetchInstances(): Promise<void> {
    const generation = this.instancesGeneration;
    const response = await this.api.getWeatherInstances();

    // A null result means the realtime connection wasn't ready or the request failed (e.g. mid-reconnect) - that is
    // "unknown", not "no locations". Keep the previous list so the widget doesn't flash "No location"
    // (issue #94), and retry: on a stable connection nothing else would, because the host pushes the
    // list only when it changes (issue #132). A real (possibly empty) response still replaces the
    // list, so removing the last location still clears an unbound widget (a widget bound to the
    // removed station gets the host's "Location unavailable" card instead, which is the honest one).
    if (!response) {
      this.scheduleRetry();
      return;
    }

    this.clearRetry();
    if (generation !== this.instancesGeneration) {
      return;
    }

    this.setInstances(response.instances ?? []);
  }

  private setInstances(instances: WeatherInstanceDto[]): void {
    this.instancesGeneration++;
    this.instances.set(instances);
    for (const instance of instances) {
      this.healed.delete(instance.instanceId);
    }
  }

  private applyState(state: WeatherStatePayload | undefined): void {
    const instanceId = state?.instanceId;
    if (!state || !instanceId) {
      return;
    }
    this.states.update(current => ({ ...current, [instanceId]: state }));

    // A state for a station the list does not know means the list is wrong, not that a new station
    // appeared unannounced: the host pushes both from the same loop. It has no reason to re-announce
    // an unchanged list, so this push is the only correction signal a stranded client ever gets -
    // reload once per unknown id, so a station that really is gone cannot loop.
    if (!this.healed.has(instanceId) && !this.instances().some(i => i.instanceId === instanceId)) {
      this.healed.add(instanceId);
      void this.loadInstances();
    }
  }

  private scheduleRetry(): void {
    if (this.retryTimer !== null || this.retryAttempt >= WeatherService.RETRY_MAX_ATTEMPTS) {
      return;
    }
    const delay = Math.min(
      WeatherService.RETRY_BASE_DELAY_MS * 2 ** this.retryAttempt,
      WeatherService.RETRY_MAX_DELAY_MS,
    );
    this.retryAttempt++;
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.loadInstances();
    }, delay);
  }

  private clearRetry(): void {
    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    this.retryAttempt = 0;
  }
}
