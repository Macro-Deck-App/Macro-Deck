import { isSecureContext } from '@macro-deck/runtime';
import { AppUpdate } from './app-update';
import { PwaInstall } from './pwa-install';
import { shouldRegisterServiceWorker } from './service-worker-registration';
import { WorkerUpdateSource, disabledUpdateSource } from './worker-update-source';

export { AppUpdate, type AppUpdateOptions, type AppUpdatePhase, type UpdateSource } from './app-update';
export { detectPlatform, type Platform, type PlatformNavigator } from './platform';
export {
  PwaInstall,
  type PwaAvailability,
  type PwaInstallOptions,
  type PwaInstallOutcome,
} from './pwa-install';
export { shouldRegisterServiceWorker, type ServiceWorkerEnvironment } from './service-worker-registration';
export { installHintKey, installStatusKey, updateActionKey, updateStatusKey } from './strings';
export { WorkerUpdateSource, disabledUpdateSource, WORKER_MESSAGE } from './worker-update-source';

export const APP_WORKER_URL = 'macro-deck-worker.js';

export interface PwaSetupOptions {
  devMode?: boolean;
  workerUrl?: string;
}

export interface Pwa {
  readonly update: AppUpdate;
  readonly install: PwaInstall;
  readonly workerRegistered: boolean;
  destroy(): void;
}

export function setupPwa(options?: PwaSetupOptions): Pwa {
  const settings = options || {};
  const install = new PwaInstall();

  const container = navigator.serviceWorker as ServiceWorkerContainer | undefined;
  const allowed = shouldRegisterServiceWorker(
    { isSecureContext: isSecureContext(), navigator: { serviceWorker: container } },
    settings.devMode === true,
  );

  let update: AppUpdate;
  let workerRegistered = false;
  if (allowed && container) {
    const url = settings.workerUrl || APP_WORKER_URL;
    update = new AppUpdate(new WorkerUpdateSource(container.register(url), container));
    workerRegistered = true;
  } else {
    update = new AppUpdate(disabledUpdateSource);
  }

  return {
    update: update,
    install: install,
    workerRegistered: workerRegistered,
    destroy: function () {
      update.destroy();
      install.destroy();
    },
  };
}
