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
import type { Variable } from '@macro-deck/runtime';

import type { Completion, CompletionContext, CompletionResult } from '@codemirror/autocomplete';

import type { EditorView as CmEditorView } from '@codemirror/view';
import type { Extension } from '@codemirror/state';

import { isInsideLiquidTag } from './liquid-context.util';
import { LiquidCompletionItem, liquidCompletions } from './liquid-completion';

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
  @Input() variables: readonly Variable[] = [];
  @Input() hintText: (key: string) => string = key => key;

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
      const [state, viewMod, language, commands, highlight, autocomplete] = await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/view'),
        import('@codemirror/language'),
        import('@codemirror/commands'),
        import('@lezer/highlight'),
        import('@codemirror/autocomplete'),
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
        '.cm-tooltip': {
          backgroundColor: 'var(--color-bg-elevated)',
          color: 'var(--color-text-primary)',
          border: '1px solid var(--color-border-overlay)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-popover)',
        },
        '.cm-tooltip.cm-tooltip-autocomplete > ul': {
          fontFamily: 'inherit',
          minWidth: '240px',
          maxWidth: 'min(22.75rem, 80vw)',
          maxHeight: '220px',
          padding: 'var(--space-1)',
        },
        '.cm-tooltip.cm-tooltip-autocomplete > ul > li': {
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          gap: 'var(--space-3)',
          padding: '0.3125rem 0.5rem',
          borderRadius: 'var(--radius-sm)',
          color: 'var(--color-text-primary)',
          fontSize: 'var(--text-sm)',
          lineHeight: 'normal',
        },
        '.cm-tooltip-autocomplete > ul > li:hover, .cm-tooltip-autocomplete > ul > li[aria-selected]': {
          backgroundColor: 'var(--color-bg-hover)',
          color: 'var(--color-text-primary)',
        },
        '.cm-completionLabel': {
          flex: '1 1 auto',
          minWidth: '0',
          fontFamily: 'var(--font-mono)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        },
        '.cm-completionMatchedText': {
          textDecoration: 'none',
        },
        '.cm-completionDetail': {
          flex: '0 1 auto',
          maxWidth: '40%',
          marginLeft: '0',
          fontStyle: 'normal',
          color: 'var(--color-text-muted)',
          fontSize: 'var(--text-xs)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        },
        '.cm-tooltip.cm-completionInfo': {
          maxWidth: '22rem',
          padding: 'var(--space-2)',
          fontSize: 'var(--text-sm)',
          whiteSpace: 'pre-wrap',
        },
      });

      const extensions: Extension[] = [
        liquidLanguage,
        language.syntaxHighlighting(highlightStyle),
        autocomplete.autocompletion({ override: [context => this.complete(context)], icons: false }),
        // The editor lives in a modal that closes on any Escape reaching the document, so dismissing
        // the suggestion list has to stop the key here.
        state.Prec.highest(viewMod.keymap.of([
          { key: 'Escape', run: autocomplete.closeCompletion, stopPropagation: true },
          { key: 'Tab', run: autocomplete.acceptCompletion },
        ])),
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

  complete(context: Pick<CompletionContext, 'state' | 'pos' | 'explicit'>): CompletionResult | null {
    const found = liquidCompletions(context.state.doc.toString(), context.pos, this.variables);
    if (!found || found.items.length === 0) return null;
    return {
      from: found.from,
      to: found.to,
      filter: false,
      options: found.items.map(item => this.toCompletion(item)),
    };
  }

  private toCompletion(item: LiquidCompletionItem): Completion {
    return {
      label: item.label,
      detail: item.detail,
      info: item.hintKey ? this.hintText(item.hintKey) : undefined,
      type: item.kind === 'variable' ? 'variable' : item.kind === 'filter' ? 'function' : 'keyword',
      apply: (view, _completion, from, to) => view.dispatch({
        changes: { from, to, insert: item.insert },
        selection: { anchor: from + (item.caret ?? item.insert.length) },
      }),
    };
  }

  private syncViewText(value: string): void {
    const view = this.view;
    if (!view || view.state.doc.toString() === value) return;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } });
  }
}
