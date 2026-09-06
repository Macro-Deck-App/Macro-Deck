import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';

import { ActionBlockDefinition, ActionFlow, AppStrings, generateBlockId } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, ToggleSwitchComponent, TranslatePipe, VariableService } from '@shared';
import { ActionBuilderComponent } from '../../action-builder/action-builder.component';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { RailItemComponent } from '../../rail-page/rail-item.component';
import { RailPageComponent } from '../../rail-page/rail-page.component';
import { ActionFlowValidationResult } from '../../../domain/action-flow-validation.util';
import { Automation, automationActionCount, automationEditableFlows, automationEventName, automationIsIncomplete } from '../../../domain/automation.interface';
import { ActionFlowOwner } from '../../../services/action-clipboard.service';
import { ActionCutOriginService } from '../../../services/action-cut-origin.service';
import { ActionService } from '../../../services/action.service';
import { AutomationService } from '../../../services/automation.service';
import { IntegrationService } from '../../../services/integration.service';

import { AutomationEditDialogComponent, AutomationEditResult } from './automation-edit-dialog.component';

@Component({
  selector: 'app-automations-page',
  standalone: true,
  imports: [
    ActionBuilderComponent,
    AutomationEditDialogComponent,
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    RailItemComponent,
    RailPageComponent,
    ToggleSwitchComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './automations-page.component.html',
  styleUrls: ['./automations-page.component.scss'],
})
export class AutomationsPageComponent implements OnInit {
  @ViewChild(ActionBuilderComponent) private builder?: ActionBuilderComponent;

  private readonly localization = inject(LocalizationService);
  protected readonly automations = inject(AutomationService);
  private readonly actionService = inject(ActionService);
  private readonly integrationService = inject(IntegrationService);
  private readonly variableService = inject(VariableService);
  private readonly toasts = inject(ToastService);
  private readonly cutOrigin = inject(ActionCutOriginService);

  protected readonly selectedAutomationId = signal<string | null>(null);
  protected readonly availableActionBlocks = signal<ActionBlockDefinition[]>([]);

  protected readonly draftFlows = signal<ActionFlow[]>([]);
  protected readonly isDirty = signal(false);
  protected readonly flowValidity = signal<ActionFlowValidationResult>({ valid: true, errors: [] });
  protected readonly isSaving = signal(false);
  protected readonly isTogglingEnabled = signal(false);

  protected readonly editDialogMode = signal<'create' | 'edit' | null>(null);
  protected readonly pendingDelete = signal<Automation | null>(null);
  protected readonly pendingSelection = signal<Automation | null>(null);

  protected readonly flowOwner = computed<ActionFlowOwner | null>(() => {
    const id = this.selectedAutomationId();
    return id ? { kind: 'automation', automationId: id } : null;
  });

  protected readonly selectedAutomation = computed(() =>
    this.automations.automations().find(automation => automation.id === this.selectedAutomationId()) ?? null,
  );

  protected readonly variables = computed(() => this.variableService.globalVariables());

  protected readonly canSave = computed(() =>
    this.isDirty() && this.flowValidity().valid && !this.isSaving(),
  );

  protected readonly warnsIncomplete = computed(() => {
    const automation = this.selectedAutomation();
    return !!automation && automation.enabled && automationIsIncomplete({ flows: this.draftFlows() });
  });

  protected readonly draftEventName = computed(() =>
    automationEventName({ flows: this.draftFlows() }, this.localization) ?? this.localization.translateKey(AppStrings.Automations.NoEventYet),
  );

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.automations.loadAutomations(),
      this.actionService.loadActions(),
      this.integrationService.loadIntegrations(),
      this.variableService.variables().length === 0 ? this.variableService.loadVariables() : Promise.resolve(),
    ]);

    this.availableActionBlocks.set(this.actionService.getActionBlockDefinitions());

    const first = this.automations.sortedAutomations()[0];
    if (first) {
      this.applySelection(first);
    }
  }

  protected actionCount(automation: Automation): number {
    return automationActionCount(automation);
  }

  protected eventName(automation: Automation): string {
    return automationEventName(automation, this.localization) ?? this.localization.translateKey(AppStrings.Automations.NoEventYet);
  }

  protected select(automation: Automation): void {
    if (automation.id === this.selectedAutomationId()) {
      return;
    }

    if (this.isDirty()) {
      this.pendingSelection.set(automation);
      return;
    }

    this.applySelection(automation);
  }

  protected confirmDiscardAndSwitch(): void {
    const next = this.pendingSelection();
    this.pendingSelection.set(null);
    if (next) {
      this.applySelection(next);
    }
  }

  protected onFlowsChange(flows: ActionFlow[]): void {
    this.draftFlows.set(flows);
    this.isDirty.set(true);
  }

  protected onValidityChange(validity: ActionFlowValidationResult): void {
    this.flowValidity.set(validity);
  }

  protected async run(): Promise<void> {
    await this.builder?.runFlow();
  }

  protected canRun(): boolean {
    return this.builder?.canRun() ?? false;
  }

  protected async save(): Promise<void> {
    const automation = this.selectedAutomation();
    if (!automation || !this.canSave()) {
      return;
    }

    this.isSaving.set(true);
    const result = await this.automations.updateAutomation(automation.id, { flows: this.draftFlows() });
    this.isSaving.set(false);

    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Automations.SaveFailed), { variant: 'error' });
      return;
    }

    this.isDirty.set(false);
    this.toasts.show(this.localization.translateKey(AppStrings.Automations.Saved, { name: automation.name }));
    await this.settleCutSource();
  }

  private async settleCutSource(): Promise<void> {
    const owner = this.flowOwner();
    if (!owner) return;

    const settled = await this.cutOrigin.settle(owner, this.draftFlows());
    if (settled && !settled.success) {
      this.toasts.show(this.localization.translateKey(AppStrings.Automations.MoveActionRemoveFailed), {
        variant: 'error',
      });
    }
  }

  protected discard(): void {
    const automation = this.selectedAutomation();
    this.draftFlows.set(automation ? automationEditableFlows(automation, generateBlockId, this.localization) : []);
    this.isDirty.set(false);
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    const automation = this.selectedAutomation();
    if (!automation) {
      return;
    }

    this.isTogglingEnabled.set(true);
    const result = await this.automations.setEnabled(automation.id, enabled);
    this.isTogglingEnabled.set(false);

    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Automations.ToggleFailed), { variant: 'error' });
    }
  }

  protected openCreate(): void {
    this.editDialogMode.set('create');
  }

  protected openRename(): void {
    if (this.selectedAutomation()) {
      this.editDialogMode.set('edit');
    }
  }

  protected async onEditDialogSave(result: AutomationEditResult): Promise<void> {
    const mode = this.editDialogMode();
    this.editDialogMode.set(null);

    if (mode === 'create') {
      const created = await this.automations.createAutomation(result.name, result.description);
      if (!created.success || !created.data) {
        this.toasts.show(created.error?.message ?? this.localization.translateKey(AppStrings.Automations.CreateFailed), { variant: 'error' });
        return;
      }

      this.applySelection(created.data);
      return;
    }

    const automation = this.selectedAutomation();
    if (!automation) {
      return;
    }

    const updated = await this.automations.updateAutomation(automation.id, {
      name: result.name,
      description: result.description,
    });
    if (!updated.success) {
      this.toasts.show(updated.error?.message ?? this.localization.translateKey(AppStrings.Automations.RenameFailed), { variant: 'error' });
    }
  }

  protected async duplicate(): Promise<void> {
    const automation = this.selectedAutomation();
    if (!automation) {
      return;
    }

    const result = await this.automations.duplicateAutomation(automation.id);
    if (!result.success || !result.data) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Automations.DuplicateFailed), { variant: 'error' });
      return;
    }

    this.toasts.show(this.localization.translateKey(AppStrings.Automations.Duplicated, { name: result.data.name }));
  }

  protected requestDelete(): void {
    const automation = this.selectedAutomation();
    if (automation) {
      this.pendingDelete.set(automation);
    }
  }

  protected async confirmDelete(): Promise<void> {
    const automation = this.pendingDelete();
    this.pendingDelete.set(null);
    if (!automation) {
      return;
    }

    const result = await this.automations.deleteAutomation(automation.id);
    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Automations.DeleteFailed), { variant: 'error' });
      return;
    }

    if (this.selectedAutomationId() === automation.id) {
      const next = this.automations.sortedAutomations()[0] ?? null;
      if (next) {
        this.applySelection(next);
      } else {
        this.selectedAutomationId.set(null);
        this.draftFlows.set([]);
        this.isDirty.set(false);
      }
    }
  }

  protected deleteMessage(automation: Automation): string {
    const actions = this.actionCount(automation);
    return this.localization.translateKey(AppStrings.Automations.DeleteAutomationMessage, { name: automation.name, count: actions });
  }

  private applySelection(automation: Automation): void {
    this.selectedAutomationId.set(automation.id);
    this.draftFlows.set(automationEditableFlows(automation, generateBlockId, this.localization));
    this.isDirty.set(false);
    this.flowValidity.set({ valid: true, errors: [] });
  }
}
