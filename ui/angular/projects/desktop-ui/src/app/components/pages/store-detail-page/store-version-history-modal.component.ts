import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { StoreVersionHistoryBody } from '@macro-deck/runtime';
import { ButtonComponent, ModalComponent, TranslatePipe } from '@shared';
import { SelectOption } from '../../forms/select/select.component';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { formatBytes } from '../../../util/format-bytes';
import { sameVersion } from '../../../util/semver-compare';

@Component({
  selector: 'app-store-version-history-modal',
  standalone: true,
  imports: [ButtonComponent, DatePipe, ModalComponent, StoreMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-version-history-modal.component.html',
  styleUrls: ['./store-version-history-modal.component.scss'],
})
export class StoreVersionHistoryModalComponent {
  readonly history = input.required<StoreVersionHistoryBody[]>();
  readonly options = input<SelectOption[]>([]);
  readonly selectedVersion = input<string | null>(null);

  readonly closed = output<void>();
  readonly selectVersion = output<string>();

  protected readonly formatBytes = formatBytes;

  private readonly optionsByVersion = computed(() => new Map(this.options().map(option => [option.value, option])));

  protected readonly selectable = computed(() => this.options().length > 1);

  protected option(entry: StoreVersionHistoryBody): SelectOption | undefined {
    return this.optionsByVersion().get(entry.version);
  }

  protected isSelected(entry: StoreVersionHistoryBody): boolean {
    return sameVersion(entry.version, this.selectedVersion());
  }

  protected onClose(): void {
    this.closed.emit();
  }
}
