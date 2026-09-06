import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, DEFAULT_ICON_DISPLAY, ICON_DISPLAY_LIMITS, WidgetIconDisplay, WidgetIconFit, iconDisplayStyle, resolveIconDisplay } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, TranslatePipe } from '@shared';

export type WidgetIconDisplayField = 'fit' | 'zoom' | 'offsetX' | 'offsetY' | 'opacity';

type NumericField = Exclude<WidgetIconDisplayField, 'fit'>;

const WHEEL_ZOOM_STEP = 10;

@Component({
  selector: 'shared-widget-icon-display-control',
  standalone: true,
  imports: [FormsModule, ButtonComponent, InputComponent, SegmentedControlComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-icon-display-control.component.html',
  styleUrls: ['./widget-icon-display-control.component.scss'],
})
export class WidgetIconDisplayControlComponent implements OnChanges {
  @Input() display: WidgetIconDisplay | undefined;
  @Input() actionMode = false;
  @Input() aspectRatio = 1;
  @Input() previewBackground: string | undefined;
  @Input() iconUrl: string | null = null;
  @Input() resetActive = false;

  @Output() readonly displayChange = new EventEmitter<WidgetIconDisplay>();
  @Output() readonly resetRequested = new EventEmitter<void>();

  @ViewChild('frame') private frame?: ElementRef<HTMLElement>;

  private readonly localization = inject(LocalizationService);

  protected readonly limits = ICON_DISPLAY_LIMITS;

  protected get dragHint(): string {
    return this.localization.translateKey(AppStrings.Widgets.Appearance.IconDisplay.DragHint);
  }
  protected get displayModeAriaLabel(): string {
    return this.localization.translateKey(AppStrings.Widgets.Appearance.IconDisplay.DisplayModeAriaLabel);
  }
  protected get unchangedPlaceholder(): string {
    return this.localization.translateKey(AppStrings.Widgets.Appearance.IconDisplay.Unchanged);
  }
  protected get resetTooltip(): string {
    return this.resetActive
      ? this.localization.translateKey(AppStrings.Widgets.Appearance.IconDisplay.ResetTooltip)
      : '';
  }
  protected get resetButtonLabel(): string {
    return this.localization.translateKey(
      this.actionMode
        ? AppStrings.Widgets.Appearance.IconDisplay.ResetFraming
        : AppStrings.Widgets.Appearance.IconDisplay.Reset,
    );
  }

  private dragPointerId: number | null = null;
  private dragOrigin = { x: 0, y: 0, offsetX: 0, offsetY: 0 };
  private pending: WidgetIconDisplay | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['display']) {
      this.pending = null;
    }
  }

  private get current(): WidgetIconDisplay | undefined {
    return this.pending ?? this.display;
  }

  protected get fitOptions(): SegmentedOption[] {
    const S = AppStrings.Widgets.Appearance.IconDisplay;
    const options: SegmentedOption[] = [
      { value: 'contain', label: this.localization.translateKey(S.Contain) },
      { value: 'cover', label: this.localization.translateKey(S.Cover) },
    ];
    return this.actionMode
      ? [{ value: '', label: this.localization.translateKey(S.Unchanged) }, ...options]
      : options;
  }

  protected get fitValue(): string {
    if (this.actionMode) {
      return this.current?.fit ?? '';
    }
    return resolveIconDisplay(this.current).fit;
  }

  protected get frameAspect(): string {
    const ratio = Number.isFinite(this.aspectRatio) && this.aspectRatio > 0 ? this.aspectRatio : 1;
    return `${ratio}`;
  }

  protected get previewStyle(): Record<string, string> {
    return iconDisplayStyle(this.current);
  }

  protected numberValue(field: NumericField): number | '' {
    const stored = this.current?.[field];
    if (typeof stored === 'number' && Number.isFinite(stored)) {
      return stored;
    }
    return this.actionMode ? '' : DEFAULT_ICON_DISPLAY[field];
  }

  protected onFitChange(value: string): void {
    this.emit({ fit: (value || undefined) as WidgetIconFit | undefined });
  }

  protected onNumberChange(field: NumericField, value: string | number): void {
    if (value === '' || value === null || value === undefined) {
      this.emit({ [field]: this.actionMode ? undefined : DEFAULT_ICON_DISPLAY[field] });
      return;
    }
    this.emit({ [field]: this.clamp(field, +value) });
  }

  protected onReset(): void {
    this.resetRequested.emit();
  }

  protected onPointerDown(event: PointerEvent): void {
    if (event.button !== 0 || !this.iconUrl) {
      return;
    }
    event.preventDefault();
    const current = resolveIconDisplay(this.current);
    this.dragPointerId = event.pointerId;
    this.dragOrigin = { x: event.clientX, y: event.clientY, offsetX: current.offsetX, offsetY: current.offsetY };
    (event.target as HTMLElement).setPointerCapture?.(event.pointerId);
  }

  protected onPointerMove(event: PointerEvent): void {
    if (this.dragPointerId !== event.pointerId) {
      return;
    }
    const box = this.frame?.nativeElement.getBoundingClientRect();
    if (!box || box.width === 0 || box.height === 0) {
      return;
    }

    const current = resolveIconDisplay(this.current);
    const offsetX = this.clamp('offsetX',
      this.dragOrigin.offsetX + ((event.clientX - this.dragOrigin.x) / box.width) * 100);
    const offsetY = this.clamp('offsetY',
      this.dragOrigin.offsetY + ((event.clientY - this.dragOrigin.y) / box.height) * 100);

    if (offsetX !== current.offsetX || offsetY !== current.offsetY) {
      this.emit({ offsetX, offsetY });
    }
  }

  protected onPointerUp(event: PointerEvent): void {
    if (this.dragPointerId === event.pointerId) {
      this.dragPointerId = null;
      (event.target as HTMLElement).releasePointerCapture?.(event.pointerId);
    }
  }

  protected onWheel(event: WheelEvent): void {
    if (!this.iconUrl) {
      return;
    }
    event.preventDefault();
    const current = resolveIconDisplay(this.current).zoom;
    const zoom = this.clamp('zoom', current + (event.deltaY < 0 ? WHEEL_ZOOM_STEP : -WHEEL_ZOOM_STEP));
    if (zoom !== current) {
      this.emit({ zoom });
    }
  }

  private emit(patch: Partial<WidgetIconDisplay>): void {
    this.pending = { ...this.current, ...patch };
    this.displayChange.emit(this.pending);
  }

  private clamp(field: NumericField, value: number): number {
    if (!Number.isFinite(value)) {
      return DEFAULT_ICON_DISPLAY[field];
    }
    const bounds = field === 'zoom'
      ? this.limits.zoom
      : field === 'opacity' ? this.limits.opacity : this.limits.offset;
    return Math.round(Math.min(bounds.max, Math.max(bounds.min, value)));
  }
}
