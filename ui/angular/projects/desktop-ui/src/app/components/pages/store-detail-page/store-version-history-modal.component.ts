import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { StoreVersionHistoryBody } from '@macro-deck/runtime';
import { ModalComponent, TranslatePipe } from '@shared';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';

@Component({
  selector: 'app-store-version-history-modal',
  standalone: true,
  imports: [DatePipe, ModalComponent, StoreMarkdownComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-version-history-modal.component.html',
  styleUrls: ['./store-version-history-modal.component.scss'],
})
export class StoreVersionHistoryModalComponent {
  readonly history = input.required<StoreVersionHistoryBody[]>();

  readonly closed = output<void>();

  protected onClose(): void {
    this.closed.emit();
  }
}
