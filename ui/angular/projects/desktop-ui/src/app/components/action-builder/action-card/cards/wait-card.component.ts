import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { LocalizationService } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';
import { ParamListComponent } from '../param-list/param-list.component';
import { summarizeParameters } from '../summary.util';

@Component({
  selector: 'shared-wait-card',
  standalone: true,
  imports: [ActionCardFrameComponent, ParamListComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-action-card-frame [block]="block" [summary]="summary">
      <shared-param-list [block]="block" />
    </shared-action-card-frame>
  `,
})
export class WaitCardComponent {
  @Input({ required: true }) block!: ActionBlock;

  private readonly localization = inject(LocalizationService);

  get summary(): string {
    return summarizeParameters(this.block.parameters, (key, args) => this.localization.translateKey(key, args));
  }
}
