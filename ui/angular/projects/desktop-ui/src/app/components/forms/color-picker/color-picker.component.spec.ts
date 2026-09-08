import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
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
