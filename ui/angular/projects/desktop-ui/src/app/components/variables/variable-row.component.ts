import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { VariableType } from '@macro-deck/runtime';
import { variableTypeLabels } from '../../domain/variable-source.util';

export const VARIABLE_ROW_HEIGHT = 52;

const DEPTH_INDENT_PX = 20;

@Component({
  selector: 'shared-variable-row',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variable-row.component.html',
  styleUrls: ['./variable-row.component.scss'],
  host: {
    '[class.vr-unbound]': 'unbound()',
    '[class.vr-dimmed]': 'dimmed()',
    '[style.padding-inline-start.px]': 'indent()',
  },
})
export class VariableRowComponent {
  private readonly localization = inject(LocalizationService);

  readonly primary = input.required<string>();
  readonly primaryMono = input(true);
  readonly secondary = input<string | null>(null);
  readonly secondaryMono = input(false);
  readonly type = input<VariableType | null | undefined>(null);
  readonly value = input<string | null>(null);
  readonly unavailable = input<string | null>(null);
  readonly actionLabel = input<string | null>(null);
  readonly interactive = input(false);
  readonly role = input<string | null>(null);
  readonly title = input<string | null>(null);
  readonly depth = input(0);
  readonly unbound = input(false);
  readonly dimmed = input(false);
  readonly branch = input(false);
  readonly expanded = input(false);

  readonly activate = output<void>();

  protected readonly indent = computed(() => this.depth() * DEPTH_INDENT_PX);

  protected readonly unavailableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Manager.ValueUnavailableShort));

  protected readonly typeLabel = computed(() => {
    const type = this.type();
    return type ? variableTypeLabels(key => this.localization.translateKey(key))[type] : null;
  });
}
