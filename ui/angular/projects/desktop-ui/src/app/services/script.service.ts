import { Injectable, computed, inject, signal } from '@angular/core';
import { ActionFlow, ApiError, AppStrings, IpcScript, Result, RunScriptResponse, Script, ScriptCreatedEvent, ScriptDeletedEvent, ScriptInput, ScriptInputValue, ScriptUpdatedEvent, ScriptUsage, normalizeScriptInputs } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({
  providedIn: 'root',
})
export class ScriptService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly scripts = signal<Script[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly sortedScripts = computed(() =>
    [...this.scripts()].sort((a, b) => a.name.localeCompare(b.name)),
  );

  constructor() {
    this.subscribeToEvents();
  }

  async loadScripts(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);

    try {
      const response = await this.api.getScripts();
      this.scripts.set((response.scripts ?? []).map(script => mapIpcScript(script)));
    } catch (error) {
      console.error('Failed to load scripts:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Script.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  async createScript(name: string, description?: string, flows?: ActionFlow[]): Promise<Result<Script>> {
    return this.mutate(
      () => this.api.createScript({ name, description, flows: serializeFlows(flows ?? []) }),
      this.localization.translateKey(AppStrings.Errors.Script.CreateFailed),
    );
  }

  async updateScript(
    id: string,
    changes: {
      name?: string;
      description?: string;
      flows?: ActionFlow[];
      inputs?: ScriptInput[];
      runsOnWidget?: boolean;
    },
  ): Promise<Result<Script>> {
    return this.mutate(
      () => this.api.updateScript({
        id,
        name: changes.name,
        description: changes.description,
        flows: changes.flows ? serializeFlows(changes.flows) : undefined,
        inputs: changes.inputs,
        runsOnWidget: changes.runsOnWidget,
      }),
      this.localization.translateKey(AppStrings.Errors.Script.UpdateFailed),
    );
  }

  async duplicateScript(id: string): Promise<Result<Script>> {
    return this.mutate(() => this.api.duplicateScript(id), this.localization.translateKey(AppStrings.Errors.Script.DuplicateFailed));
  }

  async deleteScript(id: string): Promise<Result<void>> {
    try {
      const response = await this.api.deleteScript(id);
      if (!response.success) {
        return { success: false, error: response.error };
      }

      this.removeFromState(id);
      return { success: true, data: undefined };
    } catch (error) {
      return { success: false, error: toTransportError(error, this.localization.translateKey(AppStrings.Errors.Script.DeleteFailed)) };
    }
  }

  async getUsages(id: string): Promise<ScriptUsage[]> {
    try {
      const response = await this.api.getScriptUsages(id);
      return response.usages ?? [];
    } catch (error) {
      console.error('Failed to load script usages:', error);
      return [];
    }
  }

  async runScript(id: string, inputs?: Record<string, ScriptInputValue>): Promise<Result<RunScriptResponse>> {
    try {
      const response = await this.api.runScript({ id, clientId: this.api.clientId, inputs });
      return response.success
        ? { success: true, data: response }
        : { success: false, error: response.error };
    } catch (error) {
      return { success: false, error: toTransportError(error, this.localization.translateKey(AppStrings.Errors.Script.RunFailed)) };
    }
  }

  private subscribeToEvents(): void {
    this.api.onNotification<ScriptCreatedEvent>('ScriptCreatedEvent').subscribe(event => {
      if (event.script) {
        this.upsert(mapIpcScript(event.script));
      }
    });

    this.api.onNotification<ScriptUpdatedEvent>('ScriptUpdatedEvent').subscribe(event => {
      if (event.script) {
        this.upsert(mapIpcScript(event.script));
      }
    });

    this.api.onNotification<ScriptDeletedEvent>('ScriptDeletedEvent').subscribe(event => {
      if (event.id) {
        this.removeFromState(event.id);
      }
    });
  }

  private async mutate(
    call: () => Promise<{ success: boolean; script?: IpcScript; error?: ApiError }>,
    failureMessage: string,
  ): Promise<Result<Script>> {
    try {
      const response = await call();
      if (!response.success) {
        return { success: false, error: response.error };
      }
      if (!response.script) {
        return { success: false, error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Script.NoScriptReturned) } };
      }

      const script = mapIpcScript(response.script);
      this.upsert(script);
      return { success: true, data: script };
    } catch (error) {
      console.error(`${failureMessage}:`, error);
      return { success: false, error: toTransportError(error, failureMessage) };
    }
  }

  private upsert(script: Script): void {
    this.scripts.update(scripts =>
      scripts.some(s => s.id === script.id)
        ? scripts.map(s => (s.id === script.id ? script : s))
        : [...scripts, script],
    );
  }

  private removeFromState(id: string): void {
    this.scripts.update(scripts => scripts.filter(script => script.id !== id));
  }
}

function mapIpcScript(script: IpcScript): Script {
  return {
    id: script.id,
    name: script.name,
    description: script.description ?? '',
    flows: parseFlows(script.flows),
    inputs: normalizeScriptInputs(script.inputs),
    runsOnWidget: script.runsOnWidget ?? false,
    createdAt: script.createdAt,
    updatedAt: script.updatedAt,
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
    // A hand-edited or truncated script file must not take the whole page down; an empty flow is
    // visibly wrong in the editor, which is the honest outcome.
    console.warn('Failed to parse script flows');
    return [];
  }
}

function serializeFlows(flows: ActionFlow[]): string {
  return JSON.stringify(flows);
}

function toTransportError(error: unknown, fallback: string): ApiError {
  return { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : fallback };
}
