import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';

import { ActionBlockDefinition, ActionFlow, AppStrings, SCRIPT_TRIGGER_TYPE, Script, ScriptInput, ScriptInputValue, ScriptUsage, scriptActionCount, scriptInputVariables } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe, VariableService } from '@shared';
import { ActionBuilderComponent } from '../../action-builder/action-builder.component';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { RailItemComponent } from '../../rail-page/rail-item.component';
import { RailPageComponent } from '../../rail-page/rail-page.component';
import { ActionFlowValidationResult } from '../../../domain/action-flow-validation.util';
import { ActionFlowOwner } from '../../../services/action-clipboard.service';
import { ActionCutOriginService } from '../../../services/action-cut-origin.service';
import { ActionService } from '../../../services/action.service';
import { IntegrationService } from '../../../services/integration.service';
import { ScriptService } from '../../../services/script.service';

import { ConfirmsNavigation } from '../../../guards';

import { ScriptEditDialogComponent, ScriptEditResult } from './script-edit-dialog.component';
import { ScriptInputsPanelComponent } from './script-inputs-panel.component';
import { ScriptRunDialogComponent } from './script-run-dialog.component';

@Component({
  selector: 'app-scripts-page',
  standalone: true,
  imports: [
    ActionBuilderComponent,
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    RailItemComponent,
    RailPageComponent,
    ScriptEditDialogComponent,
    ScriptInputsPanelComponent,
    ScriptRunDialogComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './scripts-page.component.html',
  styleUrls: ['./scripts-page.component.scss'],
})
export class ScriptsPageComponent implements OnInit, ConfirmsNavigation {
  private readonly localization = inject(LocalizationService);
  protected readonly scripts = inject(ScriptService);
  private readonly actionService = inject(ActionService);
  private readonly integrationService = inject(IntegrationService);
  private readonly variableService = inject(VariableService);
  private readonly toasts = inject(ToastService);
  private readonly cutOrigin = inject(ActionCutOriginService);

  private resolveLeave: ((leave: boolean) => void) | null = null;

  protected readonly triggerTabs = computed(() => [
    { triggerType: SCRIPT_TRIGGER_TYPE, label: this.localization.translateKey(AppStrings.Scripts.ActionsTabLabel) },
  ]);

  protected readonly inputsTabId = 'inputs';

  protected readonly selectedScriptId = signal<string | null>(null);
  protected readonly availableActionBlocks = signal<ActionBlockDefinition[]>([]);

  protected readonly draftFlows = signal<ActionFlow[]>([]);
  protected readonly draftInputs = signal<ScriptInput[]>([]);
  protected readonly draftRunsOnWidget = signal(false);
  protected readonly isDirty = signal(false);
  protected readonly flowValidity = signal<ActionFlowValidationResult>({ valid: true, errors: [] });
  protected readonly inputsValid = signal(true);
  protected readonly isSaving = signal(false);

  protected readonly editDialogMode = signal<'create' | 'edit' | null>(null);
  protected readonly pendingDelete = signal<Script | null>(null);
  protected readonly pendingDeleteUsages = signal<ScriptUsage[]>([]);
  protected readonly pendingSelection = signal<Script | null>(null);
  protected readonly confirmingLeave = signal(false);
  protected readonly pendingRun = signal<Script | null>(null);

  protected readonly flowOwner = computed<ActionFlowOwner | null>(() => {
    const id = this.selectedScriptId();
    return id ? { kind: 'script', scriptId: id } : null;
  });

  protected readonly selectedScript = computed(() =>
    this.scripts.scripts().find(script => script.id === this.selectedScriptId()) ?? null,
  );

  protected readonly variables = computed(() => [
    ...scriptInputVariables(this.draftInputs()),
    ...this.variableService.globalVariables(),
  ]);

  protected readonly globalNames = computed(() =>
    this.variableService.globalVariables().map(variable => variable.name),
  );

  protected readonly canSave = computed(() =>
    this.isDirty() && this.flowValidity().valid && this.inputsValid() && !this.isSaving(),
  );

  protected readonly deleteMessage = computed(() => {
    const script = this.pendingDelete();
    if (!script) {
      return '';
    }

    const usages = this.pendingDeleteUsages();
    if (usages.length === 0) {
      return this.localization.translateKey(AppStrings.Scripts.DeleteScriptSimpleMessage, { name: script.name });
    }

    const widgetCount = usages
      .filter(usage => usage.kind === 'widget')
      .reduce((total, usage) => total + usage.count, 0);
    const scriptCount = usages.filter(usage => usage.kind === 'script').length;

    const parts: string[] = [];
    if (widgetCount > 0) {
      parts.push(this.localization.translateKey(AppStrings.Scripts.UsageWidgetCount, { count: widgetCount }));
    }
    if (scriptCount > 0) {
      parts.push(this.localization.translateKey(AppStrings.Scripts.UsageOtherScriptCount, { count: scriptCount }));
    }

    const where = usages.map(usage => usage.location).join(', ');
    return this.localization.translateKey(AppStrings.Scripts.DeleteScriptUsageMessage, {
      name: script.name,
      parts: parts.join(` ${this.localization.translateKey(AppStrings.Scripts.UsageConjunction)} `),
      where,
    });
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.scripts.loadScripts(),
      this.actionService.loadActions(),
      this.integrationService.loadIntegrations(),
      this.variableService.variables().length === 0 ? this.variableService.loadVariables() : Promise.resolve(),
    ]);

    this.availableActionBlocks.set(this.actionService.getActionBlockDefinitions());

    const first = this.scripts.sortedScripts()[0];
    if (first) {
      this.select(first);
    }
  }

  protected actionCount(script: Script): number {
    return scriptActionCount(script);
  }

  protected select(script: Script): void {
    if (script.id === this.selectedScriptId()) {
      return;
    }

    if (this.isDirty()) {
      this.pendingSelection.set(script);
      return;
    }

    this.applySelection(script);
  }

  confirmNavigation(): Promise<boolean> | boolean {
    if (!this.isDirty()) return true;
    if (this.resolveLeave) return false;

    this.confirmingLeave.set(true);
    return new Promise<boolean>(resolve => { this.resolveLeave = resolve; });
  }

  protected onDiscardAndLeave(): void {
    this.settleLeave(true);
  }

  protected onKeepEditing(): void {
    this.settleLeave(false);
  }

  private settleLeave(leave: boolean): void {
    this.confirmingLeave.set(false);
    const resolve = this.resolveLeave;
    this.resolveLeave = null;
    resolve?.(leave);
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

  protected onInputsChange(inputs: ScriptInput[]): void {
    this.draftInputs.set(inputs);
    this.isDirty.set(true);
  }

  protected onRunsOnWidgetChange(runsOnWidget: boolean): void {
    this.draftRunsOnWidget.set(runsOnWidget);
    this.isDirty.set(true);
  }

  protected onValidityChange(validity: ActionFlowValidationResult): void {
    this.flowValidity.set(validity);
  }

  protected onInputsValidityChange(valid: boolean): void {
    this.inputsValid.set(valid);
  }

  protected async save(): Promise<void> {
    const script = this.selectedScript();
    if (!script || !this.canSave()) {
      return;
    }

    this.isSaving.set(true);
    const result = await this.scripts.updateScript(script.id, {
      flows: this.draftFlows(),
      inputs: this.draftInputs(),
      runsOnWidget: this.draftRunsOnWidget(),
    });
    this.isSaving.set(false);

    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Scripts.SaveFailed), { variant: 'error' });
      return;
    }

    this.isDirty.set(false);
    this.toasts.show(this.localization.translateKey(AppStrings.Scripts.Saved, { name: script.name }));
    await this.settleCutSource();
  }

  private async settleCutSource(): Promise<void> {
    const owner = this.flowOwner();
    if (!owner) return;

    const settled = await this.cutOrigin.settle(owner, this.draftFlows());
    if (settled && !settled.success) {
      this.toasts.show(this.localization.translateKey(AppStrings.Scripts.MoveActionRemoveFailed), {
        variant: 'error',
      });
    }
  }

  protected discard(): void {
    const script = this.selectedScript();
    this.draftFlows.set(script ? [...script.flows] : []);
    this.draftInputs.set(script ? [...script.inputs] : []);
    this.draftRunsOnWidget.set(script?.runsOnWidget ?? false);
    this.isDirty.set(false);
    this.inputsValid.set(true);
  }

  protected openCreate(): void {
    this.editDialogMode.set('create');
  }

  protected openRename(): void {
    if (this.selectedScript()) {
      this.editDialogMode.set('edit');
    }
  }

  protected async onEditDialogSave(result: ScriptEditResult): Promise<void> {
    const mode = this.editDialogMode();
    this.editDialogMode.set(null);

    if (mode === 'create') {
      const created = await this.scripts.createScript(result.name, result.description);
      if (!created.success || !created.data) {
        this.toasts.show(created.error?.message ?? this.localization.translateKey(AppStrings.Scripts.CreateFailed), { variant: 'error' });
        return;
      }

      this.applySelection(created.data);
      return;
    }

    const script = this.selectedScript();
    if (!script) {
      return;
    }

    const updated = await this.scripts.updateScript(script.id, {
      name: result.name,
      description: result.description,
    });
    if (!updated.success) {
      this.toasts.show(updated.error?.message ?? this.localization.translateKey(AppStrings.Scripts.RenameFailed), { variant: 'error' });
    }
  }

  protected async duplicate(): Promise<void> {
    const script = this.selectedScript();
    if (!script) {
      return;
    }

    const result = await this.scripts.duplicateScript(script.id);
    if (!result.success || !result.data) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Scripts.DuplicateFailed), { variant: 'error' });
      return;
    }

    this.toasts.show(this.localization.translateKey(AppStrings.Scripts.Duplicated, { name: result.data.name }));
  }

  protected async run(): Promise<void> {
    const script = this.selectedScript();
    if (!script) {
      return;
    }

    if (this.isDirty()) {
      this.toasts.show(this.localization.translateKey(AppStrings.Scripts.RunNeedsSaveFirst), { variant: 'error' });
      return;
    }

    // The saved declarations, not the draft: the run is of what is stored.
    if (script.inputs.length > 0) {
      this.pendingRun.set(script);
      return;
    }

    await this.execute(script, undefined);
  }

  protected async runWithInputs(inputs: Record<string, ScriptInputValue>): Promise<void> {
    const script = this.pendingRun();
    this.pendingRun.set(null);
    if (script) {
      await this.execute(script, inputs);
    }
  }

  private async execute(script: Script, inputs: Record<string, ScriptInputValue> | undefined): Promise<void> {
    const result = await this.scripts.runScript(script.id, inputs);
    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Scripts.RunFailed), { variant: 'error' });
    }
  }

  protected async requestDelete(): Promise<void> {
    const script = this.selectedScript();
    if (!script) {
      return;
    }

    this.pendingDeleteUsages.set(await this.scripts.getUsages(script.id));
    this.pendingDelete.set(script);
  }

  protected async confirmDelete(): Promise<void> {
    const script = this.pendingDelete();
    this.pendingDelete.set(null);
    if (!script) {
      return;
    }

    const result = await this.scripts.deleteScript(script.id);
    if (!result.success) {
      this.toasts.show(result.error?.message ?? this.localization.translateKey(AppStrings.Scripts.DeleteFailed), { variant: 'error' });
      return;
    }

    if (this.selectedScriptId() === script.id) {
      const next = this.scripts.sortedScripts()[0] ?? null;
      if (next) {
        this.applySelection(next);
      } else {
        this.selectedScriptId.set(null);
        this.draftFlows.set([]);
        this.draftInputs.set([]);
        this.draftRunsOnWidget.set(false);
        this.isDirty.set(false);
        this.inputsValid.set(true);
      }
    }
  }

  private applySelection(script: Script): void {
    this.selectedScriptId.set(script.id);
    this.draftFlows.set([...script.flows]);
    this.draftInputs.set([...script.inputs]);
    this.draftRunsOnWidget.set(script.runsOnWidget);
    this.isDirty.set(false);
    this.flowValidity.set({ valid: true, errors: [] });
    this.inputsValid.set(true);
  }
}
