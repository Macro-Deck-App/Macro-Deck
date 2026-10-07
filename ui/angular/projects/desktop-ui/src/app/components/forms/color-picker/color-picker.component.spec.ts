import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { parseColorReference, resolveColorReference, type Variable } from '@macro-deck/runtime';
import { LocalizationService, OverlayPanelComponent, VariableService } from '@shared';
import { SelectComponent } from '../select/select.component';
import { hexToHsv } from './color-conversion';
import { ColorPickerComponent, ColorPreset } from './color-picker.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

@Component({
  standalone: true,
  imports: [FormsModule, ColorPickerComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <shared-color-picker
      [presets]="presets()"
      [defaultColor]="defaultColor()"
      [resetValue]="resetValue()"
      [allowCustom]="allowCustom()"
      [ngModel]="value()"
      (ngModelChange)="value.set($event)" />
  `,
})
class HostComponent {
  presets = signal<ColorPreset[]>([
    { label: 'Red', value: '#ef4444' },
    { label: 'Green', value: '#22c55e' },
  ]);
  defaultColor = signal<string | undefined>(undefined);
  resetValue = signal<string | undefined>(undefined);
  allowCustom = signal(true);
  value = signal('');
}

describe('ColorPickerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  const originalEyeDropper = (window as unknown as { EyeDropper?: unknown }).EyeDropper;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    if (originalEyeDropper === undefined) {
      delete (window as unknown as { EyeDropper?: unknown }).EyeDropper;
    } else {
      (window as unknown as { EyeDropper?: unknown }).EyeDropper = originalEyeDropper;
    }
  });

  function swatches(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.cp-swatch'));
  }

  function customCell(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.cp-custom') as HTMLButtonElement;
  }

  function openCustomPicker(): void {
    customCell().click();
    fixture.detectChanges();
  }

  function query<T extends Element>(selector: string): T | null {
    return document.querySelector(selector) as T | null;
  }

  // Read once per instance, so the component is rebuilt against whichever platform is being faked.
  function withEyeDropper(sRGBHex: string | undefined): void {
    const host = window as unknown as { EyeDropper?: unknown };
    if (sRGBHex === undefined) {
      delete host.EyeDropper;
    } else {
      host.EyeDropper = class {
        open(): Promise<{ sRGBHex: string }> {
          return Promise.resolve({ sRGBHex });
        }
      };
    }

    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  }

  it('renders one swatch per preset', () => {
    expect(swatches().length).toBe(2);
  });

  it('marks the swatch that matches the bound value and shows a checkmark', async () => {
    fixture.componentInstance.value.set('#22c55e');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const green = swatches()[1];
    expect(green.classList).toContain('cp-selected');
    expect(green.querySelector('.cp-check')).toBeTruthy();
  });

  it('emits the preset value when a swatch is clicked', () => {
    swatches()[0].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('#ef4444');
  });

  it('hides the reset cell when no default color is provided', () => {
    expect(fixture.nativeElement.querySelector('.cp-reset')).toBeNull();
  });

  it('shows a reset cell that selects an empty default color', () => {
    fixture.componentInstance.defaultColor.set('');
    fixture.componentInstance.value.set('#ef4444');
    fixture.detectChanges();

    const reset = fixture.nativeElement.querySelector('.cp-reset') as HTMLButtonElement;
    expect(reset).toBeTruthy();
    reset.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('');
  });

  it('flags a non-preset value as a custom selection', async () => {
    fixture.componentInstance.value.set('#123456');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const custom = fixture.nativeElement.querySelector('.cp-custom');
    expect(custom.classList).toContain('cp-selected');
  });

  it('does not treat the default value as custom', async () => {
    fixture.componentInstance.defaultColor.set('');
    fixture.componentInstance.value.set('');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const custom = fixture.nativeElement.querySelector('.cp-custom');
    expect(custom.classList).not.toContain('cp-selected');
  });

  it('draws a transparent preset as a pattern rather than a blank cell and never counts it as custom', async () => {
    fixture.componentInstance.presets.set([
      { label: 'Red', value: '#ef4444' },
      { label: 'Transparent', value: 'transparent' },
    ]);
    fixture.detectChanges();

    await fixture.whenStable();

    swatches()[1].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('transparent');

    expect(swatches()[1].classList).toContain('cp-transparent');
    expect(swatches()[1].classList).toContain('cp-selected');
    expect(swatches()[0].classList).not.toContain('cp-transparent');
    expect(customCell().classList).not.toContain('cp-selected');
  });

  it('keeps the custom picker on the last real colour after transparent was chosen', async () => {
    fixture.componentInstance.presets.set([{ label: 'Transparent', value: 'transparent' }]);
    fixture.componentInstance.value.set('#123456');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    swatches()[0].click();
    fixture.detectChanges();

    openCustomPicker();

    expect(query<HTMLInputElement>('.cp-hex')!.value).toBe('#123456');
  });

  it('omits the custom cell when allowCustom is false', () => {
    fixture.componentInstance.allowCustom.set(false);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cp-custom')).toBeNull();
  });

  describe('custom colour picker', () => {
    it('opens an in-app picker rather than delegating to a native colour dialog', () => {
      expect(query('.cp-popover')).toBeNull();

      openCustomPicker();

      expect(query('.cp-popover')).toBeTruthy();
      expect(query('.cp-area')).toBeTruthy();
      expect(query('.cp-hue')).toBeTruthy();
      expect(query<HTMLInputElement>('.cp-hex')).toBeTruthy();
    });

    it('starts from the colour that is currently selected', async () => {
      fixture.componentInstance.value.set('#123456');
      fixture.detectChanges();
      await fixture.whenStable();

      openCustomPicker();

      expect(query<HTMLInputElement>('.cp-hex')!.value).toBe('#123456');
    });

    it('emits the colour typed into the hex field', () => {
      openCustomPicker();

      const hex = query<HTMLInputElement>('.cp-hex')!;
      hex.value = '#0af';
      hex.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#00aaff');
    });

    it('ignores an incomplete hex value while it is being typed', () => {
      openCustomPicker();
      const hex = query<HTMLInputElement>('.cp-hex')!;

      hex.value = '#00aaff';
      hex.dispatchEvent(new Event('input'));
      hex.value = '#00aaf';
      hex.dispatchEvent(new Event('input'));
      hex.value = '#00aa';
      hex.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#00aaff');
    });

    it('emits a colour of the chosen hue when the hue slider moves', async () => {
      fixture.componentInstance.value.set('#ff0000');
      fixture.detectChanges();
      await fixture.whenStable();

      openCustomPicker();

      const hue = query<HTMLInputElement>('.cp-hue')!;
      hue.value = '240';
      hue.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#0000ff');
    });

    it('moves along the saturation axis with the arrow keys', async () => {
      fixture.componentInstance.value.set('#ff0000');
      fixture.detectChanges();
      await fixture.whenStable();

      openCustomPicker();

      const area = query<HTMLElement>('.cp-area')!;
      area.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', shiftKey: true }));
      fixture.detectChanges();

      // The emitted colour is asserted through HSV rather than as a literal: which of two
      // neighbouring bytes a channel rounds to is not part of what moving along the axis means.
      const moved = hexToHsv(fixture.componentInstance.value())!;
      expect(moved.s).toBeLessThan(1);
      expect(moved.s).toBeGreaterThan(0.85);
      expect(moved.h).toBe(0);
      expect(moved.v).toBe(1);
    });

    it('hides the eyedropper where the platform has no screen picker', () => {
      withEyeDropper(undefined);
      openCustomPicker();

      expect(query('.cp-eyedropper')).toBeNull();
    });

    it('applies the colour the screen eyedropper returns', async () => {
      withEyeDropper('#00AAFF');
      openCustomPicker();

      query<HTMLButtonElement>('.cp-eyedropper')!.click();
      await Promise.resolve();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#00aaff');
    });

    it('closes the picker when the custom cell is clicked again', () => {
      openCustomPicker();
      openCustomPicker();

      expect(query('.cp-popover')).toBeNull();
    });
  });

  describe('resetValue', () => {
    it('shows the reset cell when only resetValue is set, with no defaultColor', () => {
      fixture.componentInstance.resetValue.set('$reset');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.cp-reset')).toBeTruthy();
    });

    it('emits resetValue rather than defaultColor when both are set', () => {
      fixture.componentInstance.defaultColor.set('#ef4444');
      fixture.componentInstance.resetValue.set('$reset');
      fixture.detectChanges();

      const reset = fixture.nativeElement.querySelector('.cp-reset') as HTMLButtonElement;
      reset.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('$reset');
    });

    it('an editor picker with only defaultColor still emits the colour, unaffected by resetValue existing', () => {
      fixture.componentInstance.defaultColor.set('#ef4444');
      fixture.detectChanges();

      const reset = fixture.nativeElement.querySelector('.cp-reset') as HTMLButtonElement;
      reset.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#ef4444');
    });

    it('does not render the sentinel value as a broken custom colour', async () => {
      fixture.componentInstance.resetValue.set('$reset');
      fixture.componentInstance.value.set('$reset');
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const custom = fixture.nativeElement.querySelector('.cp-custom');
      expect(custom.classList).not.toContain('cp-selected');
    });
  });
});

@Component({
  standalone: true,
  imports: [FormsModule, ColorPickerComponent, OverlayPanelComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <shared-color-picker
      [allowVariables]="allowVariables()"
      [allowAlpha]="allowAlpha()"
      [ngModel]="value()"
      (ngModelChange)="value.set($event)" />
    <shared-overlay-panel [isOpen]="otherOverlayOpen()" [x]="0" [y]="0">
      <span class="unrelated-panel">unrelated</span>
    </shared-overlay-panel>
  `,
})
class VariableHostComponent {
  otherOverlayOpen = signal(false);
  allowVariables = signal(true);
  allowAlpha = signal(true);
  value = signal('');
}

describe('ColorPickerComponent with color variables', () => {
  let fixture: ComponentFixture<VariableHostComponent>;

  const variable = (name: string, type: Variable['type'], value: string): Variable => ({
    id: name,
    name,
    scope: 'global',
    type,
    classification: 'user',
    value,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [VariableHostComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: VariableService,
          useValue: {
            variables: signal([
              variable('primary', 'color', '#3366ff'),
              variable('accent', 'color', '#ffffff'),
              variable('greeting', 'text', 'hello'),
            ]),
          },
        },
      ],
    });
    fixture = TestBed.createComponent(VariableHostComponent);
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => requestAnimationFrame(resolve));
    fixture.detectChanges();
  }

  async function show(value: string, open = true): Promise<void> {
    fixture.componentInstance.value.set(value);
    await settle();
    const chip = document.querySelector<HTMLButtonElement>('.cp-chip');
    if (open && chip && chip.getAttribute('aria-expanded') !== 'true') {
      chip.click();
      await settle();
    }
  }

  function element<T extends Element>(selector: string): T | null {
    return document.querySelector(selector) as T | null;
  }

  function selects(): SelectComponent[] {
    return fixture.debugElement.queryAll(By.directive(SelectComponent)).map(debug => debug.componentInstance);
  }

  it('offers no variable mode where the field does not allow variables, as for an old plugin', async () => {
    fixture.componentInstance.allowVariables.set(false);
    await show('#3366ff');

    expect(element('.cp-mode')).toBeNull();
    expect(element('.cp-variable')).toBeNull();
    expect(element('.cp-track')).toBeTruthy();
  });

  it('shows a closed reference as one chip with its resolved swatch, variable name and modifier count', async () => {
    await show('{{ vars.primary | color | color_darken: 20 | color_opacity: 50 }}', false);

    const chip = element<HTMLButtonElement>('.cp-chip')!;
    expect(chip.querySelector('.cp-chip-label')!.textContent!.trim()).toBe('primary');
    expect(chip.getAttribute('title')).toBe('primary');
    expect(chip.querySelector('.cp-chip-badge')!.textContent!.trim()).toBe('2');
    expect((chip.querySelector('.cp-chip-swatch') as HTMLElement).style.getPropertyValue('--cp-fill')).toBe('#003df580');
    expect(element('.cp-editor')).toBeNull();
  });

  it('names a missing variable on the chip itself', async () => {
    await show('{{ vars.gone | color }}', false);

    expect(element('.cp-chip-label')!.textContent).toContain('gone');
    expect(element('.cp-chip-badge')).toBeNull();
  });

  it('opens the editor from the chip and hands focus back to the chip on Escape', async () => {
    await show('{{ vars.primary | color }}', false);
    const chip = element<HTMLButtonElement>('.cp-chip')!;

    chip.click();
    await settle();
    const editor = element<HTMLElement>('.cp-editor')!;
    expect(editor).toBeTruthy();
    expect(editor.contains(document.activeElement)).toBeTrue();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(element('.cp-editor')).toBeNull();
    expect(document.activeElement).toBe(chip);
  });

  it('describes the modifier count on the chip in words while the badge shows the digit', async () => {
    await show('{{ vars.primary | color | color_darken: 20 | color_opacity: 50 }}', false);

    const chip = element<HTMLButtonElement>('.cp-chip')!;
    const description = document.getElementById(chip.getAttribute('aria-describedby')!)!;
    expect(description.textContent!.trim()).toBe('2 modifiers');
    expect(element('.cp-chip-badge')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('stays open while a click lands in its own nested select, but closes for a click elsewhere', async () => {
    await show('{{ vars.primary | color }}');

    element<HTMLButtonElement>('.cp-editor .cp-variable-select button.control')!.click();
    await settle();
    document.querySelector('.cp-editor .sel-option, .sel-option')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    expect(element('.cp-editor')).toBeTruthy();

    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    expect(element('.cp-editor')).toBeNull();
  });

  it('is not held open by an unrelated overlay elsewhere in the app', async () => {
    await show('{{ vars.primary | color }}');
    fixture.componentInstance.otherOverlayOpen.set(true);
    await settle();
    expect(element('.unrelated-panel')).toBeTruthy();

    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    expect(element('.cp-editor')).toBeNull();
  });

  it('closes and hands focus back to the chip when tabbing past its last control', async () => {
    await show('{{ vars.primary | color }}');
    const last = element<HTMLButtonElement>('.cp-editor .cp-add')!;
    last.focus();

    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    await settle();

    expect(element('.cp-editor')).toBeNull();
    expect(document.activeElement).toBe(element('.cp-chip'));
  });

  it('opens the editor right away when switching from a color to a variable', async () => {
    await show('#3366ff');

    (document.querySelectorAll('.cp-mode .seg-option')[1] as HTMLButtonElement).click();
    await settle();

    expect(fixture.componentInstance.value()).toBe('{{ vars.accent | color }}');
    expect(element('.cp-editor')).toBeTruthy();
  });

  it('opens a stored reference in variable mode, lists only color variables and previews the resolved color', async () => {
    await show('{{ vars.primary | color | color_darken: 20 }}');

    expect(element('.cp-variable')).toBeTruthy();
    expect(selects()[0].options.map(option => option.value)).toEqual(['accent', 'primary']);
    expect(selects()[0].options.map(option => option.swatch)).toEqual(['#ffffff', '#3366ff']);
    expect(element<HTMLElement>('.cpr-resolved')!.style.getPropertyValue('--cpr-fill')).toBe('#003df5');
  });

  it('shows the original and result preview only once there is a modifier', async () => {
    await show('{{ vars.primary | color }}');
    expect(element('shared-color-picker-result')).toBeNull();

    element<HTMLButtonElement>('.cp-add')!.click();
    await show(fixture.componentInstance.value());
    expect(element('shared-color-picker-result')).toBeTruthy();
  });

  it('stops offering new modifiers once a reference holds the most it may', async () => {
    const steps = Array.from({ length: 32 }, () => ' | color_hue: 1').join('');
    await show(`{{ vars.primary | color${steps} }}`);

    const add = element<HTMLButtonElement>('.cp-add')!;
    expect(add.disabled).toBeTrue();
    add.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe(`{{ vars.primary | color${steps} }}`);
  });

  it('writes every modifier edit back as the canonical reference', async () => {
    await show('{{vars.primary|color|color_darken: 20}}');

    element<HTMLButtonElement>('.cp-add')!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('{{ vars.primary | color | color_darken: 20 | color_lighten: 20 }}');
    await show(fixture.componentInstance.value());

    const amount = document.querySelectorAll('.cp-amount input')[0] as HTMLInputElement;
    amount.value = '35';
    amount.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('{{ vars.primary | color | color_darken: 35 | color_lighten: 20 }}');
    await show(fixture.componentInstance.value());

    (document.querySelectorAll('.cp-remove')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('{{ vars.primary | color | color_darken: 35 }}');
  });

  it('previews the original and the result hex exactly as the runtime math resolves them', async () => {
    const reference = '{{ vars.primary | color | color_darken: 20 | color_mix: vars.accent, 50 | color_reduce_opacity: 30 }}';
    await show(reference);

    const expected = resolveColorReference(parseColorReference(reference)!, name => ({ primary: '#3366ff', accent: '#ffffff' })[name])!;
    const hexes = Array.from(document.querySelectorAll('.cpr-hex')).map(hex => (hex as HTMLElement).textContent!.trim());
    expect(hexes).toEqual(['#3366FF', expected.toUpperCase()]);
    expect(document.querySelectorAll('.cpr-summary-item').length).toBe(3);
    expect(Array.from(document.querySelectorAll('.cp-step-index')).map(index => (index as HTMLElement).textContent!.trim()))
      .toEqual(['1', '2', '3']);
  });

  it('keeps the steps a plain ordered list and writes their amounts in the UI language', async () => {
    TestBed.inject(LocalizationService).culture.set('de');
    await show('{{ vars.primary | color | color_lighten: 12.5 | color_hue: -30 }}');

    const steps = Array.from(document.querySelectorAll('.cp-chain > li')) as HTMLElement[];
    expect(steps.length).toBe(2);
    expect(steps.every(step => step.getAttribute('role') === null)).toBeTrue();
    const summaries = Array.from(document.querySelectorAll('.cpr-summary-item'))
      .map(item => (item as HTMLElement).textContent!.trim());
    expect(summaries[0]).toContain('12,5%');
    expect(summaries[1]).toContain('-30\u00b0');
  });

  it('clamps a typed amount to its range', async () => {
    await show('{{ vars.primary | color | color_lighten: 50 | color_hue: 0 }}');
    const type = (index: number, text: string) => {
      const field = document.querySelectorAll('.cp-amount input')[index] as HTMLInputElement;
      field.value = text;
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    type(0, '150');
    expect(fixture.componentInstance.value()).toBe('{{ vars.primary | color | color_lighten: 100 | color_hue: 0 }}');
    await show(fixture.componentInstance.value());

    type(1, '-500');
    expect(fixture.componentInstance.value()).toBe('{{ vars.primary | color | color_lighten: 100 | color_hue: -360 }}');
    expect(Array.from(document.querySelectorAll('.cp-amount-unit')).map(unit => (unit as HTMLElement).textContent!.trim()))
      .toEqual(['%', '\u00b0']);
  });

  it('switches back to a static color holding what the reference currently resolves to', async () => {
    await show('{{ vars.primary | color | color_opacity: 50 }}');

    const staticOption = document.querySelector('.cp-mode .seg-option') as HTMLButtonElement;
    staticOption.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('#3366ff80');
  });

  it('takes an eight-digit hex and emits it canonically when alpha is allowed', async () => {
    await show('#3366ff');

    element<HTMLButtonElement>('.cp-custom')!.click();
    fixture.detectChanges();
    const hex = document.querySelector('.cp-hex') as HTMLInputElement;
    expect(document.querySelector('.cp-alpha')).toBeTruthy();
    hex.value = '#3366FFCC';
    hex.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('#3366ffcc');
  });

  it('shows a template it cannot edit read-only and offers only a reset', async () => {
    await show('{{ vars.primary | upcase }}');

    expect(element('.cp-unsupported')).toBeTruthy();
    expect(element('.cp-mode')).toBeNull();
    expect(fixture.componentInstance.value()).toBe('{{ vars.primary | upcase }}');
  });
});
