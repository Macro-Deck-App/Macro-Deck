import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService } from '@shared';
import type { Variable, VariableCatalogNode, VariableType } from '@macro-deck/runtime';
import { VariableCatalogService } from '../../services/variable-catalog.service';
import { variableTypeLabels } from '../../domain/variable-source.util';
import { VariableBindDialogComponent } from './variable-bind-dialog.component';

@Component({
  selector: 'shared-variable-catalog-id-input',
  standalone: true,
  imports: [FormsModule, ButtonComponent, InputComponent, VariableBindDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './variable-catalog-id-input.component.html',
  styleUrls: ['./variable-catalog-id-input.component.scss'],
})
export class VariableCatalogIdInputComponent {
  @Input({ required: true }) integrationId!: string;

  readonly acceptedTypes = input<readonly VariableType[]>([]);

  readonly writableOnly = input(false);

  @Output() bound = new EventEmitter<Variable>();

  private readonly variableCatalog = inject(VariableCatalogService);
  private readonly localization = inject(LocalizationService);

  readonly resourceId = signal('');
  readonly resolving = signal(false);
  readonly resolved = signal<VariableCatalogNode | null>(null);
  readonly failed = signal(false);
  readonly showBindDialog = signal(false);

  readonly canResolve = computed(() => !this.resolving() && this.resourceId().trim().length > 0);

  readonly canBind = computed(() => {
    const node = this.resolved();
    if (!node?.type) {
      return false;
    }
    const accepted = this.acceptedTypes();
    if (accepted.length > 0 && !accepted.includes(node.type)) {
      return false;
    }
    return !this.writableOnly() || node.canWrite === true;
  });

  readonly typeLabels = computed(() => variableTypeLabels(key => this.localization.translateKey(key)));

  readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.ManualIdHeading));
  readonly placeholder = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.ManualIdPlaceholder));
  readonly hint = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.ManualIdHint));
  readonly resolveActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.ResolveAction));
  readonly bindActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.BindAction));
  readonly resolveFailedMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.ResolveFailed));
  readonly notBindableMessage = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Dynamic.NotBindable));

  resolvedName(): string {
    const node = this.resolved();
    if (!node) return '';
    return resolveLocalizedText(node.displayName, this.localization) || node.name;
  }

  onIdInput(value: string): void {
    this.resourceId.set(value);
    this.resolved.set(null);
    this.failed.set(false);
  }

  async resolve(): Promise<void> {
    if (!this.canResolve()) return;
    this.resolving.set(true);
    this.failed.set(false);
    this.resolved.set(null);
    try {
      const response = await this.variableCatalog.resolve(this.integrationId, this.resourceId().trim());
      if (response?.node) {
        this.resolved.set(response.node);
      } else {
        this.failed.set(true);
      }
    } catch {
      this.failed.set(true);
    } finally {
      this.resolving.set(false);
    }
  }

  openBind(): void {
    this.showBindDialog.set(true);
  }

  onBound(variable: Variable): void {
    this.showBindDialog.set(false);
    this.resolved.set(null);
    this.resourceId.set('');
    this.bound.emit(variable);
  }

  onBindDialogClose(): void {
    this.showBindDialog.set(false);
  }
}
