import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import type { UiResource } from '@macro-deck/runtime';
import { ApiService, LocalizationService, TranslatePipe } from '@shared';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';

import type { Compartment, Extension, Text as CmText } from '@codemirror/state';
import type { EditorView as CmEditorView } from '@codemirror/view';

import {
  CodeLanguageModules,
  codeLanguageExtension,
  codeLanguageMode,
  createCodeHighlightStyle,
} from './code-languages/code-language';

const VariableNamePlaceholder = '{name}';

const ContentUploadDebounceMs = 750;

export type CodeEditorContentUploader = (text: string) => Promise<{ contentHash: string } | null>;

type ContentState = 'inline' | 'loading' | 'ready' | 'failed';

const LineBreak = /\r\n|\r|\n/;
const LineBreaks = /\r\n|\r|\n/g;

export function detectLineSeparator(text: string): string {
  return LineBreak.exec(text)?.[0] ?? '\n';
}

@Component({
  selector: 'shared-code-editor',
  standalone: true,
  imports: [FormsModule, LoadingStateComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ce-root" (focusout)="flushContent()">
      <div class="ce-toolbar">
        <span class="ce-language">{{ language }}</span>
        <div class="ce-toolbar-end">
          @if (jsonError(); as error) {
            <span class="ce-error">{{ error }}</span>
          }
          @if (contentState() === 'failed') {
            <span class="ce-error" role="alert">{{ 'macrodeck.app:Forms.CodeEditor.LoadFailed' | translate }}</span>
          } @else if (uploadFailed()) {
            <span class="ce-error" role="alert">{{ 'macrodeck.app:Forms.CodeEditor.UploadFailed' | translate }}</span>
          }
          @if (variableInsertFormat) {
            <ng-content select="[codeEditorVariablePicker]" />
          }
        </div>
      </div>
      @if (contentState() === 'loading') {
        <shared-loading-state class="ce-loading" [style.--ce-rows]="rows" />
      }
      <textarea
        #textareaEl
        class="ce-textarea"
        [class.ce-hidden]="ready() || contentState() === 'loading'"
        [class.ce-invalid]="!!jsonError()"
        [rows]="rows"
        spellcheck="false"
        [attr.aria-label]="ariaLabel"
        [placeholder]="placeholder"
        [readOnly]="locked()"
        [ngModel]="ready() ? '' : value"
        (ngModelChange)="onTextareaInput($event)"></textarea>
      <div
        #cmHost
        class="ce-cm-host"
        [class.ce-hidden]="!ready() || contentState() === 'loading'"
        [class.ce-invalid]="!!jsonError()"
        [style.--ce-rows]="rows"></div>
    </div>
  `,
  styleUrls: ['./code-editor.component.scss'],
})
export class CodeEditorComponent implements OnInit, OnChanges, OnDestroy {
  private readonly localization = inject(LocalizationService);
  private readonly api = inject(ApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  @Input() value = '';
  @Input() language = 'text';
  static readonly defaultRows = 8;

  @Input() rows = CodeEditorComponent.defaultRows;
  @Input() placeholder = '';
  @Input() ariaLabel: string | null = null;
  @Input() variableInsertFormat: string | null | undefined;
  @Input() contentResource: UiResource | null | undefined;
  @Input() contentUploader: CodeEditorContentUploader | null | undefined;

  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('textareaEl') private readonly textareaRef?: ElementRef<HTMLTextAreaElement>;
  @ViewChild('cmHost') private readonly cmHostRef?: ElementRef<HTMLDivElement>;

  readonly ready = signal(false);
  readonly jsonError = signal<string | null>(null);
  readonly contentState = signal<ContentState>('inline');
  readonly uploadFailed = signal(false);
  protected readonly locked = computed(() => this.contentState() === 'loading' || this.contentState() === 'failed');

  private view: CmEditorView | null = null;
  private languageCompartment: Compartment | null = null;
  private editableCompartment: Compartment | null = null;
  private editableExtension: ((locked: boolean) => Extension) | null = null;
  private languageModules: CodeLanguageModules | null = null;
  private applyingExternalValue = false;
  private destroyed = false;
  private loadedResourceKey: string | null = null;
  private loadSequence = 0;
  private uploadSequence = 0;
  private lastUploadHash: string | null = null;
  private contentDirty = false;
  private uploadTimer: ReturnType<typeof setTimeout> | null = null;
  private lineSeparator = '\n';
  private heldText: string | null = null;
  private viewTextStale = false;

  ngOnInit(): void {
    void this.bootstrap();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['contentResource']) {
      this.onContentResource(this.contentResource ?? null);
    }
    if (changes['value'] && this.contentState() === 'inline' && this.value !== this.heldText) {
      this.lineSeparator = detectLineSeparator(this.value);
      this.heldText = this.value;
      this.syncViewText(this.value);
    }
    if (changes['language'] && this.view) {
      this.applyLanguage();
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.clearUploadTimer();
    this.view?.destroy();
    this.view = null;
  }

  onTextareaInput(value: string): void {
    // A textarea reports its text with LF line breaks only, whatever it was given.
    this.onInput(value.replace(LineBreaks, this.lineSeparator));
  }

  onInput(value: string): void {
    this.value = value;
    this.heldText = value;
    this.validate(value);
    if (this.contentState() !== 'inline') {
      this.contentDirty = true;
      this.scheduleUpload();
      return;
    }
    this.valueChange.emit(value);
  }

  flushContent(): void {
    if (this.contentState() !== 'ready' || !this.contentDirty) return;
    this.clearUploadTimer();
    this.contentDirty = false;

    const text = this.currentText();
    this.validate(text);
    const sequence = ++this.uploadSequence;
    const upload = this.contentUploader ? this.contentUploader(text) : Promise.resolve(null);
    void upload.catch(() => null).then(result => {
      if (this.destroyed || sequence !== this.uploadSequence) return;
      this.uploadFailed.set(!result);
      if (result) this.lastUploadHash = result.contentHash;
    });
  }

  insertVariable(name: string): void {
    if (!this.variableInsertFormat) {
      return;
    }
    this.insertAtCursor(this.variableInsertFormat.split(VariableNamePlaceholder).join(name));
  }

  insertAtCursor(text: string): void {
    if (this.ready() && this.view) {
      const selection = this.view.state.selection.main;
      this.view.dispatch({
        changes: { from: selection.from, to: selection.to, insert: text },
        selection: { anchor: selection.from + text.length },
      });
      this.view.focus();
      return;
    }

    const textarea = this.textareaRef?.nativeElement;
    const current = textarea ? textarea.value : this.value;
    const start = textarea?.selectionStart ?? current.length;
    const end = textarea?.selectionEnd ?? current.length;
    const pos = start + text.length;
    this.onTextareaInput(current.slice(0, start) + text + current.slice(end));
    if (textarea) {
      // A timer, not a microtask: the textarea receives the new text only in the change detection
      // the emit above schedules, and a caret set before that would be clamped away.
      setTimeout(() => {
        textarea.focus();
        textarea.setSelectionRange(pos, pos);
      });
    }
  }

  private async bootstrap(): Promise<void> {
    try {
      const [state, viewMod, language, commands, search, highlight, langJson] = await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/view'),
        import('@codemirror/language'),
        import('@codemirror/commands'),
        import('@codemirror/search'),
        import('@lezer/highlight'),
        import('@codemirror/lang-json'),
      ]);

      if (this.destroyed) return;
      const host = this.cmHostRef?.nativeElement;
      if (!host) return;

      this.languageModules = { language, langJson };
      this.languageCompartment = new state.Compartment();
      this.editableCompartment = new state.Compartment();
      this.editableExtension = locked => [
        state.EditorState.readOnly.of(locked),
        viewMod.EditorView.editable.of(!locked),
      ];

      // CodeMirror's light base theme scopes its colours with two classes, so these rules carry more to win.
      const theme = viewMod.EditorView.theme({
        '&': {
          height: '100%',
          backgroundColor: 'var(--color-bg-primary)',
          color: 'var(--color-text-primary)',
          fontSize: 'var(--text-base)',
        },
        '&.cm-focused': { outline: 'none' },
        '.cm-content': { fontFamily: 'var(--font-mono)', caretColor: 'var(--color-accent)' },
        '&.cm-editor .cm-cursor, &.cm-editor .cm-dropCursor': { borderLeftColor: 'var(--color-accent)' },
        '.cm-placeholder': { color: 'var(--color-text-muted)' },
        '.cm-scroller': { overflow: 'auto', lineHeight: 'var(--leading-normal)' },
        '&.cm-editor .cm-scroller .cm-gutters': {
          backgroundColor: 'var(--color-bg-tertiary)',
          color: 'var(--color-text-muted)',
          border: 'none',
        },
        '&.cm-editor .cm-content .cm-activeLine': { backgroundColor: 'var(--color-bg-hover)' },
        '&.cm-editor .cm-gutters .cm-activeLineGutter': {
          backgroundColor: 'var(--color-bg-hover)',
          color: 'var(--color-text-secondary)',
        },
        '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
          backgroundColor: 'var(--color-accent-muted) !important',
        },
        '&.cm-editor .cm-content .cm-matchingBracket, &.cm-editor .cm-content .cm-nonmatchingBracket': {
          outline: '1px solid var(--color-border-accent)',
          backgroundColor: 'transparent',
        },
        '&.cm-editor .cm-panels.cm-panels-top': {
          backgroundColor: 'var(--color-bg-elevated)',
          color: 'var(--color-text-primary)',
          borderBottom: '1px solid var(--color-border)',
        },
        '&.cm-editor .cm-panels.cm-panels-bottom': {
          backgroundColor: 'var(--color-bg-elevated)',
          color: 'var(--color-text-primary)',
          borderTop: '1px solid var(--color-border)',
        },
        '&.cm-editor .cm-panels .cm-textfield': {
          backgroundColor: 'var(--color-bg-primary)',
          color: 'var(--color-text-primary)',
          border: '1px solid var(--color-border)',
          borderRadius: 'var(--radius-sm)',
        },
        '&.cm-editor .cm-panels .cm-textfield:focus': { outline: 'none', borderColor: 'var(--color-accent)' },
        '&.cm-editor .cm-panels .cm-button': {
          backgroundColor: 'var(--color-bg-tertiary)',
          backgroundImage: 'none',
          color: 'var(--color-text-primary)',
          border: '1px solid var(--color-border)',
          borderRadius: 'var(--radius-sm)',
        },
        '&.cm-editor .cm-panels .cm-button:hover, &.cm-editor .cm-panels .cm-button:active': {
          backgroundColor: 'var(--color-bg-hover)',
          backgroundImage: 'none',
        },
        '&.cm-editor .cm-panels .cm-search label': { color: 'var(--color-text-secondary)' },
        '&.cm-editor .cm-panels .cm-search [name=close]': { color: 'var(--color-text-muted)' },
        '&.cm-editor .cm-content .cm-searchMatch': {
          backgroundColor: 'var(--color-warning-muted)',
          outline: '1px solid var(--color-warning)',
        },
        '&.cm-editor .cm-content .cm-searchMatch.cm-searchMatch-selected': {
          backgroundColor: 'var(--color-accent-muted)',
        },
      });

      const extensions: Extension[] = [
        viewMod.lineNumbers(),
        viewMod.highlightActiveLineGutter(),
        viewMod.highlightActiveLine(),
        viewMod.drawSelection(),
        this.languageCompartment.of(this.languageExtension()),
        this.editableCompartment.of(this.editableExtension(this.locked())),
        language.syntaxHighlighting(createCodeHighlightStyle(language, highlight)),
        language.indentUnit.of('  '),
        language.indentOnInput(),
        language.bracketMatching(),
        commands.history(),
        search.search({ top: true }),
        // No indentWithTab: it would trap keyboard focus inside the editor.
        viewMod.keymap.of([...search.searchKeymap, ...commands.historyKeymap, ...commands.defaultKeymap]),
        viewMod.placeholder(this.placeholder),
        viewMod.EditorView.contentAttributes.of(this.ariaLabel ? { 'aria-label': this.ariaLabel } : {}),
        theme,
        viewMod.EditorView.updateListener.of(update => {
          if (update.docChanged && !this.applyingExternalValue) {
            this.onEditorChange(update.state.doc);
          }
        }),
      ];

      const editorState = state.EditorState.create({ doc: this.value, extensions });
      this.view = new viewMod.EditorView({ state: editorState, parent: host });
      this.ready.set(true);
    } catch {
      // The editor chunk failed to load: ready never flips and the textarea stays the live editor.
    }
  }

  private onEditorChange(doc: CmText): void {
    // A content document can be a megabyte: keystrokes only mark it, and the text is built once per upload.
    if (this.contentState() !== 'inline') {
      this.viewTextStale = true;
      this.contentDirty = true;
      this.scheduleUpload();
      return;
    }
    this.onInput(doc.sliceString(0, doc.length, this.lineSeparator));
  }

  private currentText(): string {
    if (this.viewTextStale && this.view) {
      const doc = this.view.state.doc;
      this.value = doc.sliceString(0, doc.length, this.lineSeparator);
      this.heldText = this.value;
    }
    this.viewTextStale = false;
    return this.value;
  }

  private onContentResource(resource: UiResource | null): void {
    if (!resource) {
      if (this.contentState() !== 'inline') this.leaveContentMode();
      return;
    }

    const key = `${resource.resourceId}|${resource.contentHash ?? ''}`;
    if (key === this.loadedResourceKey) return;
    // The provider registered exactly what this editor uploaded last: keep its text, cursor and undo history.
    if (this.contentState() === 'ready' && resource.contentHash && resource.contentHash === this.lastUploadHash) {
      this.loadedResourceKey = key;
      return;
    }
    void this.loadContent(resource, key);
  }

  private async loadContent(resource: UiResource, key: string): Promise<void> {
    const sequence = ++this.loadSequence;
    this.clearUploadTimer();
    this.contentDirty = false;
    this.uploadFailed.set(false);
    this.setContentState('loading');

    let text: string;
    try {
      text = await this.api.getUiResourceText(resource);
    } catch {
      if (!this.destroyed && sequence === this.loadSequence) this.setContentState('failed');
      return;
    }
    if (this.destroyed || sequence !== this.loadSequence) return;

    this.loadedResourceKey = key;
    this.viewTextStale = false;
    this.lineSeparator = detectLineSeparator(text);
    this.value = text;
    this.heldText = text;
    this.validate(text);
    this.syncViewText(text);
    this.setContentState('ready');
  }

  private leaveContentMode(): void {
    this.currentText();
    this.loadSequence++;
    this.clearUploadTimer();
    this.contentDirty = false;
    this.loadedResourceKey = null;
    this.lastUploadHash = null;
    this.uploadFailed.set(false);
    this.setContentState('inline');
    this.syncViewText(this.value);
  }

  private setContentState(state: ContentState): void {
    this.contentState.set(state);
    if (this.view && this.editableCompartment && this.editableExtension) {
      this.view.dispatch({ effects: this.editableCompartment.reconfigure(this.editableExtension(this.locked())) });
    }
    this.changeDetector.markForCheck();
  }

  private scheduleUpload(): void {
    this.clearUploadTimer();
    this.uploadTimer = setTimeout(() => this.flushContent(), ContentUploadDebounceMs);
  }

  private clearUploadTimer(): void {
    if (this.uploadTimer === null) return;
    clearTimeout(this.uploadTimer);
    this.uploadTimer = null;
  }

  private languageExtension(): Extension {
    return this.languageModules ? codeLanguageExtension(codeLanguageMode(this.language), this.languageModules) : [];
  }

  private applyLanguage(): void {
    if (!this.view || !this.languageCompartment) return;
    this.view.dispatch({ effects: this.languageCompartment.reconfigure(this.languageExtension()) });
    this.validate(this.value);
  }

  private syncViewText(value: string): void {
    const view = this.view;
    if (!view || view.state.doc.toString() === value.replace(LineBreaks, '\n')) return;
    const head = Math.min(view.state.selection.main.head, value.length);
    this.applyingExternalValue = true;
    try {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value }, selection: { anchor: head } });
    } finally {
      this.applyingExternalValue = false;
    }
  }

  private validate(value: string): void {
    if (codeLanguageMode(this.language) !== 'json' || value.trim() === '') {
      this.jsonError.set(null);
      return;
    }

    try {
      JSON.parse(value);
      this.jsonError.set(null);
    } catch (error) {
      this.jsonError.set(error instanceof SyntaxError
        ? error.message
        : this.localization.translateKey(AppStrings.Forms.CodeEditor.InvalidJson));
    }
  }
}
