import { ChangeDetectionStrategy, Component, Input, computed, forwardRef, inject } from '@angular/core';

import { AppStrings, isEventReference, isVariableReference, variableTokenText } from '@macro-deck/runtime';
import type { ActionBlock, ParameterValue } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { VariablePickerComponent } from '../../../variable-picker/variable-picker.component';
import { VariableTextInputComponent } from '../../../forms/variable-text-input/variable-text-input.component';
import { ActionFlowStore } from '../../services/action-flow.store';
import { NestedActionListComponent } from '../nested-action-list.component';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';

@Component({
  selector: 'shared-switch-card',
  standalone: true,
  imports: [
    ActionCardFrameComponent,
    VariablePickerComponent,
    VariableTextInputComponent,
    forwardRef(() => NestedActionListComponent),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../sections/branch-list.component.scss', './switch-card.component.scss'],
  template: `
    <shared-action-card-frame [block]="block" [collapsible]="false">
      <div class="block-branches">
        <div class="block-section block-section-condition">
          <h4 class="block-section-label">{{ subjectLabel() }}:</h4>
          <div class="block-section-content switch-operand">
            <shared-variable-text-input
              class="switch-operand-input"
              [value]="operandText(block.subject)"
              [variables]="store.pickerVariables()"
              [ariaLabel]="subjectAriaLabel()"
              (valueChange)="store.updateSwitchSubject(block.id, $event)" />
            <shared-variable-picker
              class="switch-operand-picker"
              [variables]="store.pickerVariables()"
              (pick)="store.updateSwitchSubject(block.id, { $var: $event })"
              (pickEventParameter)="store.updateSwitchSubject(block.id, { $event: $event })" />
          </div>
        </div>

        @for (branch of block.branches ?? []; track branch.id) {
          @if (branch.kind === 'case') {
            <div class="block-section block-section-condition">
              <h4 class="block-section-label">{{ caseLabel() }}:</h4>
              <div class="block-section-content switch-operand">
                <shared-variable-text-input
                  class="switch-operand-input"
                  [value]="operandText(branch.value)"
                  [variables]="store.pickerVariables()"
                  [ariaLabel]="caseValueAriaLabel()"
                  (valueChange)="store.updateCaseValue(block.id, branch.id, $event)" />
                <shared-variable-picker
                  class="switch-operand-picker"
                  [variables]="store.pickerVariables()"
                  (pick)="store.updateCaseValue(block.id, branch.id, { $var: $event })"
                  (pickEventParameter)="store.updateCaseValue(block.id, branch.id, { $event: $event })" />
              </div>
              @if (caseCount > 1) {
                <button
                  type="button"
                  class="block-section-remove"
                  (click)="store.removeCase(block.id, branch.id)"
                  [attr.aria-label]="removeCaseAriaLabel()">
                  <span class="icon icon-x icon-xs"></span>
                </button>
              }
            </div>
          } @else {
            <div class="block-section">
              <h4 class="block-section-label">{{ otherwiseLabel() }}:</h4>
              <div class="block-section-content">
                <shared-action-list [listId]="listId(branch.id)" [items]="branch.children" />
              </div>
              <button
                type="button"
                class="block-section-remove"
                (click)="store.removeBranch(block.id, branch.id)"
                [attr.aria-label]="removeOtherwiseAriaLabel()">
                <span class="icon icon-x icon-xs"></span>
              </button>
            </div>
          }
          @if (branch.kind === 'case') {
            <div class="block-section">
              <h4 class="block-section-label">{{ thenLabel() }}:</h4>
              <div class="block-section-content">
                <shared-action-list [listId]="listId(branch.id)" [items]="branch.children" />
              </div>
            </div>
          }
        }

        <div class="block-branches-footer">
          <button type="button" class="block-section-add-meta" (click)="store.addCase(block.id)">
            {{ addCaseLabel() }}
          </button>
          @if (!hasElseBranch) {
            <button type="button" class="block-section-add-meta" (click)="store.addElse(block.id)">
              {{ addOtherwiseLabel() }}
            </button>
          }
        </div>
      </div>
    </shared-action-card-frame>
  `,
})
export class SwitchCardComponent {
  @Input({ required: true }) block!: ActionBlock;

  protected readonly store = inject(ActionFlowStore);
  private readonly localization = inject(LocalizationService);

  private t(key: string): string {
    return this.localization.translateKey(key);
  }

  protected readonly subjectLabel = computed(() => this.t(AppStrings.ActionBuilder.Switch.SubjectLabel));
  protected readonly subjectAriaLabel = computed(() => this.t(AppStrings.ActionBuilder.Switch.SubjectAriaLabel));
  protected readonly caseLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.Case));
  protected readonly caseValueAriaLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.CaseValueAriaLabel));
  protected readonly removeCaseAriaLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.RemoveCaseAriaLabel));
  protected readonly addCaseLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.AddCase));
  protected readonly thenLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.Then));
  protected readonly otherwiseLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.Otherwise));
  protected readonly addOtherwiseLabel = computed(() => this.t(AppStrings.ActionBuilder.Branch.AddOtherwise));
  protected readonly removeOtherwiseAriaLabel = computed(() =>
    this.t(AppStrings.ActionBuilder.Branch.RemoveOtherwiseAriaLabel));

  get hasElseBranch(): boolean {
    return this.block.branches?.some(b => b.kind === 'else') ?? false;
  }

  get caseCount(): number {
    return this.block.branches?.filter(b => b.kind === 'case').length ?? 0;
  }

  listId(branchId: string): string {
    return `branch:${this.block.id}:${branchId}`;
  }

  operandText(value: ParameterValue | undefined): string {
    if (isVariableReference(value)) return variableTokenText('variable', value.$var);
    if (isEventReference(value)) return variableTokenText('event', value.$event);
    return value === null || value === undefined ? '' : `${value}`;
  }
}
