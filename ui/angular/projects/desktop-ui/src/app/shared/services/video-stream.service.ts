import { Injectable, inject } from '@angular/core';

import { VideoStreamClient, VideoStreamPort, VideoStreamSurface } from '@macro-deck/runtime';

import { ApiService } from '../transport/api.service';

@Injectable({ providedIn: 'root' })
export class VideoStreamService {
  private readonly api = inject(ApiService);
  private client: VideoStreamClient | null = null;

  surface(): VideoStreamSurface {
    this.client ??= new VideoStreamClient(this.port());
    return this.client.surface('desktop');
  }

  private port(): VideoStreamPort {
    return {
      request: (type, payload) => this.api.request(type, payload),
      onNotification: listener => this.api.onAnyNotification(listener),
      onConnectionChanged: listener => {
        let connected = this.api.isConnected;
        const subscription = this.api.connectionState$.subscribe(state => {
          const now = state === 'connected';
          if (now === connected) return;
          connected = now;
          listener(now);
        });
        return () => subscription.unsubscribe();
      },
      connected: () => this.api.isConnected,
    };
  }
}
