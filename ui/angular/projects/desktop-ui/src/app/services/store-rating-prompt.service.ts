import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { StoreRatingPromptCandidateBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { ConnectAccountService } from './connect-account.service';
import { storeReviewErrorMessage } from '../util/store-review-error';

export const STORE_RATING_PROMPT_RETRY_MS = 60 * 1000;
export const STORE_RATING_PROMPT_MAX_RETRIES = 10;
export const STORE_RATING_PROMPT_RECHECK_MS = 6 * 60 * 60 * 1000;

export type StoreRatingSubmitResult = { readonly ok: true } | { readonly ok: false; readonly message: string };

@Injectable({ providedIn: 'root' })
export class StoreRatingPromptService {
  private readonly api = inject(ApiService);
  private readonly account = inject(ConnectAccountService);
  private readonly localization = inject(LocalizationService);

  private readonly _pending = signal<StoreRatingPromptCandidateBody | null>(null);
  private timer: ReturnType<typeof setTimeout> | null = null;
  private retries = 0;
  private checking = false;
  private shown = false;

  readonly pending = this._pending.asReadonly();

  constructor() {
    effect(() => {
      const ready = this.api.connectionStateSignal() === 'connected' && this.account.isSignedIn();
      untracked(() => {
        if (ready) {
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
      const response = await this.api.getStoreRatingPrompt();
      if (!response.ready) {
        this.scheduleRetry();
      } else if (response.candidate) {
        this._pending.set(response.candidate);
      } else {
        this.schedule(STORE_RATING_PROMPT_RECHECK_MS);
      }
    } catch (error) {
      console.error('Could not check whether to ask for a rating', error);
      this.scheduleRetry();
    } finally {
      this.checking = false;
    }
  }

  markShown(): void {
    const candidate = this._pending();
    if (!candidate || this.shown) {
      return;
    }

    this.shown = true;
    this.api.markStoreRatingPromptShown({ kind: candidate.kind, id: candidate.id }).catch((error: unknown) => {
      console.error('Could not record the rating prompt as shown', error);
    });
  }

  dismiss(): void {
    this._pending.set(null);
    this.shown = false;
    this.schedule(STORE_RATING_PROMPT_RECHECK_MS);
  }

  async submit(rating: number, body: string): Promise<StoreRatingSubmitResult> {
    const candidate = this._pending();
    if (!candidate) {
      return { ok: false, message: storeReviewErrorMessage(this.localization, null) };
    }

    try {
      const response = await this.api.putOwnStoreReview(candidate.kind, candidate.id, {
        rating,
        body: body.trim() || null,
      });
      if (!response.success) {
        return { ok: false, message: storeReviewErrorMessage(this.localization, response.error ?? null) };
      }
    } catch (error) {
      console.error('Could not send the rating', error);
      return { ok: false, message: storeReviewErrorMessage(this.localization, null) };
    }

    this.dismiss();
    return { ok: true };
  }

  private scheduleRetry(): void {
    if (this.retries < STORE_RATING_PROMPT_MAX_RETRIES) {
      this.retries++;
      this.schedule(STORE_RATING_PROMPT_RETRY_MS);
    }
  }

  private schedule(delayMs: number): void {
    this.clearTimer();
    this.timer = setTimeout(() => void this.check(), delayMs);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
