import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  effect,
  input,
  output,
} from '@angular/core';

import type { Compartment, Extension } from '@codemirror/state';
import type { EditorView as CmEditorView } from '@codemirror/view';

import {
  JsonEditorDeps,
  buildCombinedLintSource,
  buildSchemaCompletionSource,
  buildSchemaHoverSource,
} from './widget-json-editor.schema-support';

@Component({
  selector: 'app-widget-json-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="json-editor-host"></div>`,
  styleUrls: ['./widget-json-editor.component.scss'],
})
export class WidgetJsonEditorComponent implements OnInit, OnDestroy {
  readonly text = input.required<string>();
  readonly schema = input<object | null>(null);

  readonly textChange = output<string>();
  readonly validChange = output<boolean>();
  readonly errorChange = output<string | null>();

  @ViewChild('host', { static: true }) private readonly hostRef!: ElementRef<HTMLDivElement>;

  private view: CmEditorView | null = null;
  private schemaCompartment: Compartment | null = null;
  private buildSchemaExtensions: ((schema: object | null) => Extension[]) | null = null;
  private destroyed = false;

  constructor() {
    effect(() => this.syncText(this.text()));
    effect(() => this.syncSchema(this.schema()));
  }

  ngOnInit(): void {
    void this.bootstrap();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.view?.destroy();
    this.view = null;
  }

  format(): void {
    const view = this.view;
    if (!view) return;

    let pretty: string;
    try {
      pretty = JSON.stringify(JSON.parse(view.state.doc.toString()), null, 2);
    } catch {
      return;
    }

    if (pretty === view.state.doc.toString()) return;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: pretty } });
  }

  private async bootstrap(): Promise<void> {
    const [state, viewMod, language, commands, search, lint, autocomplete, langJson, highlight, schemaLib] =
      await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/view'),
        import('@codemirror/language'),
        import('@codemirror/commands'),
        import('@codemirror/search'),
        import('@codemirror/lint'),
        import('@codemirror/autocomplete'),
        import('@codemirror/lang-json'),
        import('@lezer/highlight'),
        import('json-schema-library'),
      ]);

    if (this.destroyed) return;

    const deps: JsonEditorDeps = { syntaxTree: language.syntaxTree, Draft07: schemaLib.Draft07 };
    const getSchema = (): object | null => this.schema();

    const highlightStyle = language.HighlightStyle.define([
      { tag: highlight.tags.propertyName, color: 'var(--color-accent)' },
      { tag: highlight.tags.string, color: 'var(--type-string-fg)' },
      { tag: highlight.tags.number, color: 'var(--type-numeric-fg)' },
      { tag: highlight.tags.bool, color: 'var(--type-boolean-fg)' },
      { tag: highlight.tags.null, color: 'var(--color-text-muted)' },
      {
        tag: [highlight.tags.separator, highlight.tags.squareBracket, highlight.tags.brace],
        color: 'var(--color-text-secondary)',
      },
    ]);

    const baseTheme = viewMod.EditorView.theme({
      '&': {
        height: '100%',
        backgroundColor: 'var(--color-bg-secondary)',
        color: 'var(--color-text-primary)',
        fontSize: 'var(--text-base)',
      },
      '.cm-content': { fontFamily: 'var(--font-mono)', caretColor: 'var(--color-accent)' },
      '.cm-scroller': { overflow: 'auto' },
      '.cm-gutters': { backgroundColor: 'var(--color-bg-tertiary)', color: 'var(--color-text-muted)', border: 'none' },
      '.cm-activeLine': { backgroundColor: 'var(--color-bg-hover)' },
      '.cm-activeLineGutter': { backgroundColor: 'var(--color-bg-hover)' },
      '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
        backgroundColor: 'var(--color-accent-muted) !important',
      },
      '.cm-matchingBracket, .cm-nonmatchingBracket': {
        outline: '1px solid var(--color-border-accent)',
        backgroundColor: 'transparent',
      },
      '.cm-tooltip': {
        backgroundColor: 'var(--color-bg-elevated)',
        color: 'var(--color-text-primary)',
        border: '1px solid var(--color-border)',
      },
      '.cm-tooltip.cm-tooltip-autocomplete > ul > li[aria-selected]': {
        backgroundColor: 'var(--color-accent-muted)',
        color: 'var(--color-text-primary)',
      },
      '.cm-json-schema-hover': {
        padding: '2px 4px',
        maxWidth: '22rem',
        whiteSpace: 'pre-wrap',
      },
      '.cm-panels': {
        backgroundColor: 'var(--color-bg-elevated)',
        color: 'var(--color-text-primary)',
      },
    });

    const combinedLintSource = buildCombinedLintSource(deps, langJson.jsonParseLinter, getSchema, {
      onResult: (valid, message) => {
        this.validChange.emit(valid);
        this.errorChange.emit(message);
      },
    });

    const buildSchemaExtensions = (schemaValue: object | null): Extension[] => {
      const extensions: Extension[] = [lint.linter(combinedLintSource, { delay: 300 })];
      if (schemaValue) {
        extensions.push(
          langJson.jsonLanguage.data.of({ autocomplete: buildSchemaCompletionSource(deps, getSchema) }),
          viewMod.hoverTooltip(buildSchemaHoverSource(deps, getSchema)),
        );
      }
      return extensions;
    };

    this.schemaCompartment = new state.Compartment();
    this.buildSchemaExtensions = buildSchemaExtensions;

    const extensions: Extension[] = [
      viewMod.lineNumbers(),
      viewMod.highlightActiveLineGutter(),
      langJson.json(),
      language.syntaxHighlighting(highlightStyle),
      language.indentUnit.of('  '),
      language.indentOnInput(),
      language.bracketMatching(),
      autocomplete.closeBrackets(),
      // Deliberately not `indentWithTab` - it traps keyboard focus in the editor. Indent stays on
      // the default Mod-]/Mod-[.
      viewMod.keymap.of([
        ...autocomplete.closeBracketsKeymap,
        ...search.searchKeymap,
        ...commands.historyKeymap,
        ...commands.defaultKeymap,
      ]),
      language.codeFolding(),
      language.foldGutter(),
      viewMod.keymap.of(language.foldKeymap),
      search.search({ top: true }),
      commands.history(),
      autocomplete.autocompletion(),
      lint.lintGutter(),
      baseTheme,
      viewMod.EditorView.updateListener.of(update => {
        if (update.docChanged) {
          this.textChange.emit(update.state.doc.toString());
        }
      }),
      this.schemaCompartment.of(buildSchemaExtensions(this.schema())),
    ];

    const editorState = state.EditorState.create({ doc: this.text(), extensions });
    this.view = new viewMod.EditorView({ state: editorState, parent: this.hostRef.nativeElement });
  }

  private syncText(value: string): void {
    const view = this.view;
    if (!view || view.state.doc.toString() === value) return;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } });
  }

  private syncSchema(schemaValue: object | null): void {
    const view = this.view;
    if (!view || !this.schemaCompartment || !this.buildSchemaExtensions) return;
    view.dispatch({ effects: this.schemaCompartment.reconfigure(this.buildSchemaExtensions(schemaValue)) });
  }
}
