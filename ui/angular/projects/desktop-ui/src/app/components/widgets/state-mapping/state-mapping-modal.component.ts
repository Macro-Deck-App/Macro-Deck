import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges, ViewChild, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonStateDefinition, ButtonStateMapping, ButtonStateMappingRule, ConditionExpression, Variable, createEmptyComparison, createStateId } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { comparisonOperatorOptions } from '../../action-builder/default-action-defs';
import { ConditionEvalService } from '../../action-builder/services/condition-eval.service';
import { ConditionBuilderComponent, LeafStateLookup } from '../../condition-builder/condition-builder.component';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { isConditionComplete } from '../../../domain/action-flow-validation.util';

@Component({
  selector: 'app-state-mapping-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, SelectComponent, ConditionBuilderComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // `ConditionEvalService` is `@Injectable()` with no `providedIn: 'root'` - the original editor
  // relied on `ActionBuilderComponent` providing its own instance up the tree, since this modal
  // always opened from inside it. Reached from `NodeStateMappingEditorComponent` instead now (issue
  // #837), which is not necessarily an `ActionBuilderComponent` descendant, so this owns its instance.
  providers: [ConditionEvalService],
  templateUrl: './state-mapping-modal.component.html',
  styleUrls: ['./state-mapping-modal.component.scss'],
})
export class StateMappingModalComponent implements OnInit, OnChanges, OnDestroy {
  @Input({ required: true }) states: ButtonStateDefinition[] = [];
  @Input({ required: true }) mapping!: ButtonStateMapping;
  @Input() variables: Variable[] = [];
  @Input() scopeRefId?: string;

  @Output() mappingChange = new EventEmitter<ButtonStateMapping>();
  @Output() save = new EventEmitter<ButtonStateMapping>();
  @Output() closed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly conditionEval = inject(ConditionEvalService);
  private readonly localization = inject(LocalizationService);

  protected readonly comparisonOperators = computed(() =>
    comparisonOperatorOptions(key => this.localization.translateKey(key)));

  private readonly registeredRuleIds = new Set<string>();

  protected get stateOptions(): SelectOption[] {
    return this.states.map(state => ({ value: state.id, label: state.label }));
  }

  ngOnInit(): void {
    this.syncRegistrations();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['mapping']) {
      this.syncRegistrations();
    }
  }

  ngOnDestroy(): void {
    for (const id of this.registeredRuleIds) {
      this.conditionEval.unregister(this.slotKey(id));
    }
    this.registeredRuleIds.clear();
  }

  protected leafStateFor(ruleId: string): LeafStateLookup {
    return (leafId: string) => this.conditionEval.leafState(this.slotKey(ruleId), leafId);
  }

  // Reordering is up/down buttons rather than drag: the two existing pointer-drag services
  // (ActionDragService, and the deck's own drag/marquee services) are each ~250-line state machines
  // that exist only because Tauri swallows the HTML5 drag event (issue #281), and this list is
  // bounded and never nests - not enough to justify standing up a third one. Swapping array slots
  // (not rebuilding the rule objects) is what keeps each rule's `id`/`when`/`stateId` identical.
  protected moveUp(index: number): void {
    if (index <= 0) return;
    const rules = [...this.mapping.rules];
    [rules[index - 1], rules[index]] = [rules[index], rules[index - 1]];
    this.emitRules(rules);
  }

  protected moveDown(index: number): void {
    if (index >= this.mapping.rules.length - 1) return;
    const rules = [...this.mapping.rules];
    [rules[index], rules[index + 1]] = [rules[index + 1], rules[index]];
    this.emitRules(rules);
  }

  protected onRuleStateChange(ruleId: string, stateId: string): void {
    this.emitRules(this.mapping.rules.map(rule => rule.id === ruleId ? { ...rule, stateId } : rule));
  }

  protected onRuleConditionChange(ruleId: string, when: ConditionExpression): void {
    this.emitRules(this.mapping.rules.map(rule => rule.id === ruleId ? { ...rule, when } : rule));
  }

  protected addRule(): void {
    const newRule: ButtonStateMappingRule = {
      id: createStateId(),
      stateId: this.states[0]?.id ?? '',
      when: createEmptyComparison(),
    };
    this.emitRules([...this.mapping.rules, newRule]);
  }

  protected removeRule(ruleId: string): void {
    this.emitRules(this.mapping.rules.filter(rule => rule.id !== ruleId));
  }

  protected onFallbackChange(fallbackStateId: string): void {
    this.mappingChange.emit({ ...this.mapping, fallbackStateId });
  }

  protected canSave(): boolean {
    const ids = new Set(this.states.map(state => state.id));
    if (!ids.has(this.mapping.fallbackStateId)) return false;
    return this.mapping.rules.every(rule => ids.has(rule.stateId) && isConditionComplete(rule.when));
  }

  protected onSave(): void {
    if (!this.canSave()) return;
    dismissModal(this.modal, () => this.save.emit(this.mapping));
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }

  private emitRules(rules: ButtonStateMappingRule[]): void {
    this.mappingChange.emit({ ...this.mapping, rules });
  }

  private syncRegistrations(): void {
    const currentIds = new Set(this.mapping.rules.map(rule => rule.id));
    for (const id of [...this.registeredRuleIds]) {
      if (!currentIds.has(id)) {
        this.conditionEval.unregister(this.slotKey(id));
        this.registeredRuleIds.delete(id);
      }
    }
    for (const rule of this.mapping.rules) {
      this.conditionEval.register(this.slotKey(rule.id), rule.when);
      this.registeredRuleIds.add(rule.id);
    }
  }

  private slotKey(ruleId: string): string {
    return `mapping:${ruleId}`;
  }
}
