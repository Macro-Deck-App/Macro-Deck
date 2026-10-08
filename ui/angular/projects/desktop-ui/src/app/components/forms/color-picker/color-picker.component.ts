import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  forwardRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import {
  AppStrings,
  canonicalColor,
  COLOR_MODIFIER_OPS,
  MAX_COLOR_MODIFIERS,
  type ColorModifier,
  type ColorModifierOp,
  type ColorReference,
  formatColor,
  isUnsupportedColorTemplate,
  parseColor,
  parseColorReference,
  resolveColorReference,
  serializeColorReference,
  type Variable,
} from '@macro-deck/runtime';
import {
  ColorPaletteService,
  InputComponent,
  TranslatePipe,
  MAX_PALETTE_COLORS,
  LocalizationService,
  OverlayPanelComponent,
  SegmentedControlComponent,
  type SegmentedOption,
  VariableService,
} from '@shared';
import { ColorPickerResultComponent } from './color-picker-result.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { SelectComponent, type SelectOption } from '../select/select.component';
import { ColorPickerChipComponent } from './color-picker-chip.component';
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

export const TRANSPARENT_COLOR = 'transparent';

export function paletteColorLabel(color: string, localization: LocalizationService): string {
  const named = DEFAULT_PRESET_VALUES.find(preset => preset.value === color.toLowerCase());
  return named
    ? localization.translateKey(named.key)
    : localization.translateKey(AppStrings.Forms.ColorPicker.CustomSwatch, { hex: color });
}

export function colorVariablesInScope(variables: readonly Variable[], scopeRefId: string | undefined): Variable[] {
  const colors = variables.filter(variable => variable.type === 'color');
  const locals = scopeRefId
    ? colors.filter(variable => variable.scope === 'widget' && variable.scopeRefId === scopeRefId)
    : [];
  const localNames = new Set(locals.map(variable => variable.name));
  const globals = colors.filter(variable => variable.scope === 'global' && !localNames.has(variable.name));
  return [...locals, ...globals].sort((a, b) => a.name.localeCompare(b.name));
}

export function displayColor(value: string, colorVariables: readonly Variable[]): string | null {
  const reference = parseColorReference(value);
  if (!reference) return value;
  const values = new Map(colorVariables.map(variable => [variable.name, variable.value]));
  return resolveColorReference(reference, name => values.get(name));
}

const CUSTOM_FALLBACK = '#22c55e';
const CUSTOM_FALLBACK_HSV: Hsv = hexToHsv(CUSTOM_FALLBACK) ?? { h: 0, s: 0, v: 0 };

const COARSE_STEP = 0.1;
const FINE_STEP = 0.02;

type ColorMode = 'static' | 'variable';

const MIX_LITERAL_OPTION = '#';
const DEFAULT_MIX_COLOR = '#ffffff';

const MODIFIER_LABEL_KEYS: Record<ColorModifierOp, string> = {
  lighten: AppStrings.Forms.ColorPicker.Modifier.Lighten,
  darken: AppStrings.Forms.ColorPicker.Modifier.Darken,
  opacity: AppStrings.Forms.ColorPicker.Modifier.Opacity,
  increase_opacity: AppStrings.Forms.ColorPicker.Modifier.IncreaseOpacity,
  reduce_opacity: AppStrings.Forms.ColorPicker.Modifier.ReduceOpacity,
  saturate: AppStrings.Forms.ColorPicker.Modifier.Saturate,
  desaturate: AppStrings.Forms.ColorPicker.Modifier.Desaturate,
  hue: AppStrings.Forms.ColorPicker.Modifier.Hue,
  mix: AppStrings.Forms.ColorPicker.Modifier.Mix,
};

const MODIFIER_ICONS: Record<ColorModifierOp, string> = {
  lighten: 'sun',
  darken: 'sun-dim',
  opacity: 'droplet',
  increase_opacity: 'droplets',
  reduce_opacity: 'droplet-off',
  saturate: 'contrast',
  desaturate: 'contrast',
  hue: 'rotate-cw',
  mix: 'blend',
};

export interface ModifierStepView {
  modifier: ColorModifier;
  icon: string;
  unit: string;
  min: number;
  max: number;
  summary: string;
}

const DEFAULT_AMOUNTS: Record<ColorModifierOp, number> = {
  lighten: 20,
  darken: 20,
  opacity: 70,
  increase_opacity: 20,
  reduce_opacity: 20,
  saturate: 20,
  desaturate: 20,
  hue: 30,
  mix: 50,
};

@Component({
  selector: 'shared-color-picker',
  standalone: true,
  imports: [OverlayPanelComponent, SegmentedControlComponent, SelectComponent, ColorPickerChipComponent, InputComponent, ColorPickerResultComponent, ConfirmationModalComponent, FormsModule, NgTemplateOutlet, TranslatePipe],
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
  private readonly injector = inject(Injector);
  private readonly palette = inject(ColorPaletteService);

  public readonly label = input('');
  public readonly presets = input<ColorPreset[] | undefined>(undefined);

  protected readonly effectivePresets = computed<ColorPreset[]>(() => this.presets() ?? []);

  protected readonly transparentColor = TRANSPARENT_COLOR;

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
  public readonly allowAlpha = input(false);
  public readonly allowVariables = input(false);
  public readonly variableScopeRefId = input<string | undefined>(undefined);
  public readonly resetPlacement = input<'inline' | 'none'>('inline');
  public readonly disabled = input(false);
  public readonly inline = input(false);
  public readonly showPalette = input(true);

  public readonly value = signal('');
  public readonly lastCustomColor = signal(CUSTOM_FALLBACK);

  protected readonly isOpen = signal(false);
  private lastStatic: string | null = null;
  private lastReference: string | null = null;
  protected readonly pendingRemoval = signal<string | null>(null);
  protected readonly popoverMaxHeight = signal<number | null>(null);
  private readonly disabledByForm = signal(false);
  protected readonly isDisabled = computed(() => this.disabled() || this.disabledByForm());
  private readonly chip = viewChild(ColorPickerChipComponent);
  private readonly popover = viewChild<ElementRef<HTMLElement>>('popover');
  protected readonly hsv = signal<Hsv>(CUSTOM_FALLBACK_HSV);
  protected readonly hexText = signal(CUSTOM_FALLBACK);
  protected readonly alpha = signal(255);
  protected readonly alphaPercent = computed(() => Math.round((this.alpha() / 255) * 100));
  protected readonly hexMaxLength = computed(() => (this.allowAlpha() ? 9 : 7));

  protected readonly hueColor = computed(() => hsvToHex({ h: this.hsv().h, s: 1, v: 1 }));
  protected readonly opaqueColor = computed(() => hsvToHex(this.hsv()));
  protected readonly saturationPercent = computed(() => this.hsv().s * 100);
  protected readonly brightnessPercent = computed(() => this.hsv().v * 100);

  protected readonly effectiveResetValue = computed(() => this.resetValue() ?? this.defaultColor());

  protected readonly showsReset = computed(
    () => this.defaultColor() !== undefined || this.resetValue() !== undefined);

  public readonly customFill = computed(() => {
    const current = this.value();
    return current.startsWith('#') ? current : this.lastCustomColor();
  });

  private readonly modeOverride = signal<ColorMode | null>(null);

  protected readonly reference = computed<ColorReference | null>(() => parseColorReference(this.value()));

  protected readonly unsupported = computed(() => isUnsupportedColorTemplate(this.value()));

  protected readonly mode = computed<ColorMode>(() =>
    this.modeOverride() ?? (this.reference() ? 'variable' : 'static'));

  protected readonly showsModeToggle = computed(() => this.allowVariables() && !this.unsupported());

  protected readonly modeOptions = computed<SegmentedOption[]>(() => [
    { value: 'static', label: this.localization.translateKey(AppStrings.Forms.ColorPicker.ModeStatic) },
    { value: 'variable', label: this.localization.translateKey(AppStrings.Forms.ColorPicker.ModeVariable) },
  ]);

  protected readonly modeAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.ModeLabel));

  protected readonly alphaAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.AlphaLabel));

  private readonly variableService = computed<VariableService | null>(() =>
    this.allowVariables() ? this.injector.get(VariableService) : null);

  protected readonly colorVariables = computed<Variable[]>(() => {
    const service = this.variableService();
    return service ? colorVariablesInScope(service.variables(), this.variableScopeRefId()) : [];
  });

  protected readonly variableOptions = computed<SelectOption[]>(() =>
    this.withMissing(this.colorVariableOptions(), this.reference()?.variable));

  private readonly colorVariableOptions = computed<SelectOption[]>(() =>
    this.colorVariables().map(variable => ({
      value: variable.name,
      label: variable.name,
      swatch: canonicalColor(variable.value),
    })));

  protected readonly modifierOptions = computed<SelectOption[]>(() =>
    COLOR_MODIFIER_OPS.map(op => ({
      value: op,
      label: this.localization.translateKey(MODIFIER_LABEL_KEYS[op]),
      icon: MODIFIER_ICONS[op],
    })));

  protected readonly modifierCount = computed(() => this.reference()?.modifiers.length ?? 0);

  protected readonly modifierCountText = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.ModifierCount, { count: this.modifierCount() }));

  protected readonly chipLabel = computed(() =>
    this.missingVariableText() ?? this.reference()?.variable ?? this.variablePlaceholder());

  protected readonly canAddModifier = computed(() => (this.reference()?.modifiers.length ?? 0) < MAX_COLOR_MODIFIERS);

  protected readonly steps = computed<ModifierStepView[]>(() =>
    (this.reference()?.modifiers ?? []).map(modifier => {
      const unit = modifier.op === 'hue' ? '\u00b0' : '%';
      return {
        modifier,
        icon: MODIFIER_ICONS[modifier.op],
        unit,
        min: modifier.op === 'hue' ? -360 : 0,
        max: modifier.op === 'hue' ? 360 : 100,
        summary: this.localization.translateKey(AppStrings.Forms.ColorPicker.ModifierSummary, {
          modifier: this.localization.translateKey(MODIFIER_LABEL_KEYS[modifier.op]),
          amount: `${this.formatAmount(modifier.amount)}${unit}`,
        }),
      };
    }));

  protected readonly originalColor = computed<string | null>(() => {
    const name = this.reference()?.variable;
    const variable = name ? this.colorVariables().find(candidate => candidate.name === name) : undefined;
    return variable ? canonicalColor(variable.value) : null;
  });

  protected readonly searchVariablesPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.SearchVariablesPlaceholder));

  protected readonly noMatchingVariablesText = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.NoMatchingVariables));

  protected readonly originalLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.OriginalLabel));

  protected readonly resultLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.ResultLabel));

  protected readonly variableLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.VariableLabel));

  protected readonly variablePlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.VariablePlaceholder));

  protected readonly noColorVariablesText = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.NoColorVariables));

  protected readonly unsupportedText = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.UnsupportedReference));

  protected readonly addModifierLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.AddModifier));

  protected readonly removeModifierLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.RemoveModifierAriaLabel));

  protected readonly modifierLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.ModifierLabel));

  protected readonly amountLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.AmountLabel));

  protected readonly mixWithLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.MixWithLabel));

  protected readonly missingVariableText = computed(() => {
    const name = this.reference()?.variable;
    if (!name || this.colorVariables().some(variable => variable.name === name)) return null;
    return this.localization.translateKey(AppStrings.Forms.ColorPicker.MissingVariable, { name });
  });

  protected readonly previewColor = computed<string | null>(() =>
    this.reference() ? displayColor(this.value(), this.colorVariables()) : null);

  protected readonly showsInlineReset = computed(() =>
    this.resetPlacement() === 'inline' && this.showsReset() && this.value() !== this.effectiveResetValue());

  private readonly isSentinel = computed(() => {
    const reset = this.resetValue();
    return !!reset && this.value() === reset && !reset.startsWith('#') && reset !== TRANSPARENT_COLOR;
  });

  private readonly shownLiteral = computed<string | null>(() => {
    const current = this.value();
    if (this.reference() || this.isSentinel()) return null;
    if (current) return current;
    const fallback = this.defaultColor();
    return fallback ? fallback : null;
  });

  protected readonly fieldShowsResetGlyph = computed(() =>
    !this.reference() && this.shownLiteral() === null);

  protected readonly fieldIsPlaceholder = computed(() => this.fieldShowsResetGlyph());

  protected readonly fieldSwatch = computed<string | null>(() => {
    if (this.reference()) return this.previewColor();
    const literal = this.shownLiteral();
    return literal && (literal === TRANSPARENT_COLOR || parseColor(literal)) ? literal : null;
  });

  protected readonly fieldText = computed(() => {
    if (this.reference()) return this.chipLabel();
    if (this.isSentinel()) return this.defaultColorTitle();
    const literal = this.shownLiteral();
    if (literal === null) return this.localization.translateKey(AppStrings.Forms.ColorPicker.NotSet);
    if (literal === TRANSPARENT_COLOR) {
      return this.effectivePresets().find(preset => preset.value === TRANSPARENT_COLOR)?.label
        ?? this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorTransparent);
    }
    return literal.startsWith('#') ? literal.slice(1) : literal;
  });

  protected readonly paletteLabel = computed(() =>
    this.localization.translateKey(AppStrings.Forms.ColorPicker.PaletteLabel));

  protected readonly paletteColors = computed<string[]>(() => {
    const presets = new Set(this.effectivePresets().map(preset => preset.value.toLowerCase()));
    return this.palette.colors().filter(color =>
      !presets.has(color) && (this.allowAlpha() || !hasAlpha(color)));
  });

  protected readonly canAddToPalette = computed(() => {
    const current = this.value().toLowerCase();
    if (!current.startsWith('#') || !parseColor(current) || this.palette.isFull()) return false;
    if (this.effectivePresets().some(preset => preset.value.toLowerCase() === current)) return false;
    return !this.palette.contains(current);
  });

  protected readonly addToPaletteTitle = computed(() =>
    this.palette.isFull()
      ? this.localization.translateKey(AppStrings.Errors.ColorPalette.Full, { max: MAX_PALETTE_COLORS })
      : this.localization.translateKey(AppStrings.Forms.ColorPicker.AddToPalette));

  private _onChange: (value: string) => void = () => { };
  private _onTouched: () => void = () => { };

  public writeValue(value: string): void {
    if ((value ?? '') !== this.value()) {
      this.lastStatic = null;
      this.lastReference = null;
    }
    this.value.set(value ?? '');
    this.remember(value ?? '');
    this.modeOverride.set(null);
    if (value && value.startsWith('#')) {
      this.lastCustomColor.set(value);
    }
    if (this.isOpen() || this.inline()) this.syncFromValue();
  }

  public setDisabledState(isDisabled: boolean): void {
    this.disabledByForm.set(isDisabled);
    if (isDisabled) this.isOpen.set(false);
  }

  public registerOnChange(fn: (value: string) => void): void {
    this._onChange = fn;
  }

  public registerOnTouched(fn: () => void): void {
    this._onTouched = fn;
  }

  public select(color: string): void {
    this.applySelection(color);
    if (this.mode() === 'static') this.syncFromValue();
  }

  public reset(): void {
    const value = this.effectiveResetValue();
    if (value === undefined) return;
    this.modeOverride.set(null);
    this.select(value);
  }

  private applySelection(color: string): void {
    this.value.set(color);
    this.remember(color);
    if (color.startsWith('#')) {
      this.lastCustomColor.set(color);
    }

    this._onChange(color);
    this._onTouched();
  }

  protected customSwatchLabel(color: string): string {
    return paletteColorLabel(color, this.localization);
  }

  protected removeFromPaletteLabel(color: string): string {
    return this.localization.translateKey(AppStrings.Forms.ColorPicker.RemoveFromPalette, { hex: color });
  }

  protected addToPalette(): void {
    if (this.canAddToPalette()) void this.palette.add(this.value());
  }

  protected removeFromPalette(color: string, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.pendingRemoval.set(color);
  }

  protected confirmRemoval(): void {
    const color = this.pendingRemoval();
    this.cancelRemoval();
    if (color) void this.palette.remove(color);
  }

  protected cancelRemoval(): void {
    this.pendingRemoval.set(null);
    requestAnimationFrame(() =>
      this.popover()?.nativeElement.querySelector<HTMLElement>('.cp-palette button:not([disabled])')?.focus());
  }

  protected removalMessage(color: string): string {
    return this.localization.translateKey(AppStrings.Forms.ColorPicker.RemoveFromPaletteConfirmMessage,
      { name: paletteColorLabel(color, this.localization) });
  }

  protected setMode(mode: string): void {
    if (mode === this.mode()) return;
    if (mode === 'variable') {
      this.modeOverride.set('variable');
      const first = this.colorVariables()[0];
      if (this.lastReference) this.applySelection(this.lastReference);
      else if (first) this.emitReference({ variable: first.name, modifiers: [] });
      return;
    }

    const snapshot = this.previewColor();
    this.modeOverride.set('static');
    this.select(this.lastStatic ?? snapshot ?? this.effectiveResetValue() ?? '');
    this.modeOverride.set(null);
  }

  private remember(value: string): void {
    if (parseColorReference(value)) this.lastReference = value;
    else if (!isUnsupportedColorTemplate(value)) this.lastStatic = value;
  }

  protected toggle(): void {
    if (this.isOpen()) this.close();
    else this.open();
  }

  // A select or colour popover inside this one is its own overlay; a click or Escape meant for it must not close this one.
  protected onPopoverDismissed(): void {
    const popover = this.popover()?.nativeElement;
    if (this.pendingRemoval() || (popover && OverlayPanelComponent.isAnyOpenWithin(popover))) return;
    this.close();
  }

  protected onPopoverKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab' || !this.isOpen()) return;
    const focusable = Array.from(this.popover()?.nativeElement.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])') ?? [])
      .filter(element => element.getClientRects().length > 0);
    const edge = event.shiftKey ? focusable[0] : focusable[focusable.length - 1];
    if (edge && document.activeElement === edge) {
      event.preventDefault();
      this.close();
    }
  }

  private open(): void {
    if (this.isOpen() || this.isDisabled()) return;
    this.syncFromValue();
    this.popoverMaxHeight.set(Math.max(window.innerHeight - 32, 240));
    this.isOpen.set(true);
    requestAnimationFrame(() => this.popover()?.nativeElement.querySelector<HTMLElement>('button, input')?.focus());
  }

  private close(): void {
    if (!this.isOpen()) return;
    this.isOpen.set(false);
    this.chip()?.focus();
  }

  protected resetUnsupported(): void {
    this.select(this.effectiveResetValue() ?? '');
  }

  protected pickVariable(name: string): void {
    if (!name) return;
    this.emitReference({ variable: name, modifiers: this.reference()?.modifiers ?? [] });
  }

  protected addModifier(): void {
    const reference = this.reference();
    if (!reference || !this.canAddModifier()) return;
    this.emitReference({ ...reference, modifiers: [...reference.modifiers, { op: 'lighten', amount: DEFAULT_AMOUNTS.lighten }] });
  }

  protected removeModifier(index: number): void {
    const reference = this.reference();
    if (!reference) return;
    this.emitReference({ ...reference, modifiers: reference.modifiers.filter((_, i) => i !== index) });
  }

  protected changeModifierOp(index: number, op: string): void {
    const next = op as ColorModifierOp;
    this.updateModifier(index, current => {
      if (current.op === next) return current;
      if (next === 'mix') return { op: 'mix', amount: DEFAULT_AMOUNTS.mix, mix: { kind: 'color', color: DEFAULT_MIX_COLOR } };
      return { op: next, amount: current.op === 'mix' ? DEFAULT_AMOUNTS[next] : current.amount };
    });
  }

  protected setModifierAmount(index: number, raw: number | string | null): void {
    const current = this.reference()?.modifiers[index];
    const amount = typeof raw === 'number' ? raw : Number(raw);
    if (!current || raw === null || raw === '' || !Number.isFinite(amount)) return;
    const clamped = clampAmount(current.op, amount);
    if (clamped !== current.amount) this.updateModifier(index, modifier => ({ ...modifier, amount: clamped }));
  }

  protected changeMixTarget(index: number, target: string): void {
    this.updateModifier(index, current => ({
      ...current,
      mix: target === MIX_LITERAL_OPTION
        ? { kind: 'color', color: current.mix?.kind === 'color' ? current.mix.color : DEFAULT_MIX_COLOR }
        : { kind: 'variable', variable: target },
    }));
  }

  protected changeMixColor(index: number, color: string): void {
    if (!parseColor(color)) return;
    this.updateModifier(index, current => ({ ...current, mix: { kind: 'color', color } }));
  }

  protected mixTargetValue(modifier: ColorModifier): string {
    return modifier.mix?.kind === 'variable' ? modifier.mix.variable : MIX_LITERAL_OPTION;
  }

  protected mixTargetOptionsFor(modifier: ColorModifier): SelectOption[] {
    const literal: SelectOption = {
      value: MIX_LITERAL_OPTION,
      label: this.localization.translateKey(AppStrings.Forms.ColorPicker.ModeStatic),
      swatch: canonicalColor(this.mixLiteral(modifier)),
    };
    return this.withMissing([literal, ...this.colorVariableOptions()],
      modifier.mix?.kind === 'variable' ? modifier.mix.variable : undefined);
  }

  protected mixLiteral(modifier: ColorModifier): string {
    return modifier.mix?.kind === 'color' ? modifier.mix.color : DEFAULT_MIX_COLOR;
  }

  private formatAmount(amount: number): string {
    return new Intl.NumberFormat(this.localization.culture(), { maximumFractionDigits: 3 }).format(amount);
  }

  private updateModifier(index: number, change: (current: ColorModifier) => ColorModifier): void {
    const reference = this.reference();
    if (!reference || !reference.modifiers[index]) return;
    this.emitReference({
      ...reference,
      modifiers: reference.modifiers.map((modifier, i) => (i === index ? change(modifier) : modifier)),
    });
  }

  private emitReference(reference: ColorReference): void {
    const text = serializeColorReference(reference);
    if (text !== null) this.applySelection(text);
  }

  private withMissing(options: SelectOption[], name: string | undefined): SelectOption[] {
    if (!name || options.some(option => option.value === name)) return options;
    return [...options, {
      value: name,
      label: this.localization.translateKey(AppStrings.Forms.ColorPicker.MissingVariable, { name }),
      swatch: null,
    }];
  }

  private syncFromValue(): void {
    const fill = parseColor(this.customFill());
    const opaqueFill = fill ? formatColor({ ...fill, a: 255 }) : this.customFill();
    const start = hexToHsv(opaqueFill) ?? CUSTOM_FALLBACK_HSV;
    const previous = this.hsv();
    this.hsv.set({
      h: start.s === 0 ? previous.h : start.h,
      s: start.v === 0 ? previous.s : start.s,
      v: start.v,
    });
    this.alpha.set(this.allowAlpha() && fill ? fill.a : 255);
    this.hexText.set(this.composeHex(hsvToHex(start)));
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

  protected onAlphaInput(event: Event): void {
    const percent = Number((event.target as HTMLInputElement).value);
    if (!Number.isFinite(percent)) return;
    this.alpha.set(Math.min(255, Math.max(0, Math.round((percent / 100) * 255))));
    const hex = this.composeHex(hsvToHex(this.hsv()));
    this.hexText.set(hex);
    this.applySelection(hex);
  }

  protected onHexInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.hexText.set(text);

    if (this.allowAlpha() && this.applyAlphaHex(text)) {
      return;
    }

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
    this.alpha.set(255);
    this.applySelection(hex);
  }

  protected onHexBlur(): void {
    this.hexText.set(this.composeHex(hsvToHex(this.hsv())));
  }

  private applyAlphaHex(text: string): boolean {
    const trimmed = text.trim();
    if (!/^#?([0-9a-f]{4}|[0-9a-f]{8})$/i.test(trimmed)) return false;
    const color = parseColor(trimmed.startsWith('#') ? trimmed : `#${trimmed}`);
    if (!color) return false;
    const typed = hexToHsv(formatColor({ ...color, a: 255 }));
    if (!typed) return false;
    const previous = this.hsv();
    this.hsv.set({
      h: typed.s === 0 ? previous.h : typed.h,
      s: typed.v === 0 ? previous.s : typed.s,
      v: typed.v,
    });
    this.alpha.set(color.a);
    this.applySelection(formatColor(color));
    return true;
  }

  private composeHex(opaqueHex: string): string {
    const color = parseColor(opaqueHex);
    if (!color || !this.allowAlpha()) return opaqueHex;
    return formatColor({ ...color, a: this.alpha() });
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
    const withAlpha = this.composeHex(hex);
    this.hexText.set(withAlpha);
    this.applySelection(withAlpha);
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
    const hex = this.composeHex(hsvToHex(clamped));
    this.hexText.set(hex);
    this.applySelection(hex);
  }
}

function clampAmount(op: ColorModifierOp, amount: number): number {
  const [min, max] = op === 'hue' ? [-360, 360] : [0, 100];
  return Math.min(max, Math.max(min, amount));
}

function hasAlpha(color: string): boolean {
  const parsed = parseColor(color);
  return !!parsed && parsed.a < 255;
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
