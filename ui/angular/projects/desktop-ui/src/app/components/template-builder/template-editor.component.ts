import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Inject,
  Input,
  Optional,
  Output,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe, VariableService } from '@shared';
import type { Variable, VariableScope, VariableType } from '@macro-deck/runtime';
import {
  TEMPLATE_PREVIEW_SERVICE,
  TemplatePreviewError,
  TemplatePreviewService,
  TemplateVariablePreviewOptions,
} from '../../domain/template-preview.interface';
import { templateVariableErrorMessage } from '../../domain/template-variable-error.util';
import { TemplateBrowserComponent } from './template-browser.component';
import { LiquidEditorComponent } from './liquid-editor.component';
import { SnippetInsertion } from './template-snippet-list.component';

@Component({
  selector: 'shared-template-editor',
  standalone: true,
  imports: [TemplateBrowserComponent, LiquidEditorComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-template-browser
      [variables]="variables"
      [scopeLabel]="scopeLabel"
      (insert)="onBrowserInsert($event)" />

    <div class="tb-editor-pane">
      <shared-liquid-editor
        #editor
        class="tb-editor-host"
        [value]="draft()"
        [variables]="variables"
        [hintText]="hintText"
        [placeholder]="editorPlaceholder()"
        (valueChange)="onDraftChange($event)" />

      <div class="tb-preview-wrap">
        <span class="tb-preview-label">{{ 'macrodeck.app:TemplateBuilder.Preview' | translate }}</span>
        <pre class="tb-preview" [class.tb-preview-stale]="previewError()">{{ previewText() || ' ' }}</pre>
        @if (previewError(); as error) {
          <p class="tb-preview-error">
            @if (error.code) {
              <span class="tb-preview-error-code">{{ error.code }}</span>
            }
            {{ error.message }}
          </p>
        }
        @if (resultType) {
          @if (typedError(); as typed) {
            <p class="tb-preview-error tb-typed-error" role="alert">{{ typed }}</p>
          } @else if (typedValue() !== null) {
            <p class="tb-typed-value">
              <span class="tb-preview-label">{{ 'macrodeck.app:Variables.Manager.TemplateResult' | translate }}</span>
              <span class="tb-typed-value-text">{{ typedValue() }}</span>
            </p>
          }
        }
      </div>
    </div>
  `,
  styleUrls: ['./template-editor.component.scss'],
})
export class TemplateEditorComponent {
  @Input() variables: Variable[] = [];

  @Input() scope: VariableScope = 'global';
  @Input() scopeRefId?: string;

  @Input() set scopeLabel(value: string | undefined) {
    this.scopeLabelOverride = value;
  }
  get scopeLabel(): string {
    return this.scopeLabelOverride
      ?? this.localization.translateKey(AppStrings.Variables.ThisWidget);
  }

  @Input() set value(value: string) {
    if (value === this.draft()) return;
    this.draft.set(value ?? '');
    this.refreshPreview(value ?? '');
  }
  get value(): string { return this.draft(); }

  @Input() set resultType(value: VariableType | undefined) {
    this.typedOptions = value ? { ...this.typedOptions, resultType: value } : undefined;
    this.refreshPreview(this.draft());
  }
  get resultType(): VariableType | undefined { return this.typedOptions?.resultType; }

  @Input() set resultOptions(value: Omit<TemplateVariablePreviewOptions, 'resultType'> | undefined) {
    const next = value ?? {};
    if (JSON.stringify(next) === JSON.stringify(this.typedExtras)) return;
    this.typedExtras = next;
    if (this.typedOptions) {
      this.typedOptions = { ...this.typedExtras, resultType: this.typedOptions.resultType };
      this.refreshPreview(this.draft());
    }
  }

  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('editor') private editor?: LiquidEditorComponent;

  readonly draft = signal('');
  readonly previewText = signal('');
  readonly previewError = signal<{ code?: string; message: string } | null>(null);
  readonly typedValue = signal<string | null>(null);
  readonly typedError = signal<string | null>(null);

  private typedOptions?: TemplateVariablePreviewOptions;
  private typedExtras: Omit<TemplateVariablePreviewOptions, 'resultType'> = {};

  private scopeLabelOverride?: string;
  private refreshHandle: ReturnType<typeof setTimeout> | null = null;
  private requestSeq = 0;
  private readonly variableService = inject(VariableService, { optional: true });
  private readonly localization = inject(LocalizationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly hintText = (key: string): string => this.localization.translateKey(key);

  readonly editorPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.TemplateBuilder.EditorPlaceholder));

  constructor(
    @Optional() @Inject(TEMPLATE_PREVIEW_SERVICE) private readonly preview?: TemplatePreviewService,
  ) {
    effect(() => {
      this.variableService?.variables();
      untracked(() => void this.refreshPreview(this.draft()));
    });

    this.destroyRef.onDestroy(() => {
      if (this.refreshHandle) clearTimeout(this.refreshHandle);
    });
  }

  onDraftChange(value: string): void {
    if (value === this.draft()) return;
    this.draft.set(value);
    this.valueChange.emit(value);
    if (this.refreshHandle) clearTimeout(this.refreshHandle);
    this.refreshHandle = setTimeout(() => this.refreshPreview(value), 200);
  }

  onBrowserInsert(insertion: SnippetInsertion): void {
    this.editor?.insertAtCursor(insertion.text, insertion.caret, {
      bare: insertion.bare,
      wrapped: insertion.wrapped,
      wrappedCaret: insertion.wrappedCaret,
    });
  }

  private async refreshPreview(template: string): Promise<void> {
    if (template === '') {
      this.requestSeq++;
      this.previewText.set('');
      this.previewError.set(null);
      this.typedValue.set(null);
      this.typedError.set(null);
      return;
    }

    if (!this.preview) {
      this.previewText.set(template);
      this.previewError.set(null);
      return;
    }

    const seq = ++this.requestSeq;
    const typed = this.typedOptions;
    try {
      if (typed && this.preview.previewTemplateVariable) {
        const result = await this.preview.previewTemplateVariable(template, this.scope, this.scopeRefId,
          { ...typed, ...this.typedExtras, resultType: typed.resultType });
        if (seq !== this.requestSeq) return;
        this.previewText.set(result.rendered);
        this.previewError.set(null);
        this.typedValue.set(result.error ? null : result.value);
        this.typedError.set(result.error ? templateVariableErrorMessage(this.localization, result.error) : null);
        return;
      }

      const rendered = await this.preview.renderTemplate(template, this.scope, this.scopeRefId);
      if (seq !== this.requestSeq) return;
      this.previewText.set(rendered);
      this.previewError.set(null);
    } catch (err) {
      if (seq !== this.requestSeq) return;
      const code = err instanceof TemplatePreviewError ? err.code : undefined;
      const message = err instanceof Error ? err.message : String(err);
      this.previewError.set({ code, message });
      this.typedValue.set(null);
      this.typedError.set(null);
    }
  }
}
