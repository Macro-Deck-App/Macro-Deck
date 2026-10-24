import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';

import { Text } from '@codemirror/state';
import { EditorView } from '@codemirror/view';

import { CodeEditorComponent } from './code-editor.component';
import { codeLanguageMode } from './code-languages/code-language';

@Component({
  standalone: true,
  imports: [CodeEditorComponent],
  template: `
    <shared-code-editor [variableInsertFormat]="format()">
      <button type="button" class="test-picker" codeEditorVariablePicker></button>
    </shared-code-editor>
  `,
})
class ProjectingHostComponent {
  readonly format = signal<string | undefined>(undefined);
}

async function until(condition: () => boolean, timeoutMs = 5000): Promise<void> {
  const started = Date.now();
  while (!condition()) {
    if (Date.now() - started > timeoutMs) throw new Error('condition not met in time');
    await new Promise(resolve => setTimeout(resolve, 10));
  }
}

describe('codeLanguageMode', () => {
  it('maps the documented language names and falls back to plain text', () => {
    expect(codeLanguageMode('json')).toBe('json');
    expect(codeLanguageMode('Liquid')).toBe('liquid');
    expect(codeLanguageMode('autohotkey')).toBe('autohotkey');
    expect(codeLanguageMode('javascript')).toBe('text');
    expect(codeLanguageMode('lua')).toBe('text');
    expect(codeLanguageMode('text')).toBe('text');
    expect(codeLanguageMode('unknown')).toBe('text');
    expect(codeLanguageMode(undefined)).toBe('text');
  });
});

describe('CodeEditorComponent', () => {
  let fixture: ComponentFixture<CodeEditorComponent>;
  let component: CodeEditorComponent;
  let emitted: string[];

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getVariables', 'getIntegrations', 'onNotification']);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [CodeEditorComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    fixture = TestBed.createComponent(CodeEditorComponent);
    component = fixture.componentInstance;
    emitted = [];
    component.valueChange.subscribe(value => emitted.push(value));
  });

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function textarea(): HTMLTextAreaElement {
    return host().querySelector('.ce-textarea') as HTMLTextAreaElement;
  }

  async function bootEditor(): Promise<EditorView> {
    fixture.detectChanges();
    await until(() => component.ready());
    fixture.detectChanges();
    const content = host().querySelector('.cm-content') as HTMLElement;
    const view = EditorView.findFromDOM(content);
    if (!view) throw new Error('no editor view');
    return view;
  }

  describe('with the editor loaded', () => {
    it('replaces the textarea, shows line numbers and emits typed text', async () => {
      fixture.componentRef.setInput('value', 'MsgBox "a"');
      fixture.componentRef.setInput('language', 'autohotkey');
      const view = await bootEditor();

      expect(textarea().classList).toContain('ce-hidden');
      expect(host().querySelector('.cm-lineNumbers')).not.toBeNull();
      expect(view.state.doc.toString()).toBe('MsgBox "a"');

      view.dispatch({ changes: { from: view.state.doc.length, insert: '\nExitApp' } });

      expect(emitted).toEqual(['MsgBox "a"\nExitApp']);
    });

    it('applies an external value without echoing it and keeps the cursor when the value already matches', async () => {
      fixture.componentRef.setInput('value', 'one');
      const view = await bootEditor();

      fixture.componentRef.setInput('value', 'one two');
      fixture.detectChanges();
      expect(view.state.doc.toString()).toBe('one two');
      expect(emitted).toEqual([]);

      view.dispatch({ selection: { anchor: 2 } });
      fixture.componentRef.setInput('value', 'one two');
      fixture.detectChanges();
      expect(view.state.selection.main.head).toBe(2);
    });

    it('inserts the variable format with the picked name at the cursor', async () => {
      fixture.componentRef.setInput('value', 'x := \nMsgBox x');
      fixture.componentRef.setInput('variableInsertFormat', 'MacroDeck.GetVar("{name}")');
      const view = await bootEditor();
      view.dispatch({ selection: { anchor: 5 } });

      component.insertVariable('obs.scene');

      expect(view.state.doc.toString()).toBe('x := MacroDeck.GetVar("obs.scene")\nMsgBox x');
      expect(emitted).toEqual(['x := MacroDeck.GetVar("obs.scene")\nMsgBox x']);
    });

    it('keeps CRLF line endings of the given text in what it emits after an edit', async () => {
      fixture.componentRef.setInput('value', 'a := 1\r\nb := 2\r\n');
      const view = await bootEditor();

      view.dispatch({ changes: { from: view.state.doc.length, insert: 'c := 3\nd := 4' } });

      expect(emitted).toEqual(['a := 1\r\nb := 2\r\nc := 3\r\nd := 4']);
    });

    it('keeps LF line endings as LF', async () => {
      fixture.componentRef.setInput('value', 'a := 1\nb := 2');
      const view = await bootEditor();

      view.dispatch({ changes: { from: view.state.doc.length, insert: '\nc := 3' } });

      expect(emitted).toEqual(['a := 1\nb := 2\nc := 3']);
    });

    it('keeps CRLF after its own emitted text comes back without any line break', async () => {
      fixture.componentRef.setInput('value', 'a\r\nb');
      const view = await bootEditor();

      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'ab' } });
      fixture.componentRef.setInput('value', 'ab');
      fixture.detectChanges();
      view.dispatch({ changes: { from: 1, insert: '\n' } });

      expect(emitted).toEqual(['ab', 'a\r\nb']);
    });

    it('reports invalid JSON typed into a json editor', async () => {
      fixture.componentRef.setInput('value', '{}');
      fixture.componentRef.setInput('language', 'json');
      const view = await bootEditor();

      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: '{"a":' } });
      fixture.detectChanges();

      expect(component.jsonError()).not.toBeNull();
      expect(host().querySelector('.ce-error')).not.toBeNull();
    });
  });

  describe('before the editor has loaded', () => {
    it('keeps CRLF line endings when the textarea reports its text with LF', async () => {
      fixture.componentRef.setInput('value', 'a\r\nb');
      fixture.detectChanges();
      await fixture.whenStable();
      component.ready.set(false);

      component.onTextareaInput('a\nb\nc');

      expect(emitted).toEqual(['a\r\nb\r\nc']);
    });

    it('edits through the textarea and inserts a variable at its selection', async () => {
      fixture.componentRef.setInput('value', 'AB');
      fixture.componentRef.setInput('variableInsertFormat', '%{name}%');
      fixture.detectChanges();
      await fixture.whenStable();
      component.ready.set(false);
      textarea().setSelectionRange(1, 1);

      component.insertVariable('count');

      expect(component.value).toBe('A%count%B');
      expect(emitted).toEqual(['A%count%B']);
    });
  });

  it('shows the projected variable picker only when an insert format is set', () => {
    const projecting = TestBed.createComponent(ProjectingHostComponent);
    projecting.detectChanges();
    const element = projecting.nativeElement as HTMLElement;
    expect(element.querySelector('.test-picker')).toBeNull();

    projecting.componentInstance.format.set('{name}');
    projecting.detectChanges();
    expect(element.querySelector('.ce-toolbar .test-picker')).not.toBeNull();
  });
});

describe('CodeEditorComponent with a large content document', () => {
  type SliceString = (from: number, to?: number, lineSep?: string) => string;

  function textPrototypes(doc: object): object[] {
    const found = new Set<object>();
    const visit = (node: { children?: readonly object[] | null }): void => {
      found.add(Object.getPrototypeOf(node));
      node.children?.forEach(child => visit(child as { children?: readonly object[] | null }));
    };
    visit(doc as { children?: readonly object[] | null });
    return [...found];
  }

  it('does not serialise the whole document on a keystroke and builds it once for the upload', async () => {
    const lines: string[] = [];
    for (let i = 0; i < 30000; i++) lines.push(`    value${i} := MsgBox("line ${i}") ; comment ${i}`);
    const text = lines.join('\r\n');
    const uploads: string[] = [];
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getUiResourceText']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    apiSpy.getUiResourceText.and.resolveTo(text);
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    TestBed.configureTestingModule({
      imports: [CodeEditorComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    const fixture = TestBed.createComponent(CodeEditorComponent);
    fixture.componentRef.setInput('language', 'autohotkey');
    fixture.componentRef.setInput('contentResource', { resourceId: 'script', contentHash: 'sha256:big' });
    fixture.componentRef.setInput('contentUploader', async (uploaded: string) => {
      uploads.push(uploaded);
      return { contentHash: 'sha256:mine' };
    });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    await until(() => component.ready() && component.contentState() === 'ready', 10000);
    fixture.detectChanges();
    const view = EditorView.findFromDOM(fixture.nativeElement.querySelector('.cm-content') as HTMLElement)!;
    const middle = view.state.doc.line(15000).from;
    view.dispatch({ selection: { anchor: middle }, scrollIntoView: true });

    const longestSlices: number[] = [];
    const restore: (() => void)[] = [];
    for (const proto of textPrototypes(view.state.doc)) {
      const owner = proto as { sliceString: SliceString; toJSON?: () => string[] };
      const sliceString = owner.sliceString;
      owner.sliceString = function (this: unknown, from: number, to?: number, lineSep?: string): string {
        longestSlices.push((to ?? Number.MAX_SAFE_INTEGER) - from);
        return sliceString.call(this, from, to, lineSep);
      };
      restore.push(() => (owner.sliceString = sliceString));
    }

    const toJson = spyOn(Text.prototype, 'toJSON').and.callThrough();
    try {
      view.dispatch(view.state.replaceSelection('x'));

      expect(longestSlices.filter(length => length > 100000)).toEqual([]);
      expect(toJson).not.toHaveBeenCalled();
    } finally {
      restore.forEach(undo => undo());
    }

    component.flushContent();
    await until(() => uploads.length > 0);
    expect(uploads.length).toBe(1);
    expect(uploads[0].length).toBe(text.length + 1);
    expect(uploads[0].includes('\r\nx    value14999')).toBeTrue();
  });

  it('keeps an edit that was not uploaded yet when the provider switches back to an inline value', async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getUiResourceText']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    apiSpy.getUiResourceText.and.resolveTo('first\r\nsecond');
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    TestBed.configureTestingModule({
      imports: [CodeEditorComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    const fixture = TestBed.createComponent(CodeEditorComponent);
    fixture.componentRef.setInput('contentResource', { resourceId: 'small', contentHash: 'sha256:small' });
    fixture.componentRef.setInput('contentUploader', async () => ({ contentHash: 'sha256:mine' }));
    fixture.detectChanges();
    const component = fixture.componentInstance;
    await until(() => component.ready() && component.contentState() === 'ready', 10000);
    fixture.detectChanges();
    const view = EditorView.findFromDOM(fixture.nativeElement.querySelector('.cm-content') as HTMLElement)!;
    view.dispatch({ changes: { from: view.state.doc.length, insert: '!' } });

    fixture.componentRef.setInput('contentResource', null);
    fixture.detectChanges();

    expect(component.contentState()).toBe('inline');
    expect(view.state.doc.toString()).toBe('first\nsecond!');
  });
});
