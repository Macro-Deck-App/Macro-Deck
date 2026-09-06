import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, inject, signal } from '@angular/core';
import { Strings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

// Longest a real animationend could plausibly be delayed by (busy main thread, throttled rAF) before
// requestClose gives up and emits close anyway. A safety net, not a second copy of the exit
// animation's duration, which lives in the stylesheet.
const CLOSE_FALLBACK_MS = 600;

@Component({
  selector: 'shared-detail-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './detail-page.component.html',
  styleUrls: ['./detail-page.component.scss'],
})
export class DetailPageComponent implements OnDestroy {
  private readonly localization = inject(LocalizationService);

  private backLabelOverride?: string;

  @Input()
  set backLabel(value: string) {
    this.backLabelOverride = value;
  }

  get backLabel(): string {
    return this.backLabelOverride ?? this.localization.translateKey(Strings.Common.Back);
  }

  @Output() readonly close = new EventEmitter<void>();

  protected readonly leaving = signal(false);

  @ViewChild('surface', { static: true })
  private readonly surfaceRef!: ElementRef<HTMLElement>;

  private fallbackTimer?: ReturnType<typeof setTimeout>;
  private closeEmitted = false;

  requestClose(): void {
    if (this.leaving()) return;
    this.leaving.set(true);

    // `prefers-reduced-motion` collapses the animation to nothing in CSS, which may mean no
    // `animationend` ever arrives - so `close` is emitted right away instead of waiting for one.
    if (this.prefersReducedMotion()) {
      this.emitCloseOnce();
      return;
    }

    // Safety net: an `animationend` that is dropped or never fires for any reason must not leave
    // the page animated-out and stuck - a page that never closes is worse than a missing animation.
    this.fallbackTimer = setTimeout(() => this.emitCloseOnce(), CLOSE_FALLBACK_MS);
  }

  cancelClose(): void {
    clearTimeout(this.fallbackTimer);
    this.fallbackTimer = undefined;
    this.closeEmitted = false;
    this.leaving.set(false);
  }

  protected onSurfaceAnimationEnd(event: AnimationEvent): void {
    if (event.target !== this.surfaceRef.nativeElement || !this.leaving()) return;
    clearTimeout(this.fallbackTimer);
    this.emitCloseOnce();
  }

  ngOnDestroy(): void {
    clearTimeout(this.fallbackTimer);
  }

  private emitCloseOnce(): void {
    if (this.closeEmitted) return;
    this.closeEmitted = true;
    this.close.emit();
  }

  private prefersReducedMotion(): boolean {
    return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  }
}
