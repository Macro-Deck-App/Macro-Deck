import { Injectable, computed, inject } from '@angular/core';
import { ConnectAccountService } from './connect-account.service';

const STORE_TESTER_ROLE = 'StoreTester';

// Temporary: the store is not live, so only store testers get past the coming-soon overlay. Remove
// this gate, the overlay and the ComingSoon strings once the store goes live.
@Injectable({ providedIn: 'root' })
export class StoreAccessService {
  private readonly account = inject(ConnectAccountService);

  readonly unlocked = computed(() =>
    this.account.isSignedIn() && (this.account.session()?.account?.roles ?? []).includes(STORE_TESTER_ROLE));
}
