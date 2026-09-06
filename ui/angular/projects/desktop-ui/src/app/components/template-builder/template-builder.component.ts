import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import type { Variable, VariableScope } from '@macro-deck/runtime';
import { TemplateEditorComponent } from './template-editor.component';

@Component({
  selector: 'shared-template-builder',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe, TemplateEditorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="tb-trigger"
      [title]="openEditorLabel()"
      [attr.aria-label]="openEditorLabel()"
      [disabled]="disabled"
      (click)="open.set(true)">
      <span class="tb-trigger-symbol">{{ '{{ }}' }}</span>
    </button>

    @if (open()) {
      <shared-modal
        [heading]="'macrodeck.app:TemplateBuilder.Heading' | translate"
        size="large"
        [flush]="true"
        [style.--modal-height]="'min(86vh, 46rem)'"
        [showFooter]="true"
        (close)="cancel()">
        <shared-template-editor
          [variables]="variables"
          [scope]="scope"
          [scopeRefId]="scopeRefId"
          [value]="draft()"
          (valueChange)="draft.set($event)" />

        <div modal-footer class="tb-footer">
          <shared-button variant="secondary" type="button" (click)="cancel()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
          <shared-button variant="primary" type="button" (click)="apply()">{{ 'macrodeck.app:TemplateBuilder.Insert' | translate }}</shared-button>
        </div>
      </shared-modal>
    }
  `,
  styleUrls: ['./template-builder.component.scss'],
})
export class TemplateBuilderComponent {
  @Input() variables: Variable[] = [];

  @Input() set value(value: string) {
    this.currentValue = value ?? '';
    if (!this.open()) {
      this.draft.set(this.currentValue);
    }
  }
  get value(): string { return this.currentValue; }

  @Input() scope: VariableScope = 'global';
  @Input() scopeRefId?: string;

  @Input() disabled = false;

  @Output() apply$ = new EventEmitter<string>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly open = signal(false);
  readonly draft = signal('');

  private currentValue = '';
  private readonly localization = inject(LocalizationService);

  readonly openEditorLabel = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.OpenEditor));

  apply(): void {
    dismissModal(this.modal, () => {
      this.apply$.emit(this.draft());
      this.open.set(false);
    });
  }

  cancel(): void {
    dismissModal(this.modal, () => {
      this.draft.set(this.currentValue);
      this.open.set(false);
    });
  }
}
