import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '@shared';

export type OnboardingState = 'unknown' | 'pending' | 'done';

/**
 * The one-shot onboarding wizard shown after first-run account setup (issue #893).
 *
 * The flag lives in the host rather than in localStorage, and `AuthService.Setup` arms it, so an
 * installation that already had an account before this feature existed is never owed a wizard.
 * `unknown` is a distinct state on purpose: a failed or still-running load must not read as "done",
 * or the Macro Deck 2 migration offer would present itself in front of a wizard that is still owed. The
 * price is that a load which fails and is never retried - nothing reconnects - costs this session that
 * offer too; it stays armed for the next launch rather than being shown out of order.
 */
@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly api = inject(ApiService);

  readonly state = signal<OnboardingState>('unknown');
  readonly visible = signal(false);

  private loading: Promise<void> | null = null;
  private completing = false;

  async load(): Promise<void> {
    this.loading ??= this.loadOnce();
    await this.loading;
  }

  present(): void {
    if (this.state() !== 'pending') {
      return;
    }

    this.visible.set(true);
  }

  /** Skipped or finished - both mean the wizard must never be shown again. */
  async complete(): Promise<void> {
    if (this.completing || this.state() === 'done') {
      return;
    }

    this.completing = true;
    this.visible.set(false);
    try {
      await this.api.completeOnboarding();
    } catch {
      // The wizard still closes for this session; the host flag stays armed, so it returns on the
      // next launch rather than the user being stuck in a modal that will not go away.
    } finally {
      this.state.set('done');
    }
  }

  private async loadOnce(): Promise<void> {
    try {
      const response = await this.api.getOnboardingState();
      this.state.set(response.pending ? 'pending' : 'done');
    } catch {
      this.loading = null;
    }
  }
}
