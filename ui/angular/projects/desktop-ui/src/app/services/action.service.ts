import { Injectable, computed, inject, signal } from '@angular/core';
import { ActionBlockDefinition, ActionBlockParameter, ActionDefinition, ActionParameterDef, ExecuteActionResponse, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { mapActionParameterDef } from '../domain/action-parameter-mapping.util';
import { IntegrationService } from './integration.service';

export interface ActionDefinitionModel {
  id: string;
  integrationId: string;
  integrationName: string;
  name: string;
  description: string;
  parameters: ActionParameterDef[];
  descriptiveUiSchema?: string;
  isStateProviderAction?: boolean;
  isIconProviderAction?: boolean;
  supportsConfigUi?: boolean;
  configUiModelVersion?: number;
}

@Injectable({
  providedIn: 'root'
})
export class ActionService {
  private readonly api = inject(ApiService);
  private readonly integrationService = inject(IntegrationService);
  private readonly localization = inject(LocalizationService);
  private readonly DEFAULT_INTEGRATION_CATEGORY = 'Unknown';

  readonly actions = signal<ActionDefinitionModel[]>([]);
  readonly isLoading = signal(false);

  async loadActions(): Promise<void> {
    this.isLoading.set(true);
    try {
      const response = await this.api.getActions();
      const actions = (response.actions || []).map((a: ActionDefinition) => this.mapAction(a));
      this.actions.set(actions);
    } catch (error) {
      console.error('Failed to load actions:', error);
    } finally {
      this.isLoading.set(false);
    }
  }

  actionsForIntegration(integrationId: string): ActionDefinitionModel[] {
    return this.actions()
      .filter(a => a.integrationId === integrationId)
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  blockParameterDefs(action: ActionDefinitionModel): Omit<ActionBlockParameter, 'value'>[] {
    return action.parameters.map(p => this.mapParameter(p));
  }

  runAction(
    integrationId: string,
    actionId: string,
    parameters: Record<string, unknown>,
  ): Promise<ExecuteActionResponse> {
    return this.api.executeAction({ integrationId, actionId, parameters });
  }

  readonly actionBlockDefinitions = computed<ActionBlockDefinition[]>(() =>
    this.buildActionBlockDefinitions());

  getActionBlockDefinitions(): ActionBlockDefinition[] {
    return this.actionBlockDefinitions();
  }

  private buildActionBlockDefinitions(): ActionBlockDefinition[] {
    return this.actions().map(action => {
      const integration = this.integrationService.integrations()
        .find(i => i.id === action.integrationId);
      const category = action.integrationName
        || integration?.name
        || action.integrationId
        || this.DEFAULT_INTEGRATION_CATEGORY;

      return {
        blockType: `${action.integrationId}.${action.id}`,
        type: 'action' as const,
        label: action.name,
        description: action.description || undefined,
        color: '#3b82f6',
        category,
        categoryId: action.integrationId,
        integrationId: action.integrationId,
        actionId: action.id,
        blockShape: 'stack' as const,
        descriptiveUiSchema: action.descriptiveUiSchema,
        providesButtonState: action.isStateProviderAction,
        providesWidgetIcon: action.isIconProviderAction,
        parameters: action.parameters.map(p => this.mapParameter(p))
      };
    });
  }

  private mapAction(action: ActionDefinition): ActionDefinitionModel {
    return {
      id: action.id,
      integrationId: action.integrationId,
      integrationName: resolveLocalizedText(action.integrationName, this.localization),
      name: resolveLocalizedText(action.name, this.localization),
      description: resolveLocalizedText(action.description, this.localization),
      descriptiveUiSchema: action.descriptiveUiSchema ?? undefined,
      parameters: action.parameters || [],
      isStateProviderAction: action.isStateProviderAction ?? false,
      isIconProviderAction: action.isIconProviderAction ?? false,
      supportsConfigUi: action.supportsConfigUi ?? false,
      configUiModelVersion: action.configUiModelVersion ?? 0,
    };
  }

  definitionFor(integrationId: string | undefined, actionId: string | undefined): ActionDefinitionModel | undefined {
    if (!integrationId || !actionId) return undefined;
    return this.actions().find(a => a.integrationId === integrationId && a.id === actionId);
  }

  private mapParameter(p: ActionParameterDef): Omit<ActionBlockParameter, 'value'> {
    return mapActionParameterDef(p, this.localization);
  }
}
