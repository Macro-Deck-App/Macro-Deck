import { Injectable, computed, inject, signal } from '@angular/core';

import { ActionFlow, ApiError, AppStrings, AutomationCreatedEvent, AutomationDeletedEvent, AutomationUpdatedEvent, IpcAutomation, Result } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { Automation } from '../domain/automation.interface';

@Injectable({
  providedIn: 'root',
})
export class AutomationService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly automations = signal<Automation[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly sortedAutomations = computed(() =>
    [...this.automations()].sort((a, b) => a.name.localeCompare(b.name)),
  );

  constructor() {
    this.subscribeToEvents();
  }

  async loadAutomations(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);

    try {
      const response = await this.api.getAutomations();
      this.automations.set((response.automations ?? []).map(automation => mapIpcAutomation(automation)));
    } catch (error) {
      console.error('Failed to load automations:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Automation.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  async createAutomation(name: string, description?: string, flows?: ActionFlow[]): Promise<Result<Automation>> {
    return this.mutate(
      () => this.api.createAutomation({ name, description, flows: serializeFlows(flows ?? []) }),
      this.localization.translateKey(AppStrings.Errors.Automation.CreateFailed),
    );
  }

  async updateAutomation(
    id: string,
    changes: { name?: string; description?: string; flows?: ActionFlow[]; enabled?: boolean },
  ): Promise<Result<Automation>> {
    return this.mutate(
      () => this.api.updateAutomation({
        id,
        name: changes.name,
        description: changes.description,
        flows: changes.flows ? serializeFlows(changes.flows) : undefined,
        enabled: changes.enabled,
      }),
      this.localization.translateKey(AppStrings.Errors.Automation.UpdateFailed),
    );
  }

  async setEnabled(id: string, enabled: boolean): Promise<Result<Automation>> {
    return this.updateAutomation(id, { enabled });
  }

  async duplicateAutomation(id: string): Promise<Result<Automation>> {
    return this.mutate(() => this.api.duplicateAutomation(id), this.localization.translateKey(AppStrings.Errors.Automation.DuplicateFailed));
  }

  async deleteAutomation(id: string): Promise<Result<void>> {
    try {
      const response = await this.api.deleteAutomation(id);
      if (!response.success) {
        return { success: false, error: response.error };
      }

      this.removeFromState(id);
      return { success: true, data: undefined };
    } catch (error) {
      return { success: false, error: toTransportError(error, this.localization.translateKey(AppStrings.Errors.Automation.DeleteFailed)) };
    }
  }

  private subscribeToEvents(): void {
    this.api.onNotification<AutomationCreatedEvent>('AutomationCreatedEvent').subscribe(event => {
      if (event.automation) {
        this.upsert(mapIpcAutomation(event.automation));
      }
    });

    this.api.onNotification<AutomationUpdatedEvent>('AutomationUpdatedEvent').subscribe(event => {
      if (event.automation) {
        this.upsert(mapIpcAutomation(event.automation));
      }
    });

    this.api.onNotification<AutomationDeletedEvent>('AutomationDeletedEvent').subscribe(event => {
      if (event.id) {
        this.removeFromState(event.id);
      }
    });
  }

  private async mutate(
    call: () => Promise<{ success: boolean; automation?: IpcAutomation; error?: ApiError }>,
    failureMessage: string,
  ): Promise<Result<Automation>> {
    try {
      const response = await call();
      if (!response.success) {
        return { success: false, error: response.error };
      }
      if (!response.automation) {
        return { success: false, error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Automation.NoAutomationReturned) } };
      }

      const automation = mapIpcAutomation(response.automation);
      this.upsert(automation);
      return { success: true, data: automation };
    } catch (error) {
      console.error(`${failureMessage}:`, error);
      return { success: false, error: toTransportError(error, failureMessage) };
    }
  }

  private upsert(automation: Automation): void {
    this.automations.update(automations =>
      automations.some(a => a.id === automation.id)
        ? automations.map(a => (a.id === automation.id ? automation : a))
        : [...automations, automation],
    );
  }

  private removeFromState(id: string): void {
    this.automations.update(automations => automations.filter(automation => automation.id !== id));
  }
}

function mapIpcAutomation(automation: IpcAutomation): Automation {
  return {
    id: automation.id,
    name: automation.name,
    description: automation.description ?? '',
    enabled: automation.enabled ?? true,
    flows: parseFlows(automation.flows),
    createdAt: automation.createdAt,
    updatedAt: automation.updatedAt,
  };
}

function parseFlows(flows: string | undefined): ActionFlow[] {
  if (!flows) {
    return [];
  }

  try {
    const parsed: unknown = JSON.parse(flows);
    return Array.isArray(parsed) ? (parsed as ActionFlow[]) : [];
  } catch {
    // A hand-edited or truncated automation file must not take the whole page down; an empty flow is
    // visibly wrong in the editor, which is the honest outcome.
    console.warn('Failed to parse automation flows');
    return [];
  }
}

function serializeFlows(flows: ActionFlow[]): string {
  return JSON.stringify(flows);
}

function toTransportError(error: unknown, fallback: string): ApiError {
  return { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : fallback };
}
