import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ModalComponent, TranslatePipe } from '@shared';
import { cultureDisplayName } from '../../../localization/culture-display.util';

@Component({
  selector: 'app-store-languages-modal',
  standalone: true,
  imports: [ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-languages-modal.component.html',
  styleUrls: ['./store-languages-modal.component.scss'],
})
export class StoreLanguagesModalComponent {
  readonly languages = input.required<readonly string[]>();

  readonly closed = output<void>();

  protected readonly cultureDisplayName = cultureDisplayName;

  protected onClose(): void {
    this.closed.emit();
  }
}
