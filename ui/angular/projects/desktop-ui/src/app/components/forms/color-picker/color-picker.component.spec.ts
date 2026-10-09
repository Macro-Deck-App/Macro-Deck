import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { parseColorReference, resolveColorReference, type Variable } from '@macro-deck/runtime';
import { LocalizationService, MAX_PALETTE_COLORS, OverlayPanelComponent, VariableService } from '@shared';
import { SelectComponent } from '../select/select.component';
import { hexToHsv } from './color-conversion';
import { ColorPickerComponent, ColorPreset } from './color-picker.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { FakeColorPalette, provideColorPaletteTesting } from '../../../../testing/color-palette-test-support';

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
      [allowAlpha]="allowAlpha()"
      [disabled]="disabled()"
      [ngModel]="value()"
      (ngModelChange)="value.set($event)" />
  `,
})
class HostComponent {
  presets = signal<ColorPreset[] | undefined>([
    { label: 'Red', value: '#ef4444' },
    { label: 'Green', value: '#22c55e' },
  ]);
  defaultColor = signal<string | undefined>(undefined);
  resetValue = signal<string | undefined>(undefined);
  allowCustom = signal(true);
  allowAlpha = signal(false);
  disabled = signal(false);
  value = signal('');
}

describe('ColorPickerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let palette: FakeColorPalette;
  const originalEyeDropper = (window as unknown as { EyeDropper?: unknown }).EyeDropper;

  beforeEach(async () => {
    palette = new FakeColorPalette();
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting(), ...provideColorPaletteTesting(palette)],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => {
    if (originalEyeDropper === undefined) {
      delete (window as unknown as { EyeDropper?: unknown }).EyeDropper;
    } else {
      (window as unknown as { EyeDropper?: unknown }).EyeDropper = originalEyeDropper;
    }
  });

  async function show(value: string): Promise<void> {
    fixture.componentInstance.value.set(value);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function field(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.cp-chip') as HTMLButtonElement;
  }

  function fieldText(): string {
    return field().querySelector('.cp-chip-label')!.textContent!.trim();
  }

  function open(): void {
    field().click();
    fixture.detectChanges();
  }

  function query<T extends Element>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector) as T | null;
  }

  function presetSwatches(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.cp-popover .cp-swatch:not(.cp-palette-swatch)'));
  }

  function paletteSwatches(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.cp-popover .cp-palette-swatch'));
  }

  function addButton(): HTMLButtonElement {
    return query<HTMLButtonElement>('.cp-add-palette')!;
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

  describe('field', () => {
    it('is one compact field naming the colour by its hex code, with nothing else drawn inline', async () => {
      await show('#22C55E');

      expect(fieldText()).toBe('22C55E');
      expect((field().querySelector('.cp-chip-swatch') as HTMLElement).style.getPropertyValue('--cp-fill')).toBe('#22C55E');
      expect(fixture.nativeElement.querySelector('.cp-swatch')).toBeNull();
      expect(query('.cp-popover')).toBeNull();
    });

    it('says Not set with the reset glyph while nothing is chosen and there is no default to show', async () => {
      await show('');

      expect(fieldText()).toBe('Not set');
      expect(field().querySelector('.cp-chip-glyph')).toBeTruthy();
      expect(field().querySelector('.cp-chip-swatch')).toBeNull();
    });

    it('shows the default colour while the value is empty and the default is a real colour', async () => {
      fixture.componentInstance.defaultColor.set('#ef4444');
      await show('');

      expect(fieldText()).toBe('ef4444');
      expect(field().querySelector('.cp-chip-glyph')).toBeNull();
    });

    it('names a transparent value instead of drawing an empty swatch', async () => {
      fixture.componentInstance.presets.set([{ label: 'Transparent', value: 'transparent' }]);
      await show('transparent');

      expect(fieldText()).toBe('Transparent');
    });

    it('cannot be opened while disabled', async () => {
      fixture.componentInstance.disabled.set(true);
      await show('#ef4444');

      expect(field().disabled).toBeTrue();
      open();
      expect(query('.cp-popover')).toBeNull();
    });
  });

  describe('popover', () => {
    it('opens an in-app picker with the presets rather than delegating to a native colour dialog', () => {
      open();

      expect(query('.cp-popover')).toBeTruthy();
      expect(query('.cp-area')).toBeTruthy();
      expect(query('.cp-hue')).toBeTruthy();
      expect(query<HTMLInputElement>('.cp-hex')).toBeTruthy();
      expect(presetSwatches().length).toBe(2);
    });

    it('offers no Color and Variable tabs where the field does not allow variables', () => {
      open();

      expect(query('.cp-mode')).toBeNull();
    });

    it('emits the preset value when a swatch is clicked and marks it with a checkmark', async () => {
      open();
      presetSwatches()[1].click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#22c55e');
      expect(presetSwatches()[1].classList).toContain('cp-selected');
      expect(presetSwatches()[1].querySelector('.cp-check')).toBeTruthy();
    });

    it('moves the picker to a preset that was chosen while it is open', async () => {
      open();
      presetSwatches()[0].click();
      fixture.detectChanges();

      expect(query<HTMLInputElement>('.cp-hex')!.value).toBe('#ef4444');
    });

    it('draws a transparent preset as a pattern and keeps the picker on the last real colour', async () => {
      fixture.componentInstance.presets.set([
        { label: 'Red', value: '#ef4444' },
        { label: 'Transparent', value: 'transparent' },
      ]);
      await show('#123456');
      open();

      presetSwatches()[1].click();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('transparent');
      expect(presetSwatches()[1].classList).toContain('cp-transparent');
      expect(presetSwatches()[0].classList).not.toContain('cp-transparent');
      expect(query<HTMLInputElement>('.cp-hex')!.value).toBe('#123456');
    });

    it('offers only the presets when allowCustom is false', () => {
      fixture.componentInstance.allowCustom.set(false);
      fixture.detectChanges();
      open();

      expect(query('.cp-area')).toBeNull();
      expect(query('.cp-add-palette')).toBeNull();
      expect(presetSwatches().length).toBe(2);
    });

    it('starts from the colour that is currently selected', async () => {
      await show('#123456');
      open();

      expect(query<HTMLInputElement>('.cp-hex')!.value).toBe('#123456');
    });

    it('emits the colour typed into the hex field', () => {
      open();

      const hex = query<HTMLInputElement>('.cp-hex')!;
      hex.value = '#0af';
      hex.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#00aaff');
    });

    it('ignores an incomplete hex value while it is being typed', () => {
      open();
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
      await show('#ff0000');
      open();

      const hue = query<HTMLInputElement>('.cp-hue')!;
      hue.value = '240';
      hue.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#0000ff');
    });

    it('moves along the saturation axis with the arrow keys', async () => {
      await show('#ff0000');
      open();

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
      open();

      expect(query('.cp-eyedropper')).toBeNull();
    });

    it('applies the colour the screen eyedropper returns', async () => {
      withEyeDropper('#00AAFF');
      open();

      query<HTMLButtonElement>('.cp-eyedropper')!.click();
      await Promise.resolve();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#00aaff');
    });

    it('closes when the field is clicked again', () => {
      open();
      open();

      expect(query('.cp-popover')).toBeNull();
    });
  });

  describe('inline', () => {
    it('draws the picker without a field and lets Tab and Shift+Tab leave it at either edge', async () => {
      delete (window as unknown as { EyeDropper?: unknown }).EyeDropper;
      const inline = TestBed.createComponent(ColorPickerComponent);
      inline.componentRef.setInput('inline', true);
      inline.componentRef.setInput('showPalette', false);
      inline.detectChanges();
      const host = inline.nativeElement as HTMLElement;
      document.body.appendChild(host);

      expect(host.querySelector('.cp-chip')).toBeNull();
      const focusable = Array.from(host.querySelectorAll<HTMLElement>('button:not([disabled]), input, [tabindex]'))
        .filter(element => element.getClientRects().length > 0);
      const forward = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
      const backward = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true });
      focusable[focusable.length - 1].focus();
      focusable[focusable.length - 1].dispatchEvent(forward);
      focusable[0].focus();
      focusable[0].dispatchEvent(backward);

      expect(focusable[focusable.length - 1].classList).toContain('cp-hex');
      expect(focusable[0].classList).toContain('cp-area');
      expect(forward.defaultPrevented).toBeFalse();
      expect(backward.defaultPrevented).toBeFalse();
      host.remove();
    });
  });

  describe('palette', () => {
    it('lists the palette after the field\'s own swatches and selects a colour on click', async () => {
      palette.colors.set(['#3ff4ee', '#123456']);
      open();

      expect(paletteSwatches().map(swatch => swatch.style.getPropertyValue('--cp-fill'))).toEqual(['#3ff4ee', '#123456']);
      expect(paletteSwatches()[0].getAttribute('aria-label')).toBe('Custom color #3ff4ee');

      paletteSwatches()[0].click();
      fixture.detectChanges();
      expect(fixture.componentInstance.value()).toBe('#3ff4ee');
    });

    it('names a default colour in the palette by its name', () => {
      palette.colors.set(['#3b82f6']);
      open();

      expect(paletteSwatches()[0].getAttribute('aria-label')).toBe('Blue');
    });

    it('offers no palette colours at all once the user removed every one of them', () => {
      fixture.componentInstance.presets.set(undefined);
      fixture.detectChanges();
      open();

      expect(presetSwatches().length).toBe(0);
      expect(paletteSwatches().length).toBe(0);
      expect(query('.cp-add-palette')).toBeTruthy();
    });

    it('does not repeat a preset that is also in the palette', () => {
      palette.colors.set(['#ef4444', '#3ff4ee']);
      open();

      expect(paletteSwatches().map(swatch => swatch.style.getPropertyValue('--cp-fill'))).toEqual(['#3ff4ee']);
    });

    it('offers translucent palette colours only to a field that can store them', () => {
      palette.colors.set(['#3ff4ee', '#ef444480']);
      open();
      expect(paletteSwatches().length).toBe(1);

      open();
      fixture.componentInstance.allowAlpha.set(true);
      fixture.detectChanges();
      open();
      expect(paletteSwatches().length).toBe(2);
    });

    it('adds the current colour with plus', async () => {
      await show('#3ff4ee');
      open();

      addButton().click();
      fixture.detectChanges();

      expect(palette.added).toEqual(['#3ff4ee']);
      expect(paletteSwatches().length).toBe(1);
      expect(addButton().disabled).toBeTrue();
    });

    for (const [name, value] of [
      ['nothing chosen', ''],
      ['transparent', 'transparent'],
      ['a preset', '#ef4444'],
    ] as const) {
      it(`cannot add ${name}`, async () => {
        fixture.componentInstance.presets.set([
          { label: 'Red', value: '#ef4444' },
          { label: 'Transparent', value: 'transparent' },
        ]);
        await show(value);
        open();

        expect(addButton().disabled).toBeTrue();
      });
    }

    it('cannot add to a full palette and says why', async () => {
      palette.colors.set(Array.from({ length: MAX_PALETTE_COLORS }, (_, i) => `#0000${i.toString(16).padStart(2, '0')}`));
      await show('#abcdef');
      open();

      expect(addButton().disabled).toBeTrue();
      expect(addButton().title).toContain(String(MAX_PALETTE_COLORS));
    });

    it('asks before removing a colour with its remove button or the Delete key', async () => {
      palette.colors.set(['#111111', '#222222']);
      open();
      const modal = () => document.querySelector('shared-confirmation-modal');
      const modalButton = (text: string) =>
        Array.from(modal()!.querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent!.trim() === text)!;
      const closed = async () => {
        for (let attempt = 0; attempt < 40 && modal(); attempt++) {
          await new Promise(resolve => setTimeout(resolve, 25));
          fixture.detectChanges();
        }
      };

      query<HTMLButtonElement>('.cp-palette-remove')!.click();
      fixture.detectChanges();
      expect(modal()).toBeTruthy();
      expect(palette.removed).toEqual([]);
      expect(query('.cp-popover')).toBeNull();

      modalButton('Remove').click();
      await closed();
      expect(palette.removed).toEqual(['#111111']);
      expect(query('.cp-popover')).toBeTruthy();

      paletteSwatches()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }));
      fixture.detectChanges();
      modalButton('Cancel').click();
      await closed();

      expect(palette.removed).toEqual(['#111111']);
      expect(paletteSwatches().length).toBe(1);
      expect(fixture.componentInstance.value()).toBe('');
    });
  });

  describe('reset', () => {
    it('offers no reset when no default color is provided', async () => {
      await show('#ef4444');

      expect(fixture.nativeElement.querySelector('.cp-reset')).toBeNull();
    });

    it('offers a reset next to the field that returns to an empty default', async () => {
      fixture.componentInstance.defaultColor.set('');
      await show('#ef4444');

      const reset = fixture.nativeElement.querySelector('.cp-reset') as HTMLButtonElement;
      reset.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('');
      expect(fixture.nativeElement.querySelector('.cp-reset')).toBeNull();
    });

    it('offers the reset when only resetValue is set, with no defaultColor', async () => {
      fixture.componentInstance.resetValue.set('$reset');
      await show('#ef4444');

      expect(fixture.nativeElement.querySelector('.cp-reset')).toBeTruthy();
    });

    it('emits resetValue rather than defaultColor when both are set', async () => {
      fixture.componentInstance.defaultColor.set('#ef4444');
      fixture.componentInstance.resetValue.set('$reset');
      await show('#22c55e');

      (fixture.nativeElement.querySelector('.cp-reset') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('$reset');
    });

    it('emits the default colour when only defaultColor is set', async () => {
      fixture.componentInstance.defaultColor.set('#ef4444');
      await show('#22c55e');

      (fixture.nativeElement.querySelector('.cp-reset') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('#ef4444');
    });

    it('does not render the sentinel value as a broken colour but as the default', async () => {
      fixture.componentInstance.resetValue.set('$reset');
      await show('$reset');

      expect(fieldText()).toBe('Default color');
      expect(field().querySelector('.cp-chip-glyph')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('.cp-reset')).toBeNull();
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
        ...provideColorPaletteTesting(),
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
    expect(element('.cp-palette')).toBeTruthy();
  });

  it('shows a closed reference as one chip with its resolved swatch, variable name and modifier count', async () => {
    await show('{{ vars.primary | color | color_darken: 20 | color_opacity: 50 }}', false);

    const chip = element<HTMLButtonElement>('.cp-chip')!;
    expect(chip.querySelector('.cp-chip-label')!.textContent!.trim()).toBe('primary');
    expect(chip.getAttribute('title')).toBe('primary');
    expect(chip.querySelector('.cp-chip-badge')!.textContent!.trim()).toBe('2');
    expect((chip.querySelector('.cp-chip-swatch') as HTMLElement).style.getPropertyValue('--cp-fill')).toBe('#003df580');
    expect(element('.cp-popover')).toBeNull();
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
    const editor = element<HTMLElement>('.cp-popover')!;
    expect(editor).toBeTruthy();
    expect(editor.contains(document.activeElement)).toBeTrue();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(element('.cp-popover')).toBeNull();
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

    element<HTMLButtonElement>('.cp-popover .cp-variable-select button.control')!.click();
    await settle();
    document.querySelector('.cp-popover .sel-option, .sel-option')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    expect(element('.cp-popover')).toBeTruthy();

    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    expect(element('.cp-popover')).toBeNull();
  });

  it('is not held open by an unrelated overlay elsewhere in the app', async () => {
    await show('{{ vars.primary | color }}');
    fixture.componentInstance.otherOverlayOpen.set(true);
    await settle();
    expect(element('.unrelated-panel')).toBeTruthy();

    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    await settle();
    expect(element('.cp-popover')).toBeNull();
  });

  it('closes and hands focus back to the chip when tabbing past its last control', async () => {
    await show('{{ vars.primary | color }}');
    const last = element<HTMLButtonElement>('.cp-popover .cp-add')!;
    last.focus();

    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    await settle();

    expect(element('.cp-popover')).toBeNull();
    expect(document.activeElement).toBe(element('.cp-chip'));
  });

  it('keeps the popover open on the Variable tab when switching from a color to a variable', async () => {
    await show('#3366ff');

    (document.querySelectorAll('.cp-mode .seg-option')[1] as HTMLButtonElement).click();
    await settle();

    expect(fixture.componentInstance.value()).toBe('{{ vars.accent | color }}');
    expect(element('.cp-variable')).toBeTruthy();
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

  it('keeps each tab\'s own value when switching between Color and Variable and back', async () => {
    await show('#123456');
    const tab = (index: number) => (document.querySelectorAll('.cp-mode .seg-option')[index] as HTMLButtonElement);

    tab(1).click();
    await settle();
    element<HTMLButtonElement>('.cp-add')!.click();
    await settle();
    const reference = fixture.componentInstance.value();
    expect(reference).toContain('vars.accent');
    expect(reference).toContain('color_lighten');

    tab(0).click();
    await settle();
    expect(fixture.componentInstance.value()).toBe('#123456');

    tab(1).click();
    await settle();
    expect(fixture.componentInstance.value()).toBe(reference);
  });

  it('forgets the other tab\'s value once a different value is bound from outside', async () => {
    await show('#123456');
    (document.querySelectorAll('.cp-mode .seg-option')[1] as HTMLButtonElement).click();
    await settle();

    fixture.componentInstance.value.set('{{ vars.primary | color | color_opacity: 50 }}');
    await settle();
    (document.querySelector('.cp-mode .seg-option') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('#3366ff80');
  });

  it('takes an eight-digit hex and emits it canonically when alpha is allowed', async () => {
    await show('#3366ff');

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
