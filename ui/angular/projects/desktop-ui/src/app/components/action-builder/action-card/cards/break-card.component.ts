import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import type { ActionBlock } from '@macro-deck/runtime';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';

@Component({
  selector: 'shared-break-card',
  standalone: true,
  imports: [ActionCardFrameComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<shared-action-card-frame [block]="block" [collapsible]="false" />`,
})
export class BreakCardComponent {
  @Input({ required: true }) block!: ActionBlock;
}
