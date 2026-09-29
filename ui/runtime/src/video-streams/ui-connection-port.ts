import type { UiConnection } from '../transport/ui-connection';
import type { VideoStreamPort } from './video-stream-client';

export function uiConnectionVideoStreamPort(connection: UiConnection): VideoStreamPort {
  return {
    request: <T>(type: string, payload: unknown) => connection.request<T>(type, payload),
    onNotification: listener => connection.onNotification(listener),
    onConnectionChanged: listener => {
      let connected = connection.state.get() === 'connected';
      return connection.state.subscribe(state => {
        const now = state === 'connected';
        if (now === connected) return;
        connected = now;
        listener(now);
      });
    },
    connected: () => connection.state.get() === 'connected',
  };
}
