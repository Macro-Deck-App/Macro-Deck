import { ChangeDetectionStrategy, Component, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { AppStrings, StoreExtensionKind, StoreScreenshotBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ModalComponent, TranslatePipe } from '@shared';

@Component({
  selector: 'app-store-screenshot-viewer',
  standalone: true,
  imports: [ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-screenshot-viewer.component.html',
  styleUrls: ['./store-screenshot-viewer.component.scss'],
})
export class StoreScreenshotViewerComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly kind = input.required<StoreExtensionKind>();
  readonly extensionId = input.required<string>();
  readonly screenshots = input.required<StoreScreenshotBody[]>();
  readonly initialIndex = input(0);

  readonly closed = output<void>();

  protected readonly position = signal(0);

  constructor() {
    this.position.set(this.initialIndex());
  }

  protected readonly current = computed(() => this.screenshots()[this.position()] ?? null);

  protected readonly currentUrl = computed(() => {
    const screenshot = this.current();
    return screenshot ? this.api.getStoreScreenshotUrl(this.kind(), this.extensionId(), screenshot.index) : '';
  });

  protected readonly currentCaption = computed(() => this.current()?.caption ?? '');

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.ScreenshotPositionHeading, {
      position: this.position() + 1,
      total: this.screenshots().length,
    }));

  protected readonly counter = computed(() =>
    this.localization.translateKey(AppStrings.Store.Page.ScreenshotCounter, {
      position: this.position() + 1,
      total: this.screenshots().length,
    }));

  protected readonly hasMultiple = computed(() => this.screenshots().length > 1);

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') {
      this.prev();
    } else if (event.key === 'ArrowRight') {
      this.next();
    }
  }

  protected prev(): void {
    const length = this.screenshots().length;
    if (length === 0) return;
    this.position.set((this.position() - 1 + length) % length);
  }

  protected next(): void {
    const length = this.screenshots().length;
    if (length === 0) return;
    this.position.set((this.position() + 1) % length);
  }

  protected onClose(): void {
    this.closed.emit();
  }
}
