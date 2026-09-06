import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

@Component({
  selector: 'shared-code-editor',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ce-root">
      <div class="ce-toolbar">
        <span class="ce-language">{{ language }}</span>
        @if (jsonError) {
          <span class="ce-error">{{ jsonError }}</span>
        }
      </div>
      <textarea
        class="ce-textarea"
        [class.ce-invalid]="!!jsonError"
        [rows]="rows"
        spellcheck="false"
        [placeholder]="placeholder"
        [ngModel]="value"
        (ngModelChange)="onInput($event)"></textarea>
    </div>
  `,
  styleUrls: ['./code-editor.component.scss'],
})
export class CodeEditorComponent {
  private readonly localization = inject(LocalizationService);

  @Input() value = '';
  @Input() language = 'text';
  @Input() rows = 8;
  @Input() placeholder = '';

  @Output() valueChange = new EventEmitter<string>();

  jsonError: string | null = null;

  onInput(value: string): void {
    this.value = value;
    this.validate(value);
    this.valueChange.emit(value);
  }

  private validate(value: string): void {
    if (this.language.toLowerCase() !== 'json' || value.trim() === '') {
      this.jsonError = null;
      return;
    }

    try {
      JSON.parse(value);
      this.jsonError = null;
    } catch (error) {
      this.jsonError = error instanceof SyntaxError
        ? error.message
        : this.localization.translateKey(AppStrings.Forms.CodeEditor.InvalidJson);
    }
  }
}
