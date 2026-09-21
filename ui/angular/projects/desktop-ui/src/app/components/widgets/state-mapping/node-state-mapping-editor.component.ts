import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';

import type { ButtonStateDefinition, ButtonStateMapping, UiNodeOption } from '@macro-deck/runtime';
import { TranslatePipe, VariableService } from '@shared';
import { UiRenderContext } from '../../ui-render/ui-render-context';
import { StateMappingModalComponent } from './state-mapping-modal.component';

export interface StateMappingValue {
  rules: ButtonStateMapping['rules'];
  fallbackStateId: string;
}

@Component({
  selector: 'shared-node-state-mapping-editor',
  standalone: true,
  imports: [StateMappingModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="state-mapping-row"
      [title]="'macrodeck.app:Widgets.Editor.ConfigureStateMapping' | translate"
      (click)="open()">
      <span class="icon icon-sliders icon-sm state-mapping-icon" aria-hidden="true"></span>
      <span class="state-mapping-label">{{ 'macrodeck.app:Widgets.Editor.StateMapping' | translate }}</span>
      <span class="state-mapping-value">
        @if (ruleCount() > 0) {
          {{ 'macrodeck.app:Widgets.Editor.MappingRuleCount' | translate: { count: ruleCount() } }}
        } @else {
          {{ 'macrodeck:Common.None' | translate }}
        }
      </span>
      <span class="icon icon-chevron-right icon-xs state-mapping-chevron" aria-hidden="true"></span>
    </button>

    @if (isOpen()) {
      <app-state-mapping-modal
        [states]="states()"
        [mapping]="draft() ?? mapping()"
        [variables]="variables()"
        [scopeRefId]="scopeRefId()"
        (mappingChange)="onMappingChange($event)"
        (save)="onSave($event)"
        (closed)="onClosed()" />
    }
  `,
  styles: `
    @use '../../../../styles/index' as ds;

    .state-mapping-row {
      @include ds.control-base;
      @include ds.control-interactive;
      display: flex;
      align-items: center;
      gap: var(--space-2);
      width: 100%;
      min-width: 0;
      color: var(--color-text-primary);
      font-size: var(--text-sm);
      text-align: left;
      cursor: pointer;

      &:hover {
        background-color: var(--color-bg-hover);
      }
    }

    .state-mapping-icon,
    .state-mapping-chevron {
      flex: 0 0 auto;
      color: var(--color-text-muted);
    }

    .state-mapping-label {
      flex: 1 1 auto;
    }

    .state-mapping-value {
      color: var(--color-text-secondary);
    }
  `,
})
export class NodeStateMappingEditorComponent {
  private readonly variableService = inject(VariableService);
  private readonly context = inject(UiRenderContext);

  readonly value = input<StateMappingValue | undefined>(undefined);
  readonly stateOptions = input<UiNodeOption[]>([]);

  readonly valueChange = output<StateMappingValue>();

  protected readonly isOpen = signal(false);
  protected readonly draft = signal<ButtonStateMapping | null>(null);

  protected readonly scopeRefId = computed(() => this.context.scopeRefId);

  // The condition builder needs the widget's own visible variables, exactly like the action editor's
  // condition rows do - a state mapping only ever exists in state mode, so that half of
  // `visibleForActionButtonEditor`'s two-mode split is always the one that applies here.
  protected readonly variables = computed(() => this.variableService.visibleForActionButtonEditor(this.scopeRefId(), true));

  protected readonly states = computed<ButtonStateDefinition[]>(() =>
    this.stateOptions().map(option => ({ id: option.value, label: option.label ?? option.value })));

  protected readonly mapping = computed<ButtonStateMapping>(() => {
    const value = this.value();
    const states = this.states();
    // A stored fallback that names no current state - the empty string a button starts with, or an id
    // left behind by a deleted state - preselects the first state instead. The fallback is required,
    // and the modal's Save stays disabled until it names a real one with nothing on screen saying why.
    const stored = value?.fallbackStateId ?? '';
    return {
      rules: value?.rules ?? [],
      fallbackStateId: states.some(state => state.id === stored) ? stored : states[0]?.id ?? '',
    };
  });

  protected readonly ruleCount = computed(() => this.mapping().rules.length);

  protected open(): void {
    this.draft.set(this.mapping());
    this.isOpen.set(true);
  }

  protected onMappingChange(mapping: ButtonStateMapping): void {
    this.draft.set(mapping);
  }

  protected onSave(mapping: ButtonStateMapping): void {
    this.isOpen.set(false);
    this.draft.set(null);
    this.valueChange.emit({ rules: mapping.rules, fallbackStateId: mapping.fallbackStateId });
  }

  protected onClosed(): void {
    this.isOpen.set(false);
    this.draft.set(null);
  }
}
