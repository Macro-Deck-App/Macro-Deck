import {
  ChangeDetectionStrategy,
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
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import type { EditorView as CmEditorView } from '@codemirror/view';
import type { Extension } from '@codemirror/state';

import { isInsideLiquidTag } from './liquid-context.util';

export interface InsertForms {
  bare?: string;
  wrapped?: string;
  wrappedCaret?: number;
}
import { createLiquidHighlightStyle, createLiquidLanguage } from './liquid-language';

@Component({
  selector: 'shared-liquid-editor',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <textarea
      #textareaEl
      class="le-textarea"
      [class.le-hidden]="ready()"
      [ngModel]="value"
      (ngModelChange)="onTextareaChange($event)"
      [placeholder]="placeholder"
      spellcheck="false"></textarea>
    <div #cmHost class="le-cm-host" [class.le-hidden]="!ready()"></div>
  `,
  styleUrls: ['./liquid-editor.component.scss'],
})
export class LiquidEditorComponent implements OnInit, OnChanges, OnDestroy {
  @Input() value = '';
  @Input() placeholder = '';

  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('textareaEl') private readonly textareaRef?: ElementRef<HTMLTextAreaElement>;
  @ViewChild('cmHost') private readonly cmHostRef?: ElementRef<HTMLDivElement>;

  readonly ready = signal(false);

  private view: CmEditorView | null = null;
  private destroyed = false;

  ngOnInit(): void {
    void this.bootstrap();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['value'] && this.view) {
      this.syncViewText(this.value);
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.view?.destroy();
    this.view = null;
  }

  onTextareaChange(next: string): void {
    this.value = next;
    this.valueChange.emit(next);
  }

  focus(): void {
    if (this.ready() && this.view) {
      this.view.focus();
      return;
    }
    this.textareaRef?.nativeElement.focus();
  }

  insertAtCursor(text: string, caret?: number, forms?: InsertForms): void {
    if (this.ready() && this.view) {
      const selection = this.view.state.selection.main;
      const chosen = this.formOf(text, caret, forms, this.view.state.doc.toString(), selection.from);
      const insert = chosen.text;
      const anchor = selection.from + (chosen.caret ?? insert.length);
      this.view.dispatch({
        changes: { from: selection.from, to: selection.to, insert },
        selection: { anchor },
      });
      // dispatch runs the updateListener synchronously, and that is what emits valueChange.
      this.view.focus();
      return;
    }

    const textarea = this.textareaRef?.nativeElement;
    if (!textarea) {
      const appended = this.formOf(text, caret, forms, this.value, this.value.length);
      const next = this.value + appended.text;
      this.value = next;
      this.valueChange.emit(next);
      return;
    }

    const start = textarea.selectionStart ?? this.value.length;
    const end = textarea.selectionEnd ?? this.value.length;
    const chosen = this.formOf(text, caret, forms, this.value, start);
    const insert = chosen.text;
    const next = this.value.slice(0, start) + insert + this.value.slice(end);
    const pos = start + (chosen.caret ?? insert.length);
    this.value = next;
    this.valueChange.emit(next);
    // A timer, not a microtask: the change detection that writes `next` into the textarea is itself
    // scheduled by the `valueChange` emit above, so a microtask would set the selection while the
    // element still holds the pre-insert text and the caret would be clamped away.
    setTimeout(() => {
      textarea.focus();
      textarea.setSelectionRange(pos, pos);
    });
  }

  private formOf(
    text: string,
    caret: number | undefined,
    forms: InsertForms | undefined,
    document: string,
    at: number,
  ): { text: string; caret?: number } {
    if (isInsideLiquidTag(document, at)) {
      return forms?.bare !== undefined ? { text: forms.bare } : { text, caret };
    }
    return forms?.wrapped !== undefined ? { text: forms.wrapped, caret: forms.wrappedCaret } : { text, caret };
  }

  private async bootstrap(): Promise<void> {
    try {
      const [state, viewMod, language, commands, highlight] = await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/view'),
        import('@codemirror/language'),
        import('@codemirror/commands'),
        import('@lezer/highlight'),
      ]);

      if (this.destroyed) return;
      const host = this.cmHostRef?.nativeElement;
      if (!host) return;

      const liquidLanguage = createLiquidLanguage(language);
      const highlightStyle = createLiquidHighlightStyle(language, highlight);

      const theme = viewMod.EditorView.theme({
        '&': {
          height: '100%',
          backgroundColor: 'var(--color-bg-primary)',
          color: 'var(--color-text-primary)',
          fontSize: 'var(--text-base)',
        },
        '.cm-content': {
          fontFamily: 'var(--font-mono)',
          caretColor: 'var(--color-accent)',
          padding: 'var(--space-2)',
        },
        '.cm-scroller': { overflow: 'auto' },
        '.cm-activeLine': { backgroundColor: 'var(--color-bg-hover)' },
        '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
          backgroundColor: 'var(--color-accent-muted) !important',
        },
      });

      const extensions: Extension[] = [
        liquidLanguage,
        language.syntaxHighlighting(highlightStyle),
        commands.history(),
        viewMod.keymap.of([...commands.historyKeymap, ...commands.defaultKeymap]),
        viewMod.EditorView.lineWrapping,
        theme,
        viewMod.EditorView.updateListener.of(update => {
          if (update.docChanged) {
            this.value = update.state.doc.toString();
            this.valueChange.emit(this.value);
          }
        }),
      ];

      const editorState = state.EditorState.create({ doc: this.value, extensions });
      this.view = new viewMod.EditorView({ state: editorState, parent: host });
      this.ready.set(true);
    } catch {
      // Chunk failed to load (offline, blocked, ...): `ready` never flips and the textarea stays
      // the live editor - see the class doc.
    }
  }

  private syncViewText(value: string): void {
    const view = this.view;
    if (!view || view.state.doc.toString() === value) return;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } });
  }
}
