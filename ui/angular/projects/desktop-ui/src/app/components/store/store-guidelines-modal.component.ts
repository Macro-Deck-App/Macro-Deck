import { ChangeDetectionStrategy, Component, ViewChild, inject, output, signal } from '@angular/core';
import { ApiService, ButtonComponent, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { StoreMarkdownComponent } from './store-markdown.component';

@Component({
  selector: 'app-store-guidelines-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, StoreMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-guidelines-modal.component.html',
  styleUrls: ['./store-guidelines-modal.component.scss'],
})
export class StoreGuidelinesModalComponent {
  private readonly api = inject(ApiService);

  readonly closed = output<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly markdown = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    try {
      const response = await this.api.getStoreCreatorGuidelines();
      if (response.available && response.markdown) {
        this.markdown.set(response.markdown);
      } else {
        this.loadFailed.set(true);
      }
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected close(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }
}
