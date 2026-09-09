import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';

import { HOST_URL_RESOLVER } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { ParamInputComponent } from './param-input.component';

@Component({
  standalone: true,
  imports: [FormsModule, ParamInputComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <shared-param-input
      [type]="type()"
      [multiline]="multiline()"
      [showLiquid]="showLiquid()"
      [allowReferences]="allowReferences()"
      [variables]="variables()"
      [ariaLabel]="ariaLabel()"
      [ngModel]="value()"
      (ngModelChange)="value.set($event)" />
  `,
})
class HostComponent {
  type = signal<'text' | 'number'>('text');
  multiline = signal(false);
  showLiquid = signal(false);
  allowReferences = signal(false);
  variables = signal<Variable[]>([]);
  ariaLabel = signal<string | null>(null);
  value = signal('');
}

const TITLE: Variable = {
  id: 'id-title',
  name: 'title',
  scope: 'global',
  type: 'text',
  classification: 'user',
  value: 'Song',
};

describe('ParamInputComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        // The Liquid trigger pulls in VariableService -> ApiService, which resolves the host URL.
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function paramInput(): ParamInputComponent {
    return fixture.debugElement.children[0].componentInstance as ParamInputComponent;
  }

  function innerInputHost(): HTMLElement {
    return fixture.nativeElement.querySelector('shared-input');
  }

  function chipEditor(): HTMLElement | null {
    return fixture.nativeElement.querySelector('shared-variable-text-input');
  }

  function liquidTrigger(): HTMLButtonElement | null {
    return fixture.nativeElement.querySelector('shared-template-builder .tb-trigger');
  }

  function enableLiquid(): void {
    fixture.componentInstance.showLiquid.set(true);
    fixture.detectChanges();
  }

  function enableChipEditor(): void {
    fixture.componentInstance.allowReferences.set(true);
    fixture.componentInstance.variables.set([TITLE]);
    fixture.detectChanges();
  }

  async function setValue(value: string): Promise<void> {
    fixture.componentInstance.value.set(value);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders a textarea when multiline is set', () => {
    fixture.componentInstance.multiline.set(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('shared-input textarea.control')).toBeTruthy();
  });

  it('propagates typed values through ngModel', () => {
    paramInput().onInput('hello');
    expect(fixture.componentInstance.value()).toBe('hello');
  });

  it('emits the current value when the field loses focus', () => {
    const received: string[] = [];
    paramInput().valueBlur.subscribe(value => received.push(value));
    paramInput().onInput('macro-deck.app');

    paramInput().onBlur();

    expect(received).toEqual(['macro-deck.app']);
  });

  it('keeps the plain input while references are disabled', () => {
    expect(chipEditor()).toBeNull();
    expect(innerInputHost()).toBeTruthy();
  });

  it('offers no separate variable-picker trigger', () => {
    enableChipEditor();
    expect(fixture.nativeElement.querySelector('shared-variable-picker')).toBeNull();
  });

  it('keeps the plain input for a number field', () => {
    enableChipEditor();
    fixture.componentInstance.type.set('number');
    fixture.detectChanges();
    expect(chipEditor()).toBeNull();
    expect(innerInputHost()).toBeTruthy();
  });

  it('names the plain input for a screen reader', () => {
    fixture.componentInstance.ariaLabel.set('Label');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-input input')?.getAttribute('aria-label'))
      .toBe('Label');
  });

  it('names the chip editor for a screen reader', () => {
    enableChipEditor();
    fixture.componentInstance.ariaLabel.set('Label');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="textbox"]')?.getAttribute('aria-label'))
      .toBe('Label');
  });

  it('swaps in the chip editor for a text field that accepts references', () => {
    enableChipEditor();
    expect(chipEditor()).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-input')).toBeNull();
  });

  it('renders no Liquid trigger while the field does not offer templates', () => {
    expect(liquidTrigger()).toBeNull();
  });

  it('renders the Liquid trigger inside the field, not beside it', () => {
    enableLiquid();

    const trigger = liquidTrigger();
    const field = innerInputHost();
    expect(trigger).toBeTruthy();
    // The action belongs to the input: it shares an ancestor box with the control it acts on,
    // rather than sitting next to it and claiming its own horizontal space.
    expect(field.parentElement?.contains(trigger!)).toBe(true);
  });

  it('names the Liquid trigger for assistive technology', () => {
    enableLiquid();

    expect(liquidTrigger()?.getAttribute('aria-label')).toBeTruthy();
  });

  it('blocks the Liquid trigger while the field is disabled', () => {
    enableLiquid();

    paramInput().setDisabledState(true);
    fixture.detectChanges();
    expect(liquidTrigger()?.disabled).toBe(true);

    paramInput().setDisabledState(false);
    fixture.detectChanges();
    expect(liquidTrigger()?.disabled).toBe(false);
  });

  it('writes an applied template back through the model', () => {
    enableLiquid();

    paramInput().onTemplateApply('Now playing: {{ vars.title }}');

    expect(fixture.componentInstance.value()).toBe('Now playing: {{ vars.title }}');
  });

  it('renders an existing reference as a chip', async () => {
    enableChipEditor();
    await setValue('Now: {{ vars.title }}');

    expect(chipEditor()?.querySelectorAll('.vti-chip').length).toBe(1);
  });

  describe('the space reserved for the Liquid trigger', () => {
    function editor(): HTMLElement {
      return fixture.nativeElement.querySelector('.vti-editor');
    }

    function backdrop(): CSSStyleDeclaration {
      return getComputedStyle(chipEditor()!, '::after');
    }

    async function overflowingSingleLineField(): Promise<void> {
      enableChipEditor();
      enableLiquid();
      await setValue('Now playing: {{ vars.title }} on repeat for the rest of the afternoon');
    }

    it('is covered across the whole width the field reserved', async () => {
      await overflowingSingleLineField();

      expect(parseFloat(backdrop().width))
        .toBe(parseFloat(getComputedStyle(editor()).paddingInlineEnd));
    });

    it('is covered from the top border of the field to its bottom border', async () => {
      await overflowingSingleLineField();

      const field = getComputedStyle(editor());
      expect(parseFloat(backdrop().insetBlockStart)).toBe(parseFloat(field.borderTopWidth));
      expect(parseFloat(backdrop().insetBlockEnd)).toBe(parseFloat(field.borderBottomWidth));
    });

    it('hides what scrolls into it rather than letting it reach the trigger', async () => {
      await overflowingSingleLineField();

      expect(backdrop().backgroundColor).toBe(getComputedStyle(editor()).backgroundColor);
    });

    it('still lets a click there put the caret in the field', async () => {
      await overflowingSingleLineField();

      // Karma's banner can push the fixture out of the viewport, where nothing hit-tests at all.
      editor().scrollIntoView({ block: 'center' });
      const field = editor().getBoundingClientRect();
      // Other fixtures share the document, so only this one's elements can be hit-tested.
      const hits = document
        .elementsFromPoint(field.right - 3, field.top + field.height / 2)
        .filter(element => fixture.nativeElement.contains(element));
      expect(hits.length).toBeGreaterThan(0);
      expect(hits[0]).toBe(editor());
    });

    it('dims with the field rather than staying a solid block on it', async () => {
      await overflowingSingleLineField();
      paramInput().setDisabledState(true);
      fixture.detectChanges();

      expect(parseFloat(backdrop().opacity)).toBeLessThan(1);
    });

    it('is not reserved on a multiline field, which puts the trigger below the text', async () => {
      await overflowingSingleLineField();
      const singleLine = getComputedStyle(editor()).scrollPaddingInlineEnd;

      fixture.componentInstance.multiline.set(true);
      fixture.detectChanges();

      expect(parseFloat(singleLine)).toBeGreaterThan(0);
      expect(getComputedStyle(editor()).scrollPaddingInlineEnd).toBe('auto');
    });
  });

});
