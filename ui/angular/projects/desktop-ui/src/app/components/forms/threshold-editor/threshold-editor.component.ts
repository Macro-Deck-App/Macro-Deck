import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
  MAX_THRESHOLD_BANDS,
  insertThresholdBoundary,
  thresholdBandIndexAt,
  moveThresholdBoundary,
  recolorThresholdBand,
  removeThresholdBand,
  snapThresholdValue,
  thresholdAxis,
  thresholdBandRange,
  thresholdStepDecimals,
  type ThresholdAxis,
  type ThresholdsValue,
} from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, TranslatePipe } from '@shared';
import { ColorPickerComponent, defaultColorPresets } from '../color-picker/color-picker.component';

const NEW_BAND_COLORS = ['#34c759', '#ffcc00', '#ff9500', '#ff3b30', '#0a84ff', '#af52de', '#5ac8fa', '#ff2d55'];

interface BandView {
  index: number;
  id: string;
  color: string;
  range: string;
  left: number;
  width: number;
}

interface HandleView {
  index: number;
  value: number;
  text: string;
  left: number;
  label: string;
}

@Component({
  selector: 'shared-threshold-editor',
  standalone: true,
  imports: [FormsModule, ButtonComponent, ColorPickerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './threshold-editor.component.html',
  styleUrls: ['./threshold-editor.component.scss'],
})
export class ThresholdEditorComponent {
  readonly value = input.required<ThresholdsValue>();
  readonly min = input<number | null>(null);
  readonly max = input<number | null>(null);
  readonly step = input<number | null>(null);
  readonly unit = input<string | null | undefined>(null);
  readonly label = input<string | null | undefined>(null);
  readonly disabled = input(false);
  readonly fixedCount = input(false);
  readonly fixedColors = input(false);
  readonly maxCount = input<number | null>(null);
  readonly canReset = input(false);

  readonly valueChange = output<ThresholdsValue>();
  readonly reset = output<void>();

  private readonly localization = inject(LocalizationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly track = viewChild<ElementRef<HTMLElement>>('track');

  protected readonly colorPresets = computed(() => defaultColorPresets(this.localization));
  protected readonly selected = signal(0);
  private readonly dragDraft = signal<ThresholdsValue | null>(null);
  private readonly dragAxis = signal<ThresholdAxis | null>(null);
  private readonly pending = signal<{ base: ThresholdsValue; value: ThresholdsValue } | null>(null);
  private dragIndex = -1;
  private swallowTrackClick = false;

  // A key held down repeats faster than the emitted value comes back as an input, so the last emitted
  // value stands in until the input changes.
  private readonly settled = computed(() => {
    const pending = this.pending();
    return pending && pending.base === this.value() ? pending.value : this.value();
  });
  protected readonly current = computed(() => this.dragDraft() ?? this.settled());
  protected readonly axis = computed<ThresholdAxis>(
    () => this.dragAxis() ?? thresholdAxis(this.settled(), this.min() ?? 0, this.max() ?? 100),
  );
  private readonly span = computed(() => Math.max(this.axis().end - this.axis().start, Number.EPSILON));
  protected readonly effectiveStep = computed(() => {
    const step = this.step();
    return step && step > 0 ? step : 10 ** Math.floor(Math.log10(this.span() / 100));
  });
  protected readonly gap = computed(() => this.effectiveStep());

  protected readonly selectedIndex = computed(() => Math.min(this.selected(), this.current().bands.length - 1));
  protected readonly selectedBand = computed(() => this.current().bands[this.selectedIndex()]);
  protected readonly canInsert = computed(
    () =>
      !this.disabled() &&
      !this.fixedCount() &&
      !this.fixedColors() &&
      this.current().bands.length < Math.min(this.maxCount() ?? MAX_THRESHOLD_BANDS, MAX_THRESHOLD_BANDS),
  );
  protected readonly showRemove = computed(() => !this.fixedCount());
  protected readonly rangeShortcuts = computed(() => {
    const keys = [this.canInsert() ? 'Plus' : null, this.showRemove() && !this.disabled() ? 'Delete' : null];
    return keys.filter(Boolean).join(' ') || null;
  });
  protected readonly canRemove = computed(() => !this.disabled() && this.current().bands.length > 1);

  protected readonly bands = computed<BandView[]>(() => {
    const value = this.current();
    const axis = this.axis();
    return value.bands.map((band, index) => {
      const { from, to } = thresholdBandRange(value, index, axis);
      return {
        index,
        id: band.id,
        color: band.color,
        range: this.rangeText(from, to),
        left: this.percent(from),
        width: Math.max(this.percent(to) - this.percent(from), 0),
      };
    });
  });

  protected readonly handles = computed<HandleView[]>(() =>
    this.current()
      .bands.slice(1)
      .map((band, offset) => {
        const value = band.from as number;
        return {
          index: offset + 1,
          value,
          text: this.withUnit(this.format(value)),
          left: this.percent(value),
          label: this.localization.translateKey(AppStrings.Forms.ThresholdEditor.Threshold, { n: offset + 1 }),
        };
      }),
  );

  protected readonly axisLabels = computed(() => {
    const { start, end } = this.axis();
    return [start, (start + end) / 2, end].map(value => this.format(value));
  });

  protected select(index: number): void {
    this.selected.set(index);
  }

  protected onHandlePointerDown(event: PointerEvent, index: number): void {
    if (this.disabled() || event.button !== 0) return;
    event.preventDefault();
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
    (event.currentTarget as HTMLElement).focus();
    this.dragIndex = index;
    this.swallowTrackClick = true;
    this.dragAxis.set(this.axis());
    this.select(index);
  }

  protected onHandlePointerMove(event: PointerEvent): void {
    if (this.dragIndex < 0) return;
    const track = this.track()?.nativeElement;
    if (!track) return;
    const rect = track.getBoundingClientRect();
    if (rect.width <= 0) return;
    const axis = this.axis();
    const raw = axis.start + ((event.clientX - rect.left) / rect.width) * this.span();
    const snapped = this.clampToAxis(snapThresholdValue(raw, this.effectiveStep(), axis.start));
    this.dragDraft.set(moveThresholdBoundary(this.current(), this.dragIndex, snapped, this.gap()));
  }

  protected onHandlePointerUp(): void {
    if (this.dragIndex < 0) return;
    this.dragIndex = -1;
    const draft = this.dragDraft();
    this.dragDraft.set(null);
    this.dragAxis.set(null);
    if (draft) this.emit(draft);
  }

  protected onHandleKeydown(event: KeyboardEvent, index: number): void {
    if (this.disabled()) return;
    const band = this.current().bands[index];
    if (band?.from === undefined) return;
    const step = this.gap();
    const large = Math.max(step * 10, this.span() / 10);
    const { start, end } = this.axis();
    const targets: Record<string, number> = {
      ArrowLeft: band.from - step,
      ArrowDown: band.from - step,
      ArrowRight: band.from + step,
      ArrowUp: band.from + step,
      PageDown: band.from - large,
      PageUp: band.from + large,
      Home: start,
      End: end,
    };
    const target = targets[event.key];
    if (target === undefined) return;
    event.preventDefault();
    this.select(index);
    this.emitMove(index, this.clampToAxis(snapThresholdValue(target, this.effectiveStep(), start)));
  }

  protected onColorChange(color: string): void {
    if (this.fixedColors()) return;
    this.emit(recolorThresholdBand(this.current(), this.selectedIndex(), color));
  }

  protected onTrackPointerDown(event: PointerEvent): void {
    if (!(event.target as HTMLElement).classList.contains('te-handle')) this.swallowTrackClick = false;
  }

  // WebKit can deliver the click that ends a captured handle drag to the track, so the first click after a
  // handle press is never an insert.
  protected onTrackClick(event: MouseEvent): void {
    if (this.swallowTrackClick) {
      this.swallowTrackClick = false;
      return;
    }
    const track = this.track()?.nativeElement;
    if (!track || (event.target as HTMLElement).classList.contains('te-handle')) return;
    const rect = track.getBoundingClientRect();
    if (rect.width <= 0) return;
    const axis = this.axis();
    this.insertAt(snapThresholdValue(axis.start + ((event.clientX - rect.left) / rect.width) * this.span(), this.effectiveStep(), axis.start));
  }

  protected onRangeKeydown(event: KeyboardEvent, index: number): void {
    if (event.key === '+') {
      event.preventDefault();
      const { from, to } = thresholdBandRange(this.current(), index, this.axis());
      this.insertAt(snapThresholdValue((from + to) / 2, this.effectiveStep(), this.axis().start));
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      this.select(index);
      this.onRemove();
    }
  }

  private insertAt(at: number): void {
    if (!this.canInsert()) return;
    const value = this.current();
    const used = new Set(value.bands.map(band => band.color));
    const color = NEW_BAND_COLORS.find(candidate => !used.has(candidate)) ?? value.bands[thresholdBandIndexAt(value, at)].color;
    let n = value.bands.length + 1;
    while (value.bands.some(band => band.id === `band-${n}`)) n++;
    const inserted = insertThresholdBoundary(value, this.clampToAxis(at), this.gap(), { id: `band-${n}`, color }, this.maxCount());
    if (!inserted) return;
    this.select(inserted.bands.findIndex(band => band.id === `band-${n}`));
    this.emit(inserted);
  }

  protected onRemove(): void {
    if (!this.canRemove() || this.fixedCount()) return;
    const removed = removeThresholdBand(this.current(), this.selectedIndex());
    if (!removed) return;
    this.select(Math.max(this.selectedIndex() - 1, 0));
    this.emit(removed);
    afterNextRender(
      () => this.host.nativeElement.querySelectorAll<HTMLElement>('.te-range-select')[this.selectedIndex()]?.focus(),
      { injector: this.injector },
    );
  }

  protected onReset(): void {
    if (this.disabled()) return;
    this.reset.emit();
  }

  protected format(value: number): string {
    const digits = Math.min(thresholdStepDecimals(this.effectiveStep()), 20);
    return new Intl.NumberFormat(this.localization.culture(), { maximumFractionDigits: digits }).format(value);
  }

  private clampToAxis(value: number): number {
    const { start, end } = this.axis();
    return Math.min(Math.max(value, start), end);
  }

  private emitMove(index: number, to: number): void {
    this.emit(moveThresholdBoundary(this.current(), index, to, this.gap()));
  }

  private emit(value: ThresholdsValue): void {
    if (this.disabled() || value === this.current()) return;
    this.pending.set({ base: this.value(), value });
    this.valueChange.emit(value);
  }

  private rangeText(from: number, to: number): string {
    const unit = this.unit();
    return unit
      ? this.localization.translateKey(AppStrings.Forms.ThresholdEditor.RangeWithUnit, {
          from: this.format(from),
          to: this.format(to),
          unit,
        })
      : this.localization.translateKey(AppStrings.Forms.ThresholdEditor.Range, {
          from: this.format(from),
          to: this.format(to),
        });
  }

  private withUnit(value: string): string {
    const unit = this.unit();
    return unit ? this.localization.translateKey(AppStrings.Variables.Format.ValueWithUnit, { value, unit }) : value;
  }

  private percent(value: number): number {
    return ((value - this.axis().start) / this.span()) * 100;
  }
}
