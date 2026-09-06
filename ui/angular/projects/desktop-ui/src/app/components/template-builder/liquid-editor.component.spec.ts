import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

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
});
