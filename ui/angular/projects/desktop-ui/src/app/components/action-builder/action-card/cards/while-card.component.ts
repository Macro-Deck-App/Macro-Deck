import { ChangeDetectionStrategy, Component, Input, computed, inject } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';
import { ConditionSectionComponent } from '../sections/condition-section.component';
import { LoopBodySectionComponent } from '../sections/loop-body-section.component';

@Component({
  selector: 'shared-while-card',
  standalone: true,
  imports: [ActionCardFrameComponent, ConditionSectionComponent, LoopBodySectionComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-action-card-frame [block]="block" [collapsible]="false">
      @if (block.condition) {
        <div class="while-condition">
          <shared-condition-section
            [label]="whenLabel()"
            [blockId]="block.id"
            [expression]="block.condition" />
        </div>
      }
      <shared-loop-body-section [block]="block" />
    </shared-action-card-frame>
  `,
  styles: `
    .while-condition {
      display: block;
      margin: 0 0.5rem 0 1.5rem;
    }
  `,
})
export class WhileCardComponent {
  @Input({ required: true }) block!: ActionBlock;

  private readonly localization = inject(LocalizationService);

  protected readonly whenLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Loop.When));
}
