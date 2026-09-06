import { InjectionToken } from '@angular/core';
import { browserLockEnvironment, type CrossTabLockEnvironment } from '@macro-deck/runtime';

export {
  browserLockEnvironment,
  runExclusively,
  type CrossTabLockEnvironment,
  type CrossTabLockStorage,
  type WebLockManager,
} from '@macro-deck/runtime';

export const CROSS_TAB_LOCK_ENVIRONMENT = new InjectionToken<CrossTabLockEnvironment>(
  'CROSS_TAB_LOCK_ENVIRONMENT',
  { providedIn: 'root', factory: () => browserLockEnvironment() },
);
