import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  inject,
} from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { ConditionExpression } from '@macro-deck/runtime';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ConditionEvalService, ConditionEvalState } from '../../services/condition-eval.service';
import { ConditionBuilderComponent, LeafStateLookup } from '../../../condition-builder/condition-builder.component';

@Component({
  selector: 'shared-condition-section',
  standalone: true,
  imports: [ConditionBuilderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './condition-section.component.html',
  styleUrls: ['./condition-section.component.scss'],
})
export class ConditionSectionComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) blockId!: string;
  @Input() branchId?: string;
  @Input({ required: true }) expression!: ConditionExpression;
  @Input() removable = false;

  protected readonly store = inject(ActionFlowStore);
  private readonly conditionEval = inject(ConditionEvalService, { optional: true });
  private readonly localization = inject(LocalizationService);

  get removeBranchAriaLabel(): string {
    return this.localization.translateKey(AppStrings.ActionBuilder.ConditionSection.RemoveBranchAriaLabel, {
      label: this.label,
    });
  }

  private registeredKey: string | null = null;

  private get slotKey(): string {
    return `${this.blockId}:${this.branchId ?? '__top__'}`;
  }

  ngOnChanges(): void {
    if (!this.conditionEval) return;
    const key = this.slotKey;
    if (this.registeredKey && this.registeredKey !== key) {
      this.conditionEval.unregister(this.registeredKey);
    }
    this.conditionEval.register(key, this.expression);
    this.registeredKey = key;
  }

  ngOnDestroy(): void {
    if (this.conditionEval && this.registeredKey) {
      this.conditionEval.unregister(this.registeredKey);
    }
  }

  get evalState(): ConditionEvalState | undefined {
    return this.conditionEval?.state(this.slotKey);
  }

  get leafState(): LeafStateLookup {
    return (leafId: string) => this.conditionEval?.leafState(this.slotKey, leafId);
  }

  onExpressionChange(expression: ConditionExpression): void {
    if (this.branchId) {
      this.store.updateBranchCondition(this.blockId, this.branchId, expression);
    } else {
      this.store.updateBlockCondition(this.blockId, expression);
    }
  }

  onRemove(): void {
    if (this.branchId) {
      this.store.removeBranch(this.blockId, this.branchId);
    }
  }
}
