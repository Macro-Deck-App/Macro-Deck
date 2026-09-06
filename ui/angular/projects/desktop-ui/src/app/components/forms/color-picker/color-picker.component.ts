import {
  ChangeDetectionStrategy,
  Component,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, OverlayPanelComponent } from '@shared';
import { Hsv, hexToHsv, hsvToHex, normalizeHex } from './color-conversion';

export interface ColorPreset {
  label: string;
  value: string;
}

const DEFAULT_PRESET_VALUES: { key: string; value: string }[] = [
  { key: AppStrings.Forms.ColorPicker.Red, value: '#ef4444' },
  { key: AppStrings.Forms.ColorPicker.Orange, value: '#f59e0b' },
  { key: AppStrings.Forms.ColorPicker.Yellow, value: '#eab308' },
  { key: AppStrings.Forms.ColorPicker.Green, value: '#22c55e' },
  { key: AppStrings.Forms.ColorPicker.Blue, value: '#3b82f6' },
  { key: AppStrings.Forms.ColorPicker.Purple, value: '#8b5cf6' },
  { key: AppStrings.Forms.ColorPicker.Pink, value: '#ec4899' },
];

const CUSTOM_FALLBACK = '#22c55e';
const CUSTOM_FALLBACK_HSV: Hsv = hexToHsv(CUSTOM_FALLBACK) ?? { h: 0, s: 0, v: 0 };

const COARSE_STEP = 0.1;
const FINE_STEP = 0.02;

@Component({
  selector: 'shared-color-picker',
  standalone: true,
  imports: [OverlayPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => ColorPickerComponent),
      multi: true,
    },
  ],
  templateUrl: './color-picker.component.html',
  styleUrls: ['./color-picker.component.scss'],
})
export class ColorPickerComponent implements ControlValueAccessor {
  private readonly localization = inject(LocalizationService);

  public readonly label = input('');
  public readonly presets = input<ColorPreset[] | undefined>(undefined);

  protected readonly effectivePresets = computed<ColorPreset[]>(() =>
    this.presets() ?? DEFAULT_PRESET_VALUES.map(preset => ({
      label: this.localization.translateKey(preset.key),
      value: preset.value,
    })));

  protected readonly pickerAriaLabel = computed(() =>
    this.label() || this.localization.translateKey(AppStrings.Forms.ColorPicker.ColorPickerLabel));

  protected readonly defaultColorTitle = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.DefaultColorTitle));

  protected readonly resetAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.ResetAriaLabel));

  protected readonly customColorTitle = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.CustomColorTitle));

  protected readonly areaAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.SaturationBrightnessLabel));

  protected readonly hueAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.HueLabel));

  protected readonly hexAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.HexLabel));

  protected readonly eyedropperLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.EyedropperLabel));

  protected readonly supportsEyedropper = eyedropperFactory() !== undefined;

  public readonly defaultColor = input<string | undefined>(undefined);
  public readonly resetValue = input<string | undefined>(undefined);
  public readonly allowCustom = input(true);

  public readonly value = signal('');
  public readonly lastCustomColor = signal(CUSTOM_FALLBACK);

  protected readonly isPickerOpen = signal(false);
  protected readonly hsv = signal<Hsv>(CUSTOM_FALLBACK_HSV);
  protected readonly hexText = signal(CUSTOM_FALLBACK);

  protected readonly hueColor = computed(() => hsvToHex({ h: this.hsv().h, s: 1, v: 1 }));
  protected readonly saturationPercent = computed(() => this.hsv().s * 100);
  protected readonly brightnessPercent = computed(() => this.hsv().v * 100);

  protected readonly effectiveResetValue = computed(() => this.resetValue() ?? this.defaultColor());

  protected readonly showsReset = computed(
    () => this.defaultColor() !== undefined || this.resetValue() !== undefined);

  public readonly isCustom = computed(() => {
    const current = this.value();
    if (!current) {
      return false;
    }

    if (this.effectiveResetValue() === current) {
      return false;
    }

    return !this.effectivePresets().some(preset => preset.value === current);
  });

  public readonly customFill = computed(() => {
    const current = this.value();
    return current.startsWith('#') ? current : this.lastCustomColor();
  });

  private _onChange: (value: string) => void = () => { };
  private _onTouched: () => void = () => { };

  public writeValue(value: string): void {
    this.value.set(value ?? '');
    if (value && value.startsWith('#')) {
      this.lastCustomColor.set(value);
    }
  }

  public registerOnChange(fn: (value: string) => void): void {
    this._onChange = fn;
  }

  public registerOnTouched(fn: () => void): void {
    this._onTouched = fn;
  }

  public select(color: string): void {
    this.value.set(color);
    if (color.startsWith('#')) {
      this.lastCustomColor.set(color);
    }

    this._onChange(color);
    this._onTouched();
  }

  protected togglePicker(): void {
    if (this.isPickerOpen()) {
      this.closePicker();
      return;
    }

    const start = hexToHsv(this.customFill()) ?? CUSTOM_FALLBACK_HSV;
    this.hsv.set(start);
    this.hexText.set(hsvToHex(start));
    this.isPickerOpen.set(true);
  }

  protected closePicker(): void {
    this.isPickerOpen.set(false);
  }

  protected onAreaPointerDown(event: PointerEvent): void {
    const area = event.currentTarget as HTMLElement;
    area.setPointerCapture(event.pointerId);
    area.focus();
    this.applyAreaPosition(event, area);
  }

  protected onAreaPointerMove(event: PointerEvent): void {
    const area = event.currentTarget as HTMLElement;
    if (!area.hasPointerCapture(event.pointerId)) {
      return;
    }

    this.applyAreaPosition(event, area);
  }

  protected onAreaKeydown(event: KeyboardEvent): void {
    const step = event.shiftKey ? COARSE_STEP : FINE_STEP;
    const { h, s, v } = this.hsv();

    switch (event.key) {
      case 'ArrowLeft':
        this.commitHsv({ h, s: s - step, v });
        break;
      case 'ArrowRight':
        this.commitHsv({ h, s: s + step, v });
        break;
      case 'ArrowUp':
        this.commitHsv({ h, s, v: v + step });
        break;
      case 'ArrowDown':
        this.commitHsv({ h, s, v: v - step });
        break;
      default:
        return;
    }

    event.preventDefault();
  }

  protected onHueInput(event: Event): void {
    this.commitHsv({ ...this.hsv(), h: Number((event.target as HTMLInputElement).value) });
  }

  protected onHexInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.hexText.set(text);

    const hex = normalizeHex(text);
    const typed = hex === null ? null : hexToHsv(hex);
    if (hex === null || typed === null) {
      return;
    }

    // Hue is undefined for greys and saturation is undefined for black, so a typed value that lands
    // on either would otherwise throw away the hue or saturation the user had already dialled in.
    const previous = this.hsv();
    this.hsv.set({
      h: typed.s === 0 ? previous.h : typed.h,
      s: typed.v === 0 ? previous.s : typed.s,
      v: typed.v,
    });
    this.select(hex);
  }

  protected onHexBlur(): void {
    this.hexText.set(hsvToHex(this.hsv()));
  }

  protected async pickFromScreen(): Promise<void> {
    const factory = eyedropperFactory();
    if (!factory) {
      return;
    }

    let picked: string;
    try {
      picked = (await new factory().open()).sRGBHex;
    } catch {
      // Rejects when the user dismisses the eyedropper, which is not an error worth reporting.
      return;
    }

    const hex = normalizeHex(picked);
    const hsv = hex === null ? null : hexToHsv(hex);
    if (hex === null || hsv === null) {
      return;
    }

    this.hsv.set(hsv);
    this.hexText.set(hex);
    this.select(hex);
  }

  private applyAreaPosition(event: PointerEvent, area: HTMLElement): void {
    const rect = area.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) {
      return;
    }

    this.commitHsv({
      h: this.hsv().h,
      s: (event.clientX - rect.left) / rect.width,
      v: 1 - (event.clientY - rect.top) / rect.height,
    });
  }

  private commitHsv(hsv: Hsv): void {
    const clamped: Hsv = {
      h: ((hsv.h % 360) + 360) % 360,
      s: clamp01(hsv.s),
      v: clamp01(hsv.v),
    };

    this.hsv.set(clamped);
    const hex = hsvToHex(clamped);
    this.hexText.set(hex);
    this.select(hex);
  }
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value));
}

interface ScreenEyeDropper {
  open(): Promise<{ sRGBHex: string }>;
}

// Chromium-only, so the affordance it backs is offered only where it exists.
function eyedropperFactory(): (new () => ScreenEyeDropper) | undefined {
  return (globalThis as unknown as { EyeDropper?: new () => ScreenEyeDropper }).EyeDropper;
}
