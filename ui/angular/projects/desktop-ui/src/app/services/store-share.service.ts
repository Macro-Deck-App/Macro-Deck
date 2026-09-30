import { Injectable, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, ToastService } from '@shared';
import { storeShareUrl } from '../util/store-share-link';
import { TextClipboardService, clipboardFailureDetail } from './text-clipboard.service';

@Injectable({ providedIn: 'root' })
export class StoreShareService {
  private readonly clipboard = inject(TextClipboardService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  async copyLink(packageId: string): Promise<void> {
    const result = await this.clipboard.copyText(storeShareUrl(packageId));
    this.toasts.show(
      result.status === 'copied'
        ? this.localization.translateKey(AppStrings.Store.Page.LinkCopied)
        : clipboardFailureDetail(result.reason, this.localization),
      { variant: result.status === 'copied' ? 'success' : 'error' },
    );
  }
}
