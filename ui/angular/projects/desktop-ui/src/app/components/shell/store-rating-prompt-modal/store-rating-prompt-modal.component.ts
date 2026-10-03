import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import {
  ApiService,
  ButtonComponent,
  InputComponent,
  LocalizationService,
  ModalComponent,
  ToastService,
  TranslatePipe,
  dismissModal,
} from '@shared';
import { StoreRatingPromptService } from '../../../services/store-rating-prompt.service';
import { StoreStarPickerComponent } from '../../store/store-star-picker.component';
import { storeKindIcon } from '../../../util/store-operation-display';

@Component({
  selector: 'app-store-rating-prompt-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, InputComponent, StoreStarPickerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-rating-prompt-modal.component.html',
  styleUrls: ['./store-rating-prompt-modal.component.scss'],
})
export class StoreRatingPromptModalComponent {
  private readonly prompt = inject(StoreRatingPromptService);
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);
  protected readonly appStrings = AppStrings;

  @ViewChild(ModalComponent) private readonly modal?: ModalComponent;

  protected readonly candidate = this.prompt.pending;
  protected readonly rating = signal<number | null>(null);
  protected readonly body = signal('');
  protected readonly reviewOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly iconFailed = signal(false);

  protected readonly iconUrl = computed(() => {
    const candidate = this.candidate();
    return candidate?.hasIcon
      ? this.api.getStoreExtensionIconUrl(candidate.kind, candidate.id, candidate.iconSha256 ?? undefined)
      : null;
  });

  protected readonly kindIcon = computed(() => storeKindIcon(this.candidate()?.kind ?? 'Plugin'));

  protected readonly heading = computed(() => this.localization.translateKey(
    AppStrings.Store.RatingPrompt.Heading, { name: this.candidate()?.name ?? '' }));

  constructor() {
    this.prompt.markShown();
  }

  protected selectRating(value: number | null): void {
    this.rating.set(value);
    this.error.set(null);
  }

  protected async submit(): Promise<void> {
    const rating = this.rating();
    if (rating === null || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.prompt.submit(rating, this.body());
      if (result.ok) {
        this.toasts.show(this.localization.translateKey(AppStrings.Store.RatingPrompt.Thanks));
      } else {
        this.error.set(result.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected notNow(): void {
    dismissModal(this.modal, () => this.prompt.dismiss());
  }

  protected onModalClose(): void {
    this.prompt.dismiss();
  }
}
