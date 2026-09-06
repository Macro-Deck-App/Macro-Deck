import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';

import type { ActionBlockDefinition } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, TranslatePipe } from '@shared';
import { defaultActionDefs } from '../default-action-defs';
import { ActionFlowStore } from '../services/action-flow.store';
import { ActionPickerComponent } from './action-picker.component';

@Component({
  selector: 'shared-node-action-picker',
  standalone: true,
  imports: [ActionPickerComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [ActionFlowStore],
  template: `
    @if (selectedLabel(); as label) {
      <span class="config-node-chip">
        <span class="config-node-chip-text">{{ label }}</span>
        <shared-button variant="ghost" size="compact" (click)="valueChange.emit('')">{{
          'macrodeck:Common.Remove' | translate
        }}</shared-button>
      </span>
    } @else {
      <shared-button variant="secondary" size="compact" (click)="open.set(true)">{{
        'macrodeck:Common.Select' | translate
      }}</shared-button>
    }
    @if (open()) {
      <shared-action-picker
        [definitions]="definitions()"
        (picked)="onPicked($event)"
        (closed)="open.set(false)" />
    }
  `,
  styles: `
    .config-node-chip {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      padding: var(--space-1) var(--space-2);
      background-color: var(--color-bg-hover);
      border-radius: var(--radius-sm);
      color: var(--color-text-primary);
      font-size: var(--text-sm);
    }
  `,
})
export class NodeActionPickerComponent {
  private readonly localization = inject(LocalizationService);

  readonly value = input('');
  readonly valueChange = output<string>();

  protected readonly open = signal(false);
  protected readonly definitions = computed<ActionBlockDefinition[]>(() =>
    defaultActionDefs(key => this.localization.translateKey(key)),
  );
  protected readonly selectedLabel = computed(() => {
    const id = this.value();
    if (!id) return null;
    return this.definitions().find(def => def.blockType === id)?.label ?? id;
  });

  protected onPicked(def: ActionBlockDefinition): void {
    this.open.set(false);
    this.valueChange.emit(def.blockType);
  }
}
