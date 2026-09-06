import {
  ChangeDetectionStrategy,
  Component,
  Input,
  computed,
  forwardRef,
  inject,
} from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { ActionBlock, ActionBranch } from '@macro-deck/runtime';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ConditionSectionComponent } from './condition-section.component';
import { NestedActionListComponent } from '../nested-action-list.component';

@Component({
  selector: 'shared-branch-list',
  standalone: true,
  imports: [ConditionSectionComponent, forwardRef(() => NestedActionListComponent)],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './branch-list.component.html',
  styleUrls: ['./branch-list.component.scss'],
})
export class BranchListComponent {
  @Input({ required: true }) block!: ActionBlock;

  protected readonly store = inject(ActionFlowStore);
  private readonly localization = inject(LocalizationService);

  protected readonly thenLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Branch.Then));
  protected readonly removeOtherwiseAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Branch.RemoveOtherwiseAriaLabel));
  protected readonly addElseIfLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Branch.AddElseIf));
  protected readonly addOtherwiseLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Branch.AddOtherwise));

  branchListId(branchId: string): string {
    return `branch:${this.block.id}:${branchId}`;
  }

  branchLabel(branch: ActionBranch): string {
    const B = AppStrings.ActionBuilder.Branch;
    switch (branch.kind) {
      case 'if':     return this.localization.translateKey(B.If);
      case 'elseif': return this.localization.translateKey(B.ElseIf);
      case 'else':   return this.localization.translateKey(B.Otherwise);
    }
  }

  get hasElseBranch(): boolean {
    return this.block.branches?.some(b => b.kind === 'else') ?? false;
  }
}
