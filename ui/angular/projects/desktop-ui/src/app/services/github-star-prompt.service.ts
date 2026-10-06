import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { ApiService } from '@shared';

export const GITHUB_STAR_PROMPT_RETRY_MS = 60 * 1000;
export const GITHUB_STAR_PROMPT_MAX_RETRIES = 10;

@Injectable({ providedIn: 'root' })
export class GitHubStarPromptService {
  private readonly api = inject(ApiService);

  private readonly _pending = signal(false);
  private timer: ReturnType<typeof setTimeout> | null = null;
  private retries = 0;
  private checking = false;
  private shown = false;

  readonly pending = this._pending.asReadonly();

  constructor() {
    effect(() => {
      const connected = this.api.connectionStateSignal() === 'connected';
      untracked(() => {
        if (connected) {
          this.retries = 0;
          void this.check();
        } else {
          this.clearTimer();
        }
      });
    });
  }

  async check(): Promise<void> {
    if (this.checking || this.shown || this._pending()) {
      return;
    }

    this.clearTimer();
    this.checking = true;
    try {
      const response = await this.api.getGitHubStarPrompt();
      this._pending.set(response.due);
    } catch (error) {
      console.error('Could not check whether to ask for a GitHub star', error);
      this.scheduleRetry();
    } finally {
      this.checking = false;
    }
  }

  markShown(): void {
    if (!this._pending() || this.shown) {
      return;
    }

    this.shown = true;
    this.api.markGitHubStarPromptShown().catch((error: unknown) => {
      console.error('Could not record the GitHub star prompt as shown', error);
    });
  }

  dismiss(): void {
    this._pending.set(false);
  }

  private scheduleRetry(): void {
    if (this.retries < GITHUB_STAR_PROMPT_MAX_RETRIES) {
      this.retries++;
      this.clearTimer();
      this.timer = setTimeout(() => void this.check(), GITHUB_STAR_PROMPT_RETRY_MS);
    }
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
