import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import type { ActionBlock } from '@macro-deck/runtime';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';
import { ParamListComponent } from '../param-list/param-list.component';
import { LoopBodySectionComponent } from '../sections/loop-body-section.component';

@Component({
  selector: 'shared-repeat-card',
  standalone: true,
  imports: [ActionCardFrameComponent, ParamListComponent, LoopBodySectionComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-action-card-frame [block]="block" [collapsible]="false">
      <shared-param-list [block]="block" />
      <shared-loop-body-section [block]="block" />
    </shared-action-card-frame>
  `,
})
export class RepeatCardComponent {
  @Input({ required: true }) block!: ActionBlock;
}
