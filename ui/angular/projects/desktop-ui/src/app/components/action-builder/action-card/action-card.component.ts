import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import type { ActionBlock } from '@macro-deck/runtime';
import { GenericActionCardComponent } from './cards/generic-action-card.component';
import { WaitCardComponent } from './cards/wait-card.component';
import { IfElseCardComponent } from './cards/if-else-card.component';
import { RepeatCardComponent } from './cards/repeat-card.component';
import { WhileCardComponent } from './cards/while-card.component';
import { BreakCardComponent } from './cards/break-card.component';

@Component({
  selector: 'shared-action-card',
  standalone: true,
  imports: [
    GenericActionCardComponent,
    WaitCardComponent,
    IfElseCardComponent,
    RepeatCardComponent,
    WhileCardComponent,
    BreakCardComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (block.blockType) {
      @case ('wait')       { <shared-wait-card [block]="block" /> }
      @case ('ifElse')     { <shared-if-else-card [block]="block" /> }
      @case ('repeatLoop') { <shared-repeat-card [block]="block" /> }
      @case ('whileLoop')  { <shared-while-card [block]="block" /> }
      @case ('break')      { <shared-break-card [block]="block" /> }
      @default             { <shared-generic-action-card [block]="block" /> }
    }
  `,
})
export class ActionCardComponent {
  @Input({ required: true }) block!: ActionBlock;
}
