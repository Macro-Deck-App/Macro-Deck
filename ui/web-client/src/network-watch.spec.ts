import { startNetworkWatch } from './network-watch';

describe('startNetworkWatch', () => {
  const fakeWindow = () => {
    const listeners: { [type: string]: Array<() => void> } = {};
    return {
      addEventListener: (type: string, listener: () => void) => {
        (listeners[type] = listeners[type] || []).push(listener);
      },
      removeEventListener: (type: string, listener: () => void) => {
        listeners[type] = (listeners[type] || []).filter(entry => entry !== listener);
      },
      fire: (type: string) => { (listeners[type] || []).forEach(listener => listener()); },
    } as unknown as Window & { fire(type: string): void };
  };

  it('calls retryNow once for each online event', () => {
    const win = fakeWindow();
    let calls = 0;
    startNetworkWatch({ retryNow: () => { calls++; } }, { win });

    win.fire('online');

    expect(calls).toBe(1);
  });

  it('stops calling retryNow once torn down', () => {
    const win = fakeWindow();
    let calls = 0;
    const stop = startNetworkWatch({ retryNow: () => { calls++; } }, { win });

    stop();
    win.fire('online');

    expect(calls).toBe(0);
  });
});
