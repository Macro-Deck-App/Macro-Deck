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
import type { ActionBlock } from '@macro-deck/runtime';
import { NestedActionListComponent } from '../nested-action-list.component';

@Component({
  selector: 'shared-loop-body-section',
  standalone: true,
  imports: [forwardRef(() => NestedActionListComponent)],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="block-section loop-body">
      <h4 class="block-section-label">{{ doLabel() }}:</h4>
      <div class="block-section-content">
        <shared-action-list
          [listId]="'children:' + block.id"
          [items]="block.children ?? []" />
      </div>
    </div>
  `,
  styleUrls: ['./block-section.scss', './loop-body-section.component.scss'],
})
export class LoopBodySectionComponent {
  @Input({ required: true }) block!: ActionBlock;

  private readonly localization = inject(LocalizationService);

  protected readonly doLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Loop.Do));
}
