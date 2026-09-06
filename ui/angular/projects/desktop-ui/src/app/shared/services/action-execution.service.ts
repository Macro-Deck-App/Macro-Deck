import { Injectable, inject, signal } from '@angular/core';
import { Observable, Subject, filter, merge, of } from 'rxjs';
import {
  type ActionExecutionResult,
  type ActionExecutionStatusEvent,
  AppStrings,
  isFailureExecutionStatus,
  resolveLocalizedText,
} from '@macro-deck/runtime';

import { ApiService } from '../transport';
import { LocalizationService } from '../localization';
import { ToastService } from './toast.service';

const MAX_TRACKED_EXECUTIONS = 50;

function toResult(event: ActionExecutionStatusEvent): ActionExecutionResult {
  return {
    success: event.status === 'Succeeded',
    error: event.error,
    executionId: event.executionId,
    status: event.status,
    durationMs: event.durationMs,
    actions: event.actions,
  };
}

@Injectable({ providedIn: 'root' })
export class ActionExecutionService {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  private readonly results = new Map<string, ActionExecutionResult>();
  private readonly resultReceived = new Subject<ActionExecutionResult>();

  private readonly claimed = new Set<string>();

  private readonly _latest = signal<ActionExecutionResult | null>(null);
  readonly latest = this._latest.asReadonly();

  constructor() {
    this.api.onNotification<ActionExecutionStatusEvent>('ActionExecutionStatusEvent')
      .subscribe(event => this.record(toResult(event)));
  }

  claim(executionId: string): void {
    this.claimed.add(executionId);
  }

  result$(executionId: string): Observable<ActionExecutionResult> {
    const stream = this.resultReceived.pipe(filter(result => result.executionId === executionId));
    const existing = this.results.get(executionId);
    return existing ? merge(of(existing), stream) : stream;
  }

  waitFor(executionId: string, timeoutMs = 15000): Promise<ActionExecutionResult | null> {
    const existing = this.results.get(executionId);
    if (existing) {
      return Promise.resolve(existing);
    }

    return new Promise(resolve => {
      const subscription = this.resultReceived
        .pipe(filter(result => result.executionId === executionId))
        .subscribe(result => {
          clearTimeout(timer);
          subscription.unsubscribe();
          resolve(result);
        });

      const timer = setTimeout(() => {
        subscription.unsubscribe();
        resolve(null);
      }, timeoutMs);
    });
  }

  private record(result: ActionExecutionResult): void {
    if (result.executionId === undefined) {
      return;
    }

    if (!this.results.has(result.executionId) && this.results.size >= MAX_TRACKED_EXECUTIONS) {
      const oldestId = this.results.keys().next().value;
      if (oldestId !== undefined) {
        this.results.delete(oldestId);
      }
    }

    // A fire-and-forget UI-event press (the generic widget-tree pipeline, #748) never gets an
    // executionId back to await - the only signal it ever gets that a press was refused is this
    // pushed event, so a failure nobody already claimed is toasted right here, generically, with no
    // widget-specific code. `FolderService`'s REST round trip claims its own id before this can ever
    // see it, so its own `toastExecutionFailure` is the only one that fires for that path.
    const claimed = this.claimed.delete(result.executionId);
    if (!claimed && isFailureExecutionStatus(result.status)) {
      this.toasts.show(
        resolveLocalizedText(result.error?.message, this.localization)
          || this.localization.translateKey(AppStrings.Errors.Folder.ActionRunFailed),
        { variant: 'error' },
      );
    }

    this.results.set(result.executionId, result);
    this._latest.set(result);
    this.resultReceived.next(result);
  }
}
