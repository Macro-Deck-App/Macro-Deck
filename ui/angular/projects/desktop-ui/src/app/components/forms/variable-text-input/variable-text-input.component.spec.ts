import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import type { Variable } from '@macro-deck/runtime';
import { VariableTextInputComponent } from './variable-text-input.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function variable(name: string, extra: Partial<Variable> = {}): Variable {
  return {
    id: `id-${name}`,
    name,
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: `${name}-value`,
    ...extra,
  };
}

const VARIABLES: Variable[] = [
  variable('volume', { value: '42', type: 'numeric' }),
  variable('volatile'),
  variable('title'),
  variable('name', { origin: 'event' }),
];

describe('VariableTextInputComponent', () => {
  let fixture: ComponentFixture<VariableTextInputComponent>;
  let component: VariableTextInputComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [VariableTextInputComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(VariableTextInputComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variables', VARIABLES);
    fixture.detectChanges();
  });

  function editor(): HTMLElement {
    return fixture.nativeElement.querySelector('.vti-editor');
  }

  function chips(): HTMLElement[] {
    return Array.from(editor().querySelectorAll<HTMLElement>('.vti-chip'));
  }

  function nodeShapes(): string[] {
    return Array.from(editor().childNodes).map(node => {
      if (node.nodeType === Node.TEXT_NODE) {
        return (node as Text).data === '\u200B' ? 'anchor' : 'text';
      }
      const element = node as HTMLElement;
      return element.dataset['token'] === undefined ? element.nodeName.toLowerCase() : 'chip';
    });
  }

  function setValue(value: string): void {
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
  }

  function placeCaret(node: Node, offset: number): void {
    const range = document.createRange();
    range.setStart(node, offset);
    range.collapse(true);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
  }

  function caretAtEndOfText(): void {
    const text = editor().firstChild as Text;
    placeCaret(text, text.data.length);
  }

  function caretOffset(): number {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return -1;
    const range = selection.getRangeAt(0);
    const measured = document.createRange();
    measured.selectNodeContents(editor());
    measured.setEnd(range.startContainer, range.startOffset);

    let length = 0;
    const visit = (node: Node): void => {
      for (const child of Array.from(node.childNodes)) {
        if (child.nodeType === Node.TEXT_NODE) {
          length += (child as Text).data.replace(/\u200B/g, '').length;
          continue;
        }
        const token = (child as HTMLElement).dataset?.['token'];
        if (token !== undefined) length += token.length;
        else visit(child);
      }
    };
    visit(measured.cloneContents());
    return length;
  }

  describe('rendering', () => {
    it('renders a bare reference as a chip and keeps the surrounding text', () => {
      setValue('Now playing {{ vars.title }} !');

      expect(chips().length).toBe(1);
      expect(chips()[0].textContent).toContain('vars.title');
      expect(editor().textContent).toContain('Now playing');
      expect(editor().textContent).toContain(' !');
    });

    it('marks an event reference apart from a variable one', () => {
      setValue('{{ vars.title }} {{ event.name }}');

      expect(chips().length).toBe(2);
      expect(chips()[0].classList.contains('vti-chip-event')).toBeFalse();
      expect(chips()[1].classList.contains('vti-chip-event')).toBeTrue();
    });

    it('flags a reference no variable answers', () => {
      setValue('{{ vars.title }} {{ vars.gone }}');

      expect(chips()[0].classList.contains('vti-chip-unknown')).toBeFalse();
      expect(chips()[1].classList.contains('vti-chip-unknown')).toBeTrue();
    });

    it('flags nothing while the variable list is still empty', () => {
      fixture.componentRef.setInput('variables', []);
      setValue('{{ vars.title }}');

      expect(chips()[0].classList.contains('vti-chip-unknown')).toBeFalse();
    });

    it('repaints the chips once the variable list arrives, without rebuilding the tree', () => {
      fixture.componentRef.setInput('variables', []);
      setValue('a {{ vars.gone }}');
      expect(chips()[0].classList.contains('vti-chip-unknown')).toBeFalse();
      const text = editor().firstChild;

      fixture.componentRef.setInput('variables', VARIABLES);
      fixture.detectChanges();

      expect(chips()[0].classList.contains('vti-chip-unknown')).toBeTrue();
      expect(editor().firstChild).toBe(text);
    });

    it('ignores a variable list that only carries new values', () => {
      setValue('a {{ vars.title }}');
      const text = editor().firstChild;

      fixture.componentRef.setInput('variables', VARIABLES.map(v => ({ ...v, value: 'changed' })));
      fixture.detectChanges();

      expect(editor().firstChild).toBe(text);
    });

    it('leaves a filtered expression as plain text', () => {
      setValue('{{ vars.title | upcase }}');

      expect(chips().length).toBe(0);
      expect(editor().textContent).toBe('{{ vars.title | upcase }}');
    });

    it('gives the caret a landing spot where a chip has no text beside it', () => {
      setValue('{{ vars.title }}');
      expect(nodeShapes()).toEqual(['anchor', 'chip', 'anchor']);

      setValue('{{ vars.title }}{{ vars.volume }}');
      expect(nodeShapes()).toEqual(['anchor', 'chip', 'anchor', 'chip', 'anchor']);
    });

    it('adds no anchor where the text already offers a caret position', () => {
      setValue('a {{ vars.title }} b');
      expect(nodeShapes()).toEqual(['text', 'chip', 'text']);
    });

    it('keeps the anchors out of the value', () => {
      setValue('{{ vars.title }}');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      editor().dispatchEvent(new Event('input'));

      expect(emitted).toEqual(['{{ vars.title }}']);
      expect(component.value).toBe('{{ vars.title }}');
    });

    it('reads the anchor in front of a leading chip as model offset 0', () => {
      setValue('{{ vars.title }}x');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();
      component.insertToken('A');

      expect(emitted).toEqual(['A{{ vars.title }}x']);
    });

    it('marks the field empty so the placeholder shows', () => {
      setValue('');
      expect(editor().classList.contains('vti-empty')).toBeTrue();

      setValue('x');
      expect(editor().classList.contains('vti-empty')).toBeFalse();
    });
  });

  describe('sizing', () => {
    function visibleLines(): number {
      const style = getComputedStyle(editor());
      const borders = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
      const padding = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom);
      return (parseFloat(style.minHeight) - padding - borders) / parseFloat(style.lineHeight);
    }

    it('sizes a multi-line field by text lines, not by box height', () => {
      fixture.componentRef.setInput('multiline', true);
      fixture.componentRef.setInput('rows', 5);
      fixture.detectChanges();

      expect(Math.round(visibleLines())).toBe(5);
    });

    it('follows the row count', () => {
      fixture.componentRef.setInput('multiline', true);
      fixture.componentRef.setInput('rows', 2);
      fixture.detectChanges();

      expect(Math.round(visibleLines())).toBe(2);
    });

    it('scrolls a single-line field without a scrollbar taking up its height', () => {
      setValue('a value long enough to overflow the control');
      expect(getComputedStyle(editor()).scrollbarWidth).toBe('none');
    });

    it('keeps the scrollbar on a multi-line field, which is the textarea analogue', () => {
      fixture.componentRef.setInput('multiline', true);
      fixture.detectChanges();

      expect(getComputedStyle(editor()).scrollbarWidth).not.toBe('none');
    });

    it('leaves a single-line field unconstrained by rows', () => {
      setValue('x');
      expect(editor().style.getPropertyValue('--vti-rows')).toBe('');
    });
  });

  describe('a field with an action overlaid on it', () => {
    const GUTTER = 46;
    const BAND = GUTTER + 10;

    function overlayAction(): void {
      editor().style.width = '200px';
      editor().style.setProperty('--input-action-inline-gutter', `${GUTTER}px`);
      editor().style.setProperty('--input-action-inline-band', `${BAND}px`);
      fixture.detectChanges();
    }

    function overflowingValue(): void {
      setValue('a {{ vars.title }} and enough trailing text to overflow the box several times over');
    }

    it('reveals the end of an overflowing value clear of the space the action reserved', () => {
      overlayAction();
      setValue('enough leading text to overflow the box several times over {{ vars.title }}');

      const end = chips()[chips().length - 1];
      editor().scrollLeft = 0;
      end.scrollIntoView({ block: 'nearest', inline: 'nearest' });

      const revealed = editor().getBoundingClientRect().right
        - end.getBoundingClientRect().right;
      expect(revealed).toBeGreaterThanOrEqual(BAND);
    });

    it('does not remove a reference the action is painted over', () => {
      overlayAction();
      overflowingValue();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      const chip = chips()[0];
      editor().scrollLeft = chip.offsetLeft - 10;
      const remove = chip.querySelector<HTMLElement>('.vti-chip-remove')!;
      remove.dispatchEvent(new MouseEvent('mousedown', {
        bubbles: true,
        clientX: editor().getBoundingClientRect().right - 3,
      }));
      fixture.detectChanges();

      expect(emitted).toEqual([]);
      expect(chips().length).toBe(1);
    });

    it('still removes a reference the user can see', () => {
      overlayAction();
      overflowingValue();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      const chip = chips()[0];
      const remove = chip.querySelector<HTMLElement>('.vti-chip-remove')!;
      remove.dispatchEvent(new MouseEvent('mousedown', {
        bubbles: true,
        clientX: chip.getBoundingClientRect().right,
      }));
      fixture.detectChanges();

      expect(chips().length).toBe(0);
    });

    it('removes a reference anywhere in a field that has no action overlaid on it', () => {
      editor().style.width = '200px';
      overflowingValue();

      const chip = chips()[0];
      const remove = chip.querySelector<HTMLElement>('.vti-chip-remove')!;
      remove.dispatchEvent(new MouseEvent('mousedown', {
        bubbles: true,
        clientX: editor().getBoundingClientRect().right - 3,
      }));
      fixture.detectChanges();

      expect(chips().length).toBe(0);
    });
  });

  describe('reading the edited tree back', () => {
    it('turns a token typed as loose text into a chip and emits the value', () => {
      setValue('Hello ');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      (editor().firstChild as Text).data = 'Hello {{ vars.title }}';
      caretAtEndOfText();
      editor().dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(emitted).toEqual(['Hello {{ vars.title }}']);
      expect(chips().length).toBe(1);
    });

    it('serializes a chip back to its exact source text', () => {
      setValue('{{  vars.title  }}');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      editor().appendChild(document.createTextNode('!'));
      editor().dispatchEvent(new Event('input'));

      expect(emitted).toEqual(['{{  vars.title  }}!']);
    });

    it('reads a browser-inserted break as a newline', () => {
      setValue('a');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      editor().appendChild(document.createElement('br'));
      editor().appendChild(document.createTextNode('b'));
      editor().dispatchEvent(new Event('input'));

      expect(emitted).toEqual(['a\nb']);
    });

    it('ignores an in-flight IME composition', () => {
      setValue('a');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      component.composing = true;
      editor().dispatchEvent(new Event('input'));
      expect(emitted).toEqual([]);

      editor().dispatchEvent(new Event('compositionend'));
      expect(emitted).toEqual(['a']);
    });
  });

  describe('chip removal', () => {
    it('removes only the clicked reference, keeping the rest of the text', () => {
      setValue('a {{ vars.title }} b {{ vars.volume }} c');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      const remove = chips()[0].querySelector<HTMLElement>('.vti-chip-remove');
      remove?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
      fixture.detectChanges();

      expect(emitted).toEqual(['a  b {{ vars.volume }} c']);
      expect(chips().length).toBe(1);
    });

    it('removes the right reference after the text before it changed', () => {
      setValue('a {{ vars.title }}');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      (editor().firstChild as Text).data = 'a longer prefix ';
      editor().dispatchEvent(new Event('input'));

      const remove = chips()[0].querySelector<HTMLElement>('.vti-chip-remove');
      remove?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));

      expect(emitted[emitted.length - 1]).toBe('a longer prefix ');
    });

    it('offers no remove affordance while disabled', () => {
      fixture.componentRef.setInput('disabled', true);
      setValue('{{ vars.title }}');

      expect(chips()[0].querySelector('.vti-chip-remove')).toBeNull();
      expect(editor().getAttribute('contenteditable')).toBe('false');
    });
  });

  describe('autocomplete', () => {
    it('opens on the braces and lists every candidate', () => {
      setValue('Hi {{');
      caretAtEndOfText();
      component.onCaretMoved();
      fixture.detectChanges();

      expect(component.isOpen()).toBeTrue();
      expect(component.suggestions().map(s => s.label)).toEqual([
        'vars.volume', 'vars.volatile', 'vars.title', 'event.name',
      ]);
    });

    it('filters as the name is typed', () => {
      setValue('Hi {{ vol');
      caretAtEndOfText();
      component.onCaretMoved();

      expect(component.suggestions().map(s => s.name)).toEqual(['volume', 'volatile']);
    });

    it('narrows to one root once its prefix is typed', () => {
      setValue('{{ event.');
      caretAtEndOfText();
      component.onCaretMoved();

      expect(component.suggestions().map(s => s.label)).toEqual(['event.name']);
    });

    it('honours the accepted types', () => {
      fixture.componentRef.setInput('acceptedTypes', ['numeric']);
      setValue('{{ ');
      caretAtEndOfText();
      component.onCaretMoved();

      expect(component.suggestions().map(s => s.name)).toEqual(['volume']);
    });

    it('stays closed when nothing matches', () => {
      setValue('{{ zzz');
      caretAtEndOfText();
      component.onCaretMoved();

      expect(component.isOpen()).toBeFalse();
    });

    it('replaces the half-typed reference with the completed token', () => {
      setValue('Hi {{ vol');
      caretAtEndOfText();
      component.onCaretMoved();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      component.accept(component.suggestions()[0]);
      fixture.detectChanges();

      expect(emitted).toEqual(['Hi {{ vars.volume }}']);
      expect(chips().length).toBe(1);
      expect(component.isOpen()).toBeFalse();
    });

    it('completes an event entry through the event root', () => {
      setValue('{{ nam');
      caretAtEndOfText();
      component.onCaretMoved();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      component.accept(component.suggestions()[0]);

      expect(emitted).toEqual(['{{ event.name }}']);
    });

    it('takes the highlighted row on Enter and moves the highlight with the arrows', () => {
      setValue('{{ vol');
      caretAtEndOfText();
      component.onCaretMoved();

      component.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
      expect(component.activeIndex()).toBe(1);

      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));
      component.onKeyDown(new KeyboardEvent('keydown', { key: 'Enter' }));

      expect(emitted).toEqual(['{{ vars.volatile }}']);
    });

    it('keeps the highlight while the caret has not moved', () => {
      setValue('{{ vol');
      caretAtEndOfText();
      component.onCaretMoved();
      component.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));

      component.onCaretMoved();

      expect(component.activeIndex()).toBe(1);
    });

    it('stays dismissed after Escape until the reference changes', () => {
      setValue('{{ vol');
      caretAtEndOfText();
      component.onCaretMoved();

      component.onKeyDown(new KeyboardEvent('keydown', { key: 'Escape' }));
      expect(component.isOpen()).toBeFalse();

      component.onCaretMoved();
      expect(component.isOpen()).toBeFalse();

      setValue('{{ volu');
      caretAtEndOfText();
      component.onCaretMoved();
      expect(component.isOpen()).toBeTrue();
    });
  });

  describe('insertion and keys', () => {
    it('appends an inserted token when the field was never focused', () => {
      setValue('Hello');
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      component.insertToken('{{ vars.title }}');

      expect(emitted).toEqual(['Hello{{ vars.title }}']);
    });

    it('inserts a token at the caret rather than replacing the text', () => {
      setValue('ab');
      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      component.insertToken('{{ vars.title }}');

      expect(emitted).toEqual(['a{{ vars.title }}b']);
    });

    it('inserts at the caret the field held before the picker took the focus', () => {
      setValue('ab');
      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();

      placeCaret(editor(), 0);
      editor().blur();

      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));
      component.insertToken('{{ vars.title }}');

      expect(emitted).toEqual(['a{{ vars.title }}b']);
    });

    it('blocks Enter in a single-line field and inserts a newline in a multi-line one', () => {
      setValue('ab');
      caretAtEndOfText();
      component.onCaretMoved();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      const single = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
      component.onKeyDown(single);
      expect(single.defaultPrevented).toBeTrue();
      expect(emitted).toEqual([]);

      fixture.componentRef.setInput('multiline', true);
      fixture.detectChanges();
      component.onKeyDown(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
      expect(emitted).toEqual(['ab\n']);
    });

    it('steps over a whole reference in one arrow press', () => {
      setValue('a{{ vars.title }}b');
      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();

      component.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
      expect(caretOffset()).toBe(17);

      component.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowLeft', cancelable: true }));
      expect(caretOffset()).toBe(1);
    });

    it('steps one character where no reference is in the way', () => {
      setValue('abc');
      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();

      component.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
      expect(caretOffset()).toBe(2);
    });

    it('steps over a surrogate pair as one character', () => {
      setValue('a😀b');
      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();

      component.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
      expect(caretOffset()).toBe(3);
    });

    it('leaves a modified or extending arrow press to the browser', () => {
      setValue('a{{ vars.title }}b');
      placeCaret(editor().firstChild as Text, 1);
      component.onCaretMoved();

      const shifted = new KeyboardEvent('keydown', { key: 'ArrowRight', shiftKey: true, cancelable: true });
      component.onKeyDown(shifted);
      expect(shifted.defaultPrevented).toBeFalse();

      const worded = new KeyboardEvent('keydown', { key: 'ArrowLeft', altKey: true, cancelable: true });
      component.onKeyDown(worded);
      expect(worded.defaultPrevented).toBeFalse();
    });

    it('does not swallow an arrow press at the edge of the value', () => {
      setValue('ab');
      placeCaret(editor().firstChild as Text, 2);
      component.onCaretMoved();

      const event = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true });
      component.onKeyDown(event);
      expect(event.defaultPrevented).toBeFalse();
    });

    it('pastes plain text at the caret and flattens newlines in a single-line field', () => {
      setValue('ab');
      caretAtEndOfText();
      component.onCaretMoved();
      const emitted: string[] = [];
      component.valueChange.subscribe(v => emitted.push(v));

      component.onPaste(pasteEvent('one\ntwo'));

      expect(emitted).toEqual(['abone two']);
    });
  });
});

function pasteEvent(text: string): ClipboardEvent {
  return {
    preventDefault: () => undefined,
    clipboardData: { getData: () => text },
  } as unknown as ClipboardEvent;
}
