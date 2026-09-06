import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { AppStrings, StoreExtensionKind, StoreScreenshotBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { StoreScreenshotViewerComponent } from './store-screenshot-viewer.component';

@Component({
  selector: 'app-store-screenshot-strip',
  standalone: true,
  imports: [StoreScreenshotViewerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-screenshot-strip.component.html',
  styleUrls: ['./store-screenshot-strip.component.scss'],
})
export class StoreScreenshotStripComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly kind = input.required<StoreExtensionKind>();
  readonly extensionId = input.required<string>();
  readonly screenshots = input.required<StoreScreenshotBody[]>();

  protected readonly openIndex = signal<number | null>(null);
  private triggerElement: HTMLElement | null = null;

  protected thumbnailUrl(screenshot: StoreScreenshotBody): string {
    return this.api.getStoreScreenshotUrl(this.kind(), this.extensionId(), screenshot.index);
  }

  protected thumbnailAriaLabel(screenshot: StoreScreenshotBody, index: number): string {
    return screenshot.caption || this.localization.translateKey(AppStrings.Store.Page.ScreenshotAriaLabel, { number: index + 1 });
  }

  protected open(index: number, event: MouseEvent): void {
    this.triggerElement = event.currentTarget as HTMLElement;
    this.openIndex.set(index);
  }

  protected onViewerClosed(): void {
    this.openIndex.set(null);
    this.triggerElement?.focus();
    this.triggerElement = null;
  }
}
