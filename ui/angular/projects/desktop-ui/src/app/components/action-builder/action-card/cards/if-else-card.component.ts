import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import type { ActionBlock } from '@macro-deck/runtime';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';
import { BranchListComponent } from '../sections/branch-list.component';

@Component({
  selector: 'shared-if-else-card',
  standalone: true,
  imports: [ActionCardFrameComponent, BranchListComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-action-card-frame [block]="block" [collapsible]="false">
      <shared-branch-list [block]="block" />
    </shared-action-card-frame>
  `,
})
export class IfElseCardComponent {
  @Input({ required: true }) block!: ActionBlock;
}
