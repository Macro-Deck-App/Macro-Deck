import { ChangeDetectionStrategy, Component, Input, inject, signal } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ToastService } from '@shared';
import { TextClipboardService, clipboardFailureDetail } from '../../services/text-clipboard.service';
import { CopyTextModalComponent } from '../overlay/copy-text-modal/copy-text-modal.component';

@Component({
  selector: 'shared-copy-value',
  standalone: true,
  imports: [ButtonComponent, CopyTextModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cv-label">{{ label }}</div>
    <div class="cv-body">
      <code class="cv-value">{{ value }}</code>
      <shared-button
        variant="ghost"
        size="icon"
        [ariaLabel]="copyAriaLabel()"
        (click)="copy()">
        <span class="icon icon-copy icon-sm" aria-hidden="true"></span>
      </shared-button>
    </div>
    @if (manualCopy(); as fallback) {
      <shared-copy-text-modal
        [message]="fallback.message"
        [value]="fallback.value"
        [zIndex]="fallbackZIndex"
        (closed)="manualCopy.set(null)" />
    }
  `,
  styleUrls: ['./copy-value.component.scss'],
})
export class CopyValueComponent {
  @Input() label = '';
  @Input() value = '';
  @Input() fallbackZIndex: number | null = null;

  private readonly clipboard = inject(TextClipboardService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  protected readonly manualCopy = signal<{ value: string; message: string } | null>(null);

  protected copyAriaLabel(): string {
    return this.localization.translateKey(AppStrings.CopyValue.CopyAriaLabel, { label: this.label });
  }

  protected async copy(): Promise<void> {
    const result = await this.clipboard.copyText(this.value);
    if (result.status === 'copied') {
      this.toasts.show(
        this.localization.translateKey(AppStrings.CopyValue.Copied, { label: this.label }),
        { detail: this.value },
      );
      return;
    }

    this.manualCopy.set({
      value: this.value,
      message: this.localization.translateKey(AppStrings.CopyValue.ManualCopyMessage, {
        detail: clipboardFailureDetail(result.reason, this.localization),
      }),
    });
  }
}
