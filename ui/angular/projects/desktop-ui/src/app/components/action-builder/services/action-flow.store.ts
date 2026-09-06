import { Injectable, computed, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';

import { ActionBlock, ActionBlockDefinition, ActionBlockParameter, ActionBranch, ActionFlow, AppStrings, ComparisonOperator, ConditionExpression, EVENT_TRIGGER_TYPE, EventTriggerBinding, ParameterValue, cloneBlockWithNewIds, collectSecretIds, createBlockFromDefinition, createEmptyComparison, findBlock, generateBlockId, insertAfterBlock, insertBlockAt, isEventFlow, isInvalidNestedMove, isSameTriggerType, mapDescendants, mapSecretReferences, removeBlockAt, removeBlockFromFlows, resolveList } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import type { EventDefinition, RunActionFlowResponse, Variable, VariableScope } from '@macro-deck/runtime';
import { ActionFlowValidationError, validateActionFlows } from '../../../domain/action-flow-validation.util';
import { ActionClipboardEntry, ActionClipboardService, ActionFlowOwner, sameActionFlowOwner } from '../../../services/action-clipboard.service';
import { DEFAULT_TRIGGER_TYPE, comparisonOperatorOptions } from '../default-action-defs';

export interface StateProviderToggleRequest {
  blockId: string;
  integrationId: string;
  actionId: string;
  actionLabel?: string;
  checked: boolean;
}

export interface IconProviderToggleRequest {
  blockId: string;
  integrationId: string;
  actionId: string;
  actionLabel?: string;
  checked: boolean;
}

@Injectable()
export class ActionFlowStore {
  readonly flows = signal<ActionFlow[]>([]);
  readonly variables = signal<Variable[]>([]);

  readonly scriptRunsOnWidget = signal(false);

  readonly eventVariables = signal<Variable[]>([]);

  readonly eventDefinition = signal<EventDefinition | undefined>(undefined);

  readonly eventConfigurationValues = signal<Record<string, unknown>>({});

  readonly pickerVariables = computed<Variable[]>(() => [...this.eventVariables(), ...this.variables()]);
  readonly previewScope = signal<VariableScope>('global');
  readonly previewScopeRefId = signal<string | undefined>(undefined);
  readonly previewScopeStates = signal<{ id: string; label: string }[] | undefined>(undefined);

  readonly previewScopeHasOnOffStates = computed<boolean | undefined>(() => {
    const states = this.previewScopeStates();
    return states === undefined ? undefined : states.length >= 2;
  });

  readonly availableBlocks = signal<ActionBlockDefinition[]>([]);

  readonly stateProviderBlockId = signal<string | undefined>(undefined);

  readonly consumerSupportsStateProvider = signal(false);

  private readonly stateProviderRequestSubject = new Subject<StateProviderToggleRequest>();
  readonly stateProviderRequests$ = this.stateProviderRequestSubject.asObservable();

  requestStateProviderToggle(request: StateProviderToggleRequest): void {
    this.stateProviderRequestSubject.next(request);
  }

  providesButtonState(integrationId: string | undefined, actionId: string | undefined): boolean {
    if (!integrationId || !actionId) return false;
    return this.availableBlocks().some(block =>
      block.integrationId === integrationId && block.actionId === actionId && block.providesButtonState === true);
  }

  readonly iconProviderBlockId = signal<string | undefined>(undefined);

  readonly consumerSupportsIconProvider = signal(false);

  private readonly iconProviderRequestSubject = new Subject<IconProviderToggleRequest>();
  readonly iconProviderRequests$ = this.iconProviderRequestSubject.asObservable();

  requestIconProviderToggle(request: IconProviderToggleRequest): void {
    this.iconProviderRequestSubject.next(request);
  }

  providesWidgetIcon(integrationId: string | undefined, actionId: string | undefined): boolean {
    if (!integrationId || !actionId) return false;
    return this.availableBlocks().some(block =>
      block.integrationId === integrationId && block.actionId === actionId && block.providesWidgetIcon === true);
  }

  readonly selectedTriggerId = signal<string>(DEFAULT_TRIGGER_TYPE);

  readonly pickerOpenForList = signal<string | null>(null);

  readonly expandedBlocks = signal<Set<string>>(new Set());

  private readonly localization = inject(LocalizationService);
  readonly comparisonOperators = computed(() =>
    comparisonOperatorOptions(key => this.localization.translateKey(key)));

  private readonly api = inject(ApiService);
  private readonly clipboard = inject(ActionClipboardService);
  private readonly toasts = inject(ToastService);

  readonly hasOwnerWidget = computed(() =>
    (this.previewScope() === 'widget' && !!this.previewScopeRefId()) || this.scriptRunsOnWidget());

  readonly flowOwner = signal<ActionFlowOwner | null>(null);

  readonly resolvedOwner = computed<ActionFlowOwner | null>(() => {
    const declared = this.flowOwner();
    if (declared) return declared;
    const widgetId = this.previewScopeRefId();
    return this.hasOwnerWidget() && widgetId ? { kind: 'widget', widgetId } : null;
  });

  readonly canPaste = this.clipboard.hasContent;

  isCutSource(blockId: string): boolean {
    return this.clipboard.cutBlockId() === blockId;
  }

  readonly validation = computed(() =>
    validateActionFlows(this.flows(), { hasOwnerWidget: this.hasOwnerWidget() },
      key => this.localization.translateKey(key)));

  readonly rootListId = computed(() => `flow:${this.resolveTriggerId(this.selectedTriggerId())}`);

  readonly eventFlows = computed(() => this.flows().filter(isEventFlow));

  readonly selectedEventFlow = computed(() =>
    this.eventFlows().find(flow => flow.triggerId === this.selectedTriggerId()),
  );

  readonly selectedFlow = computed<ActionFlow | undefined>(() => {
    const selected = this.selectedTriggerId();
    return this.flows().find(flow => flow.triggerId === selected)
      ?? this.flows().find(flow => !isEventFlow(flow) && isSameTriggerType(flow.triggerType, selected));
  });

  readonly running = signal(false);

  readonly canRun = computed(() => {
    const flow = this.selectedFlow();
    return !this.running() && !!flow && flow.children.some(block => !block.disabled);
  });

  private emitFn: (flows: ActionFlow[]) => void = () => undefined;
  private triggerLabelFn: (triggerType: string) => string | undefined = () => undefined;

  connect(
    emit: (flows: ActionFlow[]) => void,
    triggerLabelFor?: (triggerType: string) => string | undefined,
  ): void {
    this.emitFn = emit;
    if (triggerLabelFor) this.triggerLabelFn = triggerLabelFor;
  }

  errorsFor(blockId: string): ActionFlowValidationError[] {
    return this.validation().errors.filter(e => e.blockId === blockId);
  }

  isExpanded(blockId: string): boolean {
    return this.expandedBlocks().has(blockId);
  }

  toggleExpanded(blockId: string): void {
    this.expandedBlocks.update(prev => {
      const next = new Set(prev);
      if (next.has(blockId)) next.delete(blockId);
      else next.add(blockId);
      return next;
    });
  }

  private markExpanded(blockId: string): void {
    this.expandedBlocks.update(prev => {
      const next = new Set(prev);
      next.add(blockId);
      return next;
    });
  }

  requestAdd(listId: string): void {
    this.pickerOpenForList.set(listId);
  }

  closePicker(): void {
    this.pickerOpenForList.set(null);
  }

  pickAction(def: ActionBlockDefinition): void {
    const listId = this.pickerOpenForList();
    if (!listId) return;

    const flows = structuredClone(this.flows());
    this.ensureSelectedFlow(flows);

    const block = createBlockFromDefinition(def);
    insertBlockAt(flows, listId, Number.MAX_SAFE_INTEGER, block);
    this.markExpanded(block.id);
    this.emit(flows);
    this.closePicker();
  }

  private ensureSelectedFlow(flows: ActionFlow[]): void {
    const selected = this.selectedTriggerId();
    if (flows.some(f => f.triggerId === selected || isSameTriggerType(f.triggerType, selected))) return;
    flows.push({
      triggerId: selected,
      triggerType: selected,
      triggerLabel: this.triggerLabelFn(selected) ?? selected,
      children: [],
    });
  }

  moveBlock(sourceListId: string, sourceIndex: number, targetListId: string, targetIndex: number): void {
    const flows = structuredClone(this.flows());
    const moved = removeBlockAt(flows, sourceListId, sourceIndex);
    if (!moved) return;
    if (isInvalidNestedMove(moved, targetListId)) return;

    let index = targetIndex;
    if (sourceListId === targetListId && sourceIndex < targetIndex) index--;
    insertBlockAt(flows, targetListId, index, moved);
    this.emit(flows);
  }

  async duplicateBlock(blockId: string): Promise<void> {
    const source = findBlock(this.flows(), blockId);
    if (!source) return;

    const clone = await this.withClonedSecrets(cloneBlockWithNewIds(source));

    const flows = structuredClone(this.flows());
    if (!insertAfterBlock(flows, blockId, clone)) return;
    this.markExpanded(clone.id);
    this.emit(flows);
  }

  copyBlock(blockId: string): void {
    const source = findBlock(this.flows(), blockId);
    if (!source) return;
    this.clipboard.copy(source);
  }

  cutBlock(blockId: string): void {
    const source = findBlock(this.flows(), blockId);
    if (!source) return;

    const owner = this.resolvedOwner();
    if (owner) this.clipboard.cut(source, owner);
    else this.clipboard.copy(source);
  }

  cancelCut(): void {
    if (this.clipboard.entry()?.isCut) this.clipboard.clear();
  }

  async pasteAfter(blockId: string): Promise<void> {
    await this.paste((flows, block) => insertAfterBlock(flows, blockId, block));
  }

  async pasteIntoList(listId: string): Promise<void> {
    await this.paste((flows, block) => {
      this.ensureSelectedFlow(flows);
      const list = resolveList(flows, listId);
      if (!list) return false;
      list.push(block);
      return true;
    });
  }

  private async paste(insert: (flows: ActionFlow[], block: ActionBlock) => boolean): Promise<void> {
    const entry = this.clipboard.entry();
    if (!entry) return;

    const owner = this.resolvedOwner();
    const clone = cloneBlockWithNewIds(entry.block);

    // Rehearsed on a throwaway copy first: cloning the secrets of a paste that cannot land would
    // leave the copies orphaned on the host.
    if (!insert(structuredClone(this.flows()), clone)) return;

    const block = await this.withClonedSecrets(clone);

    // The store outlives what it edits - the scripts page keeps one builder across script switches,
    // and the widget editor re-binds its widget in place - so a paste whose secret round trip
    // straddled such a switch must not land in the record the user moved on to.
    if (!this.ownerStillIs(owner)) return;

    const flows = structuredClone(this.flows());
    if (!insert(flows, block)) return;

    this.markExpanded(block.id);
    this.emit(flows);
    this.settleCut(entry, owner, block.id);
  }

  private async withClonedSecrets(clone: ActionBlock): Promise<ActionBlock> {
    const secretIds = [...new Set(collectSecretIds(clone))];
    if (secretIds.length === 0) return clone;

    const map = new Map<string, string | null>();
    let dropped = false;
    for (const id of secretIds) {
      try {
        const response = await this.api.cloneSecret(id);
        map.set(id, response.id ?? null);
        if (!response.id) dropped = true;
      } catch {
        map.set(id, null);
        dropped = true;
      }
    }

    if (dropped) {
      this.toasts.show(this.localization.translateKey(AppStrings.ActionBuilder.Store.SecretsNotCopied), {
        variant: 'error',
        detail: this.localization.translateKey(AppStrings.ActionBuilder.Store.ReEnterSecretsHint),
      });
    }
    return mapSecretReferences(clone, map);
  }

  private settleCut(
    entry: ActionClipboardEntry,
    owner: ActionFlowOwner | null,
    pastedBlockId: string,
  ): void {
    if (!entry.isCut || !entry.origin || !owner) return;

    if (sameActionFlowOwner(entry.origin.owner, owner)) {
      const removal = removeBlockFromFlows(this.flows(), entry.origin.blockId);
      if (removal.removed) this.emit(removal.flows);
      this.clipboard.clear();
      return;
    }

    this.clipboard.markPasted(owner, pastedBlockId);
  }

  private ownerStillIs(owner: ActionFlowOwner | null): boolean {
    const current = this.resolvedOwner();
    if (owner === null || current === null) return owner === current;
    return sameActionFlowOwner(owner, current);
  }

  toggleDisabled(blockId: string): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId) return updated;
        return { ...updated, disabled: !updated.disabled };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  updateBlockComment(blockId: string, comment: string): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId) return updated;
        if (comment.trim().length === 0) {
          const { comment: _comment, ...rest } = updated;
          return rest;
        }
        return { ...updated, comment };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  removeBlock(blockId: string): void {
    this.emit(removeBlockFromFlows(this.flows(), blockId).flows);
  }

  updateParam(blockId: string, paramName: string, value: ParameterValue, valueLabel?: string): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId || !updated.parameters) return updated;
        return {
          ...updated,
          parameters: updated.parameters.map(param => {
            if (param.name !== paramName) return param;
            const keepLabel = valueLabel === undefined && param.value === value;
            return { ...param, value, valueLabel: keepLabel ? param.valueLabel : valueLabel };
          }),
        };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  cacheParamLabel(blockId: string, paramName: string, value: ParameterValue, valueLabel: string): void {
    let changed = false;
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId || !updated.parameters) return updated;
        return {
          ...updated,
          parameters: updated.parameters.map(param => {
            if (param.name !== paramName || param.value !== value || param.valueLabel === valueLabel) {
              return param;
            }
            changed = true;
            return { ...param, valueLabel };
          }),
        };
      });

    const next = this.flows().map(flow => ({ ...flow, children: updateList(flow.children) }));
    if (changed) this.flows.set(next);
  }

  setBlockParameters(blockId: string, parameters: ActionBlockParameter[]): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId) return updated;
        return { ...updated, parameters };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  updateBlockCondition(blockId: string, expression: ConditionExpression): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId) return updated;
        return { ...updated, condition: expression };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  updateBranchCondition(blockId: string, branchId: string, expression: ConditionExpression): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId || !updated.branches) return updated;
        return {
          ...updated,
          branches: updated.branches.map(branch =>
            branch.id === branchId ? { ...branch, condition: expression } : branch,
          ),
        };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  addElseIf(blockId: string): void {
    this.mutateBranches(blockId, branches => {
      const newBranch: ActionBranch = {
        id: generateBlockId(),
        kind: 'elseif',
        condition: createEmptyComparison(),
        children: [],
      };
      const elseIdx = branches.findIndex(b => b.kind === 'else');
      if (elseIdx === -1) return [...branches, newBranch];
      return [...branches.slice(0, elseIdx), newBranch, ...branches.slice(elseIdx)];
    });
  }

  addElse(blockId: string): void {
    this.mutateBranches(blockId, branches => {
      if (branches.some(b => b.kind === 'else')) return branches;
      return [...branches, { id: generateBlockId(), kind: 'else', children: [] }];
    });
  }

  removeBranch(blockId: string, branchId: string): void {
    this.mutateBranches(blockId, branches =>
      branches.filter((b, i) => !(b.id === branchId && i !== 0)),
    );
  }

  private mutateBranches(
    blockId: string,
    fn: (branches: ActionBranch[]) => ActionBranch[],
  ): void {
    const updateList = (list: ActionBlock[]): ActionBlock[] =>
      list.map(block => {
        const updated = mapDescendants(block, updateList);
        if (updated.id !== blockId || !updated.branches) return updated;
        return { ...updated, branches: fn(updated.branches) };
      });

    this.emit(this.flows().map(flow => ({ ...flow, children: updateList(flow.children) })));
  }

  addEventTrigger(): string {
    const triggerId = generateBlockId();
    this.emit([
      ...this.flows(),
      { triggerId, triggerType: EVENT_TRIGGER_TYPE, children: [] },
    ]);
    this.selectedTriggerId.set(triggerId);
    return triggerId;
  }

  removeEventTrigger(triggerId: string): void {
    this.emit(this.flows().filter(flow => flow.triggerId !== triggerId));
  }

  // Idempotent: a second call for an already-added trigger re-selects it instead of creating a second
  // empty flow for the same type (#480). The pushed flow is byte-identical to what
  // ensureSelectedFlow produces, so an action picked into it lands here rather than in a duplicate.
  addTrigger(triggerType: string): void {
    if (!this.flows().some(f => !isEventFlow(f) && isSameTriggerType(f.triggerType, triggerType))) {
      this.emit([
        ...this.flows(),
        {
          triggerId: triggerType,
          triggerType,
          triggerLabel: this.triggerLabelFn(triggerType) ?? triggerType,
          children: [],
        },
      ]);
    }
    this.selectedTriggerId.set(triggerType);
  }

  removeTrigger(triggerType: string): void {
    const next = this.flows().filter(f => isEventFlow(f) || !isSameTriggerType(f.triggerType, triggerType));
    if (next.length === this.flows().length) return;
    this.emit(next);
    if (isSameTriggerType(this.selectedTriggerId(), triggerType)) {
      this.selectedTriggerId.set(DEFAULT_TRIGGER_TYPE);
    }
  }

  updateEventBinding(triggerId: string, binding: EventTriggerBinding): void {
    this.emit(
      this.flows().map(flow => (flow.triggerId === triggerId ? { ...flow, event: binding } : flow)),
    );
  }

  private upsertEventParam(
    parameters: ActionBlockParameter[],
    paramName: string,
    update: (existing: ActionBlockParameter | undefined) => ActionBlockParameter,
  ): ActionBlockParameter[] {
    const index = parameters.findIndex(p => p.name === paramName);
    if (index === -1) return [...parameters, update(undefined)];
    const next = [...parameters];
    next[index] = update(next[index]);
    return next;
  }

  updateEventParam(
    triggerId: string,
    paramName: string,
    value: ParameterValue,
    valueLabel?: string,
  ): void {
    this.emit(
      this.flows().map(flow => {
        if (flow.triggerId !== triggerId || !flow.event) return flow;
        const parameters = this.upsertEventParam(flow.event.parameters ?? [], paramName, existing => {
          const keepLabel = valueLabel === undefined && existing?.value === value;
          return {
            ...(existing ?? { name: paramName, type: 'string' as const, label: paramName, value: null }),
            value,
            valueLabel: keepLabel ? existing?.valueLabel : valueLabel,
          };
        });
        return { ...flow, event: { ...flow.event, parameters } };
      }),
    );
  }

  updateEventParamOperator(triggerId: string, paramName: string, operator: ComparisonOperator): void {
    this.emit(
      this.flows().map(flow => {
        if (flow.triggerId !== triggerId || !flow.event) return flow;
        const parameters = this.upsertEventParam(flow.event.parameters ?? [], paramName, existing => ({
          ...(existing ?? { name: paramName, type: 'number' as const, label: paramName, value: null }),
          operator: operator === '==' ? undefined : operator,
        }));
        return { ...flow, event: { ...flow.event, parameters } };
      }),
    );
  }

  updateEventFilter(triggerId: string, filter: ConditionExpression | undefined): void {
    this.emit(
      this.flows().map(flow =>
        flow.triggerId === triggerId && flow.event
          ? { ...flow, event: { ...flow.event, filter } }
          : flow,
      ),
    );
  }

  async runSelectedFlow(): Promise<RunActionFlowResponse | null> {
    const flow = this.selectedFlow();
    if (!flow || this.running()) return null;

    this.running.set(true);
    try {
      return await this.api.runActionFlow({
        flows: JSON.stringify(this.flows()),
        triggerId: flow.triggerId,
        scope: this.previewScope(),
        scopeRefId: this.previewScopeRefId(),
        clientId: this.api.clientId,
      });
    } catch {
      return {
        success: false,
        error: {
          code: 'NETWORK_ERROR',
          message: this.localization.translateKey(AppStrings.ActionBuilder.Store.RunFailed),
        },
      };
    } finally {
      this.running.set(false);
    }
  }

  private resolveTriggerId(selected: string): string {
    const byId = this.flows().find(flow => flow.triggerId === selected);
    if (byId) return selected;
    const byType = this.flows().find(flow => isSameTriggerType(flow.triggerType, selected));
    return byType?.triggerId ?? selected;
  }

  private emit(flows: ActionFlow[]): void {
    this.flows.set(flows);
    this.emitFn(flows);
  }
}
