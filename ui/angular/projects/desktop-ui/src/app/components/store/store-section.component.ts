import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output, viewChild } from '@angular/core';
import { StoreCatalogItemBody } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';
import { StoreOperationService } from '../../services/store-operation.service';
import { StoreExtensionCardComponent } from './store-extension-card.component';

export interface StoreUnsignedInstallRequest {
  item: StoreCatalogItemBody;
  version: string | undefined;
}

@Component({
  selector: 'shared-store-section',
  standalone: true,
  imports: [StoreExtensionCardComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-section.component.html',
  styleUrls: ['./store-section.component.scss'],
})
export class StoreSectionComponent {
  private readonly operations = inject(StoreOperationService);

  readonly heading = input('');
  readonly subheading = input('');
  readonly icon = input('');
  readonly count = input('');
  readonly items = input<StoreCatalogItemBody[]>([]);
  readonly layout = input<'grid' | 'row'>('grid');
  readonly cards = input<'compact' | 'tile' | null>(null);
  readonly featuredKeys = input<ReadonlySet<string>>(new Set());
  readonly updatesAvailable = input(false);

  readonly install = output<StoreCatalogItemBody>();
  readonly installUnsigned = output<StoreUnsignedInstallRequest>();
  readonly retry = output<StoreCatalogItemBody>();
  readonly uninstall = output<StoreCatalogItemBody>();
  readonly checkForUpdates = output<void>();

  private readonly track = viewChild<ElementRef<HTMLElement>>('track');

  protected readonly cardVariant = computed(() => this.cards() ?? (this.layout() === 'row' ? 'tile' : 'compact'));

  protected operationFor(item: StoreCatalogItemBody) {
    return this.operations.operationFor(item.kind, item.id);
  }

  protected isFeatured(item: StoreCatalogItemBody): boolean {
    return this.featuredKeys().has(`${item.kind}:${item.id}`);
  }

  protected scroll(direction: -1 | 1): void {
    const track = this.track()?.nativeElement;
    if (!track) {
      return;
    }
    track.scrollBy({ left: direction * track.clientWidth * 0.9, behavior: 'smooth' });
  }
}
