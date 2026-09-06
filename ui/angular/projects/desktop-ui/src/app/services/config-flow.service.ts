import { Injectable, inject, signal, computed } from '@angular/core';
import { filter, take } from 'rxjs';
import { ActionParameterType, AppStrings, ConfigFlowAuthorizedNotification, ConfigFlowStepDto, LocalizedText, UiConfigEntryPoints, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService, UiSessionHandle, UiSessionService } from '@shared';
import type { UiNode, UiNodeEvent } from '@macro-deck/runtime';
import { ExternalLinkService } from './external-link.service';

@Injectable({ providedIn: 'root' })
export class ConfigFlowService {
  private readonly api = inject(ApiService);
  private readonly externalLinks = inject(ExternalLinkService);
  private readonly localization = inject(LocalizationService);
  private readonly uiSessions = inject(UiSessionService);

  private readonly session = signal<UiSessionHandle | null>(null);

  readonly root = computed<UiNode | null>(() => this.session()?.root() ?? null);

  readonly integrationId = signal<string | null>(null);
  readonly flowId = signal<string | null>(null);
  readonly step = signal<ConfigFlowStepDto | null>(null);
  readonly values = signal<Record<string, unknown>>({});
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly message = signal<string | null>(null);
  readonly storedSecretFields = signal<ReadonlySet<string>>(new Set());
  private readonly clearedSecretFields = signal<ReadonlySet<string>>(new Set());

  readonly starting = signal(false);
  readonly submitting = signal(false);
  readonly notSupported = signal(false);
  readonly done = signal(false);

  readonly waitingForAuth = signal(false);

  readonly canSubmit = computed(() => {
    const step = this.step();
    if (!step) return false;
    const values = this.values();
    return step.fields.every(field => {
      if (!field.required) return true;
      const value = values[field.name];
      if (this.storedSecretFields().has(field.name)) return true;
      if (Array.isArray(value)) return value.length > 0;
      return value !== undefined && value !== null && value !== '';
    });
  });

  async start(integrationId: string, options?: { title?: string; entryId?: string }): Promise<void> {
    this.reset();
    this.integrationId.set(integrationId);
    this.starting.set(true);

    try {
      const response = options?.entryId
        ? await this.api.startEditConfigFlow(integrationId, options.entryId)
        : options?.title !== undefined
          ? await this.api.startNewConfigFlow(integrationId, options.title)
          : await this.api.startConfigFlow(integrationId);
      if (!response.supported || !response.flowId || !response.step) {
        this.notSupported.set(true);
        return;
      }
      this.flowId.set(response.flowId);
      this.storedSecretFields.set(new Set(response.storedSecretFields ?? []));
      this.applyStep(response.step, response.initialValues ?? {});

      if (response.supportsConfigUi) {
        this.session.set(this.uiSessions.open({
          kind: 'config',
          entryPoint: UiConfigEntryPoints.IntegrationConfig,
          integrationId,
          flowId: response.flowId,
          configUiModelVersion: response.configUiModelVersion ?? 0,
        }));
      }
    } finally {
      this.starting.set(false);
    }
  }

  sendTreeEvent(event: UiNodeEvent): void {
    this.session()?.send(event);
  }

  setValue(name: string, value: unknown): void {
    this.clearedSecretFields.update(current => {
      if (!current.has(name)) return current;
      const next = new Set(current);
      next.delete(name);
      return next;
    });
    this.values.update(current => ({ ...current, [name]: value }));
  }

  clearStoredSecret(name: string): void {
    this.clearedSecretFields.update(current => new Set(current).add(name));
    this.storedSecretFields.update(current => {
      const next = new Set(current);
      next.delete(name);
      return next;
    });
    this.values.update(current => {
      const next = { ...current };
      delete next[name];
      return next;
    });
  }

  async submit(): Promise<void> {
    const step = this.step();
    if (!step) return;
    await this.submitStep(step.stepId, this.values());
  }

  private async submitStep(stepId: string, values: Record<string, unknown>): Promise<void> {
    const flowId = this.flowId();
    const integrationId = this.integrationId();
    if (!flowId || !integrationId) return;

    this.submitting.set(true);
    this.message.set(null);
    this.fieldErrors.set({});

    try {
      const response = await this.api.submitConfigFlowStep(integrationId, {
        flowId,
        stepId,
        values,
        clearedSecretFields: [...this.clearedSecretFields()],
      });

      if (response.error) {
        this.message.set(this.resolveMessage(response.error.message));
        return;
      }

      switch (response.kind) {
        case 'Step':
          if (response.step) this.applyStep(response.step);
          break;
        case 'Error':
          if (response.step) this.step.set(response.step);
          this.fieldErrors.set(this.resolveFieldErrors(response.fieldErrors));
          this.message.set(this.resolveMessage(response.message));
          break;
        case 'External':
          this.handleExternal(response.externalUrl, response.resumeStepId);
          break;
        case 'Complete':
          this.done.set(true);
          this.closeSession();
          break;
      }
    } finally {
      this.submitting.set(false);
    }
  }

  private handleExternal(url: string | undefined, resumeStepId: string | undefined): void {
    if (!url || !resumeStepId) {
      this.message.set(this.localization.translateKey(AppStrings.ConfigFlow.AuthorizationStartFailed));
      return;
    }

    const flowId = this.flowId();
    this.waitingForAuth.set(true);
    this.externalLinks.open(url);

    this.api
      .onNotification<ConfigFlowAuthorizedNotification>('ConfigFlowAuthorizedNotification')
      .pipe(
        filter(notification => notification.flowId === flowId),
        take(1),
      )
      .subscribe(notification => {
        this.waitingForAuth.set(false);
        if (!notification.success) {
          this.message.set(notification.error ?? this.localization.translateKey(AppStrings.ConfigFlow.AuthorizationIncomplete));
          return;
        }
        void this.submitStep(resumeStepId, {});
      });
  }

  reset(): void {
    this.closeSession();
    this.integrationId.set(null);
    this.flowId.set(null);
    this.step.set(null);
    this.values.set({});
    this.fieldErrors.set({});
    this.message.set(null);
    this.storedSecretFields.set(new Set());
    this.clearedSecretFields.set(new Set());
    this.starting.set(false);
    this.submitting.set(false);
    this.notSupported.set(false);
    this.done.set(false);
    this.waitingForAuth.set(false);
  }

  private closeSession(): void {
    this.session()?.close();
    this.session.set(null);
  }

  private resolveMessage(message: LocalizedText | undefined): string | null {
    return resolveLocalizedText(message, this.localization) || null;
  }

  private resolveFieldErrors(errors: Record<string, LocalizedText> | undefined): Record<string, string> {
    if (!errors) return {};

    return Object.fromEntries(
      Object.entries(errors)
        .map(([field, message]) => [field, resolveLocalizedText(message, this.localization)])
        .filter((entry): entry is [string, string] => Boolean(entry[1])),
    );
  }

  private applyStep(step: ConfigFlowStepDto, initialValues: Record<string, unknown> = {}): void {
    this.step.set(step);
    this.values.set({ ...defaultValuesFor(step), ...initialValues });
    this.fieldErrors.set({});
    this.message.set(null);
  }
}

function defaultValuesFor(step: ConfigFlowStepDto): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  for (const field of step.fields) {
    if (field.defaultValue !== undefined && field.defaultValue !== null) {
      values[field.name] = field.defaultValue;
    } else if (field.type === ActionParameterType.Boolean) {
      values[field.name] = false;
    }
  }
  return values;
}
