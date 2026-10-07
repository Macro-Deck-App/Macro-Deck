import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';

import { LiquidEditorComponent } from './liquid-editor.component';

// CodeMirror never boots under Karma (jsdom-less ChromeHeadless can load the chunk, but nothing in
// this suite waits for the async `bootstrap()` to resolve) - `ready()` stays false throughout, so
// every case below exercises the textarea fallback path deliberately.
describe('LiquidEditorComponent (textarea fallback)', () => {
  let fixture: ComponentFixture<LiquidEditorComponent>;
  let component: LiquidEditorComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LiquidEditorComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(LiquidEditorComponent);
    component = fixture.componentInstance;
  });

  function textarea(): HTMLTextAreaElement {
    return fixture.nativeElement.querySelector('.le-textarea');
  }

  it('splices at the selection, not the end, and emits valueChange', async () => {
    fixture.componentRef.setInput('value', 'A B');
    fixture.detectChanges();
    await fixture.whenStable();

    const el = textarea();
    el.setSelectionRange(2, 2);

    const emitted: string[] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    component.insertAtCursor('{{ vars.greeting }}');

    expect(component.value).toBe('A {{ vars.greeting }}B');
    expect(emitted).toEqual(['A {{ vars.greeting }}B']);
  });

  it('inserts the bare reference when the caret is already inside a Liquid tag', async () => {
    // Building a condition: the braces are already open, so the full {{ ... }} form would nest into
    // `{% if {{ vars.x }} %}`, which does not parse.
    fixture.componentRef.setInput('value', '{% if  %}');
    fixture.detectChanges();
    await fixture.whenStable();

    textarea().setSelectionRange('{% if '.length, '{% if '.length);
    component.insertAtCursor('{{ vars.cpu }}', undefined, { bare: 'vars.cpu' });

    expect(component.value).toBe('{% if vars.cpu %}');
  });

  it('inserts the full reference when the caret is in plain text', async () => {
    fixture.componentRef.setInput('value', 'Load: ');
    fixture.detectChanges();
    await fixture.whenStable();

    textarea().setSelectionRange(6, 6);
    component.insertAtCursor('{{ vars.cpu }}', undefined, { bare: 'vars.cpu' });

    expect(component.value).toBe('Load: {{ vars.cpu }}');
  });

  it('replaces an active selection rather than inserting inside it', async () => {
    fixture.componentRef.setInput('value', 'A SELECTED B');
    fixture.detectChanges();
    await fixture.whenStable();

    const el = textarea();
    el.setSelectionRange(2, 10);

    component.insertAtCursor('X');

    expect(component.value).toBe('A X B');
  });

  it('called before anything is mounted, appends rather than throwing', () => {
    const freshFixture = TestBed.createComponent(LiquidEditorComponent);
    const freshComponent = freshFixture.componentInstance;
    freshComponent.value = 'A';

    expect(() => freshComponent.insertAtCursor('B')).not.toThrow();
    expect(freshComponent.value).toBe('AB');
  });

  it('caret positions the selection inside the inserted text', async () => {
    fixture.componentRef.setInput('value', '');
    fixture.detectChanges();
    await fixture.whenStable();

    const el = textarea();
    el.setSelectionRange(0, 0);

    component.insertAtCursor('{% if  %}', 6);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));

    expect(component.value).toBe('{% if  %}');
    // Inside the tag, ready for the condition - not parked at the end of the snippet.
    expect(el.selectionStart).toBe(6);
    expect(el.selectionEnd).toBe(6);
  });

  it('two consecutive inserts concatenate rather than nest', async () => {
    fixture.componentRef.setInput('value', '');
    fixture.detectChanges();
    await fixture.whenStable();

    let el = textarea();
    el.setSelectionRange(0, 0);
    component.insertAtCursor('{{ vars.a }}');

    fixture.detectChanges();
    await fixture.whenStable();
    el = textarea();
    el.setSelectionRange(component.value.length, component.value.length);
    component.insertAtCursor('{{ vars.b }}');

    expect(component.value).toBe('{{ vars.a }}{{ vars.b }}');
  });

  it('completes a variable with its localized hint text and places the caret after the insertion', () => {
    component.variables = [
      { id: 'cpu', name: 'cpu', scope: 'global', type: 'numeric', classification: 'user', value: '12' },
    ];
    component.hintText = key => `hint:${key}`;
    const state = EditorState.create({ doc: '{{ vars.c' });

    const result = component.complete({ state, pos: state.doc.length, explicit: false })!;
    const view = new EditorView({ state });
    const option = result.options[0];
    (option.apply as (v: EditorView, c: typeof option, from: number, to: number) => void)(
      view, option, result.from, result.to ?? state.doc.length);

    expect(option.label).toBe('vars.cpu');
    expect(option.detail).toBe('12');
    expect(view.state.doc.toString()).toBe('{{ vars.cpu }}');
    expect(view.state.selection.main.head).toBe('{{ vars.cpu }}'.length);
    view.destroy();
  });

  it('describes a filter with the resolved hint text', () => {
    component.hintText = key => `hint:${key}`;
    const state = EditorState.create({ doc: '{{ vars.cpu | upc' });

    const result = component.complete({ state, pos: state.doc.length, explicit: false })!;

    expect(result.options.map(option => option.label)).toEqual(['upcase']);
    expect(String(result.options[0].info)).toContain('hint:');
  });

  it('dismisses the suggestion list on Escape without letting the key reach the surrounding modal', async () => {
    document.body.appendChild(fixture.nativeElement);
    component.variables = [
      { id: 'cpu', name: 'cpu', scope: 'global', type: 'numeric', classification: 'user', value: '12' },
    ];
    fixture.componentRef.setInput('value', '{{ vars.c');
    fixture.detectChanges();
    for (let attempt = 0; attempt < 50 && !component.ready(); attempt++) {
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    const view = (component as unknown as { view: EditorView }).view;
    const { startCompletion, completionStatus } = await import('@codemirror/autocomplete');
    view.dispatch({ selection: { anchor: view.state.doc.length } });
    view.focus();
    startCompletion(view);
    for (let attempt = 0; attempt < 50 && completionStatus(view.state) !== 'active'; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    let reachedDocument = 0;
    const listener = (event: KeyboardEvent) => { if (event.key === 'Escape') reachedDocument++; };
    document.addEventListener('keydown', listener);

    try {
      view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    } finally {
      document.removeEventListener('keydown', listener);
      fixture.nativeElement.remove();
    }

    expect(component.ready()).toBeTrue();
    expect(completionStatus(view.state)).toBeNull();
    expect(reachedDocument).toBe(0);
  });

  it('accepts the selected suggestion with Tab', async () => {
    document.body.appendChild(fixture.nativeElement);
    component.variables = [
      { id: 'cpu', name: 'cpu', scope: 'global', type: 'numeric', classification: 'user', value: '12' },
    ];
    fixture.componentRef.setInput('value', '{{ vars.c');
    fixture.detectChanges();
    for (let attempt = 0; attempt < 50 && !component.ready(); attempt++) {
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    const view = (component as unknown as { view: EditorView }).view;
    const { startCompletion, completionStatus } = await import('@codemirror/autocomplete');
    view.dispatch({ selection: { anchor: view.state.doc.length } });
    view.focus();
    startCompletion(view);
    for (let attempt = 0; attempt < 50 && completionStatus(view.state) !== 'active'; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    // acceptCompletion ignores a list opened less than 75 ms ago.
    await new Promise(resolve => setTimeout(resolve, 100));

    try {
      view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    } finally {
      fixture.nativeElement.remove();
    }

    expect(view.state.doc.toString()).toBe('{{ vars.cpu }}');
  });
});
