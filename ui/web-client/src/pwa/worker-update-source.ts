import { type UpdateSource } from './app-update';

export const WORKER_MESSAGE = {
  skipWaiting: 'macro-deck.skip-waiting',
  version: 'macro-deck.version',
  unrecoverable: 'macro-deck.unrecoverable',
};

const VERSION_REPLY_TIMEOUT_MS = 2000;

export class WorkerUpdateSource implements UpdateSource {
  readonly enabled = true;

  private readonly versionListeners: Array<(version: string) => void> = [];
  private readonly unrecoverableListeners: Array<() => void> = [];
  private registration: ServiceWorkerRegistration | null = null;
  private announced: string | null = null;

  constructor(
    registration: Promise<ServiceWorkerRegistration>,
    private readonly container: ServiceWorkerContainer,
  ) {
    const self = this;
    container.addEventListener('message', function (event) {
      const data = (event as MessageEvent).data as { type?: string } | null;
      if (data && data.type === WORKER_MESSAGE.unrecoverable) self.emitUnrecoverable();
    });

    void registration.then(function (registered) {
      self.registration = registered;
      registered.addEventListener('updatefound', function () { self.watchInstalling(registered); });
      if (registered.waiting && container.controller) self.announce(registered.waiting);
    }, function () {
      // A registration the browser refuses leaves the client running from the network, which is the
      // same place it started; nothing here can recover it.
    });
  }

  onVersionReady(listener: (version: string) => void): void {
    this.versionListeners.push(listener);
  }

  onUnrecoverable(listener: () => void): void {
    this.unrecoverableListeners.push(listener);
  }

  checkForUpdate(): Promise<boolean> {
    const registration = this.registration;
    if (!registration) return Promise.resolve(false);
    const self = this;
    return registration.update().then(function () {
      const waiting = registration.waiting;
      if (waiting && self.container.controller) {
        self.announce(waiting);
        return true;
      }
      return false;
    });
  }

  activateUpdate(): Promise<void> {
    const registration = this.registration;
    const waiting = registration ? registration.waiting : null;
    if (!waiting) return Promise.reject(new Error('no waiting worker'));

    const container = this.container;
    return new Promise<void>(function (resolve, reject) {
      const onControllerChange = function (): void {
        container.removeEventListener('controllerchange', onControllerChange);
        resolve();
      };
      container.addEventListener('controllerchange', onControllerChange);
      waiting.addEventListener('statechange', function () {
        if (waiting.state === 'redundant') {
          container.removeEventListener('controllerchange', onControllerChange);
          reject(new Error('worker became redundant'));
        }
      });
      waiting.postMessage({ type: WORKER_MESSAGE.skipWaiting });
    });
  }

  private watchInstalling(registration: ServiceWorkerRegistration): void {
    const installing = registration.installing;
    if (!installing) return;
    const self = this;
    installing.addEventListener('statechange', function () {
      if (installing.state === 'installed' && self.container.controller) self.announce(installing);
    });
  }

  private announce(worker: ServiceWorker): void {
    const self = this;
    void this.versionOf(worker).then(function (version) {
      if (self.announced === version) return;
      self.announced = version;
      for (let index = 0; index < self.versionListeners.length; index++) {
        self.versionListeners[index](version);
      }
    });
  }

  private versionOf(worker: ServiceWorker): Promise<string> {
    if (typeof MessageChannel !== 'function') return Promise.resolve('pending');
    return new Promise<string>(function (resolve) {
      const channel = new MessageChannel();
      const timer = setTimeout(function () { resolve('pending'); }, VERSION_REPLY_TIMEOUT_MS);
      channel.port1.onmessage = function (event) {
        clearTimeout(timer);
        const data = event.data as { version?: string } | null;
        resolve(data && data.version ? data.version : 'pending');
      };
      try {
        worker.postMessage({ type: WORKER_MESSAGE.version }, [channel.port2]);
      } catch (error) {
        clearTimeout(timer);
        resolve('pending');
      }
    });
  }

  private emitUnrecoverable(): void {
    for (let index = 0; index < this.unrecoverableListeners.length; index++) {
      this.unrecoverableListeners[index]();
    }
  }
}

export const disabledUpdateSource: UpdateSource = {
  enabled: false,
  onVersionReady: function () { /* nothing ever becomes ready */ },
  onUnrecoverable: function () { /* nothing can break */ },
  checkForUpdate: function () { return Promise.resolve(false); },
  activateUpdate: function () { return Promise.resolve(); },
};
