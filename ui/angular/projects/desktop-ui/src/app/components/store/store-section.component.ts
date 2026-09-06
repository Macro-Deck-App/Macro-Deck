import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { StoreCatalogItemBody } from '@macro-deck/runtime';
import { StoreOperationService } from '../../services/store-operation.service';
import { StoreExtensionCardComponent } from './store-extension-card.component';

@Component({
  selector: 'shared-store-section',
  standalone: true,
  imports: [StoreExtensionCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-section.component.html',
  styleUrls: ['./store-section.component.scss'],
})
export class StoreSectionComponent {
  private readonly operations = inject(StoreOperationService);

  readonly heading = input('');
  readonly icon = input('');
  readonly count = input('');
  readonly items = input<StoreCatalogItemBody[]>([]);

  readonly install = output<StoreCatalogItemBody>();
  readonly installUnsigned = output<StoreCatalogItemBody>();
  readonly retry = output<StoreCatalogItemBody>();
  readonly uninstall = output<StoreCatalogItemBody>();

  protected operationFor(item: StoreCatalogItemBody) {
    return this.operations.operationFor(item.kind, item.id);
  }
}
