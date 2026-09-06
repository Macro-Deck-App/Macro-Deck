import { Injectable, effect, inject, signal } from '@angular/core';
import { AppStrings, FolderFocusRule, FolderFocusRuleChangedEvent, FolderFocusRuleRemovedEvent, Result, SetFolderFocusRuleRequest } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

type PendingPush =
  | { kind: 'changed'; folderId: string; rules: FolderFocusRule[] }
  | { kind: 'removed'; ruleId: string };

@Injectable({ providedIn: 'root' })
export class FolderFocusRuleService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly rules = signal<FolderFocusRule[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private inFlightLoad: Promise<void> | null = null;

  private loadOverlay: PendingPush[] | null = null;

  constructor() {
    this.subscribeToEvents();

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.load();
      }
    });
  }

  rulesForFolder(folderId: string): FolderFocusRule[] {
    return this.rules().filter(rule => rule.folderId === folderId);
  }

  load(): Promise<void> {
    this.inFlightLoad ??= this.runLoad().finally(() => {
      this.inFlightLoad = null;
    });
    return this.inFlightLoad;
  }

  private async runLoad(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    const overlay: PendingPush[] = [];
    this.loadOverlay = overlay;
    try {
      const response = await this.api.getFolderFocusRules();
      this.rules.set(applyOverlay(response.rules ?? [], overlay));
    } catch (error) {
      console.error('Failed to load folder focus rules:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.FolderFocusRule.LoadFailed));
    } finally {
      this.loadOverlay = null;
      this.isLoading.set(false);
    }
  }

  async save(request: SetFolderFocusRuleRequest): Promise<Result<FolderFocusRule>> {
    try {
      const response = await this.api.setFolderFocusRule(request);
      if (!response.success || !response.rule) {
        return { success: false, error: response.error };
      }
      this.upsertLocal(response.rule);
      return { success: true, data: response.rule };
    } catch (error) {
      console.error('Failed to save folder focus rule:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.FolderFocusRule.SaveFailed),
        },
      };
    }
  }

  async delete(folderId: string, ruleId: string): Promise<Result> {
    try {
      const response = await this.api.deleteFolderFocusRule(folderId, ruleId);
      if (!response.success) {
        return { success: false, error: response.error };
      }
      this.deleteLocal(ruleId);
      return { success: true };
    } catch (error) {
      console.error('Failed to delete folder focus rule:', error);
      return {
        success: false,
        error: {
          code: 'INTERNAL_ERROR',
          message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.FolderFocusRule.DeleteFailed),
        },
      };
    }
  }

  private subscribeToEvents(): void {
    this.api.onNotification<FolderFocusRuleChangedEvent>('FolderFocusRuleChangedEvent').subscribe(evt => {
      if (!evt.folderId) return;
      const rules = evt.rules ?? [];
      this.loadOverlay?.push({ kind: 'changed', folderId: evt.folderId, rules });
      this.applyFolderRuleSet(evt.folderId, rules);
    });

    this.api.onNotification<FolderFocusRuleRemovedEvent>('FolderFocusRuleRemovedEvent').subscribe(evt => {
      if (!evt.ruleId) return;
      this.loadOverlay?.push({ kind: 'removed', ruleId: evt.ruleId });
      this.deleteLocal(evt.ruleId);
    });
  }

  private applyFolderRuleSet(folderId: string, rules: FolderFocusRule[]): void {
    this.rules.update(all => [...all.filter(rule => rule.folderId !== folderId), ...rules]);
  }

  private upsertLocal(rule: FolderFocusRule): void {
    this.rules.update(all => {
      const idx = all.findIndex(r => r.ruleId === rule.ruleId);
      if (idx === -1) return [...all, rule];
      const next = all.slice();
      next[idx] = rule;
      return next;
    });
  }

  private deleteLocal(ruleId: string): void {
    this.rules.update(all => all.filter(r => r.ruleId !== ruleId));
  }
}

function applyOverlay(snapshot: FolderFocusRule[], overlay: PendingPush[]): FolderFocusRule[] {
  let result = snapshot;
  for (const push of overlay) {
    result = push.kind === 'changed'
      ? [...result.filter(rule => rule.folderId !== push.folderId), ...push.rules]
      : result.filter(rule => rule.ruleId !== push.ruleId);
  }
  return result;
}
