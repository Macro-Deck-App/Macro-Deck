import { GetServerTimeResponse, HttpClient } from '@macro-deck/runtime';

export class ServerClock {
  private offsetMs = 0;
  private syncing = false;

  constructor(private readonly http: HttpClient) {
    // A document that was hidden may have been hidden for hours, and a laptop that slept came back
    // with a clock that moved without time passing.
    if (typeof document !== 'undefined' && document.addEventListener) {
      document.addEventListener('visibilitychange', () => {
        if (!document.hidden) void this.sync();
      });
    }
  }

  now(): number {
    return Date.now() + this.offsetMs;
  }

  offset(): number {
    return this.offsetMs;
  }

  sync(): Promise<void> {
    if (this.syncing) return Promise.resolve();
    this.syncing = true;

    const sent = Date.now();
    return this.http.get<GetServerTimeResponse>('/api/system/time').then(
      response => {
        const received = Date.now();
        this.offsetMs = Math.round(response.utcMs + (received - sent) / 2 - received);
        this.syncing = false;
      },
      () => {
        this.syncing = false;
      });
  }
}
