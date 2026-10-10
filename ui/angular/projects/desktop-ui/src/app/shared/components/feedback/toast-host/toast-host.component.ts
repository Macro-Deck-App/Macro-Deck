import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import {
  ActiveNotification,
  AppStrings,
  LocalizedText,
  NotificationActionDto,
  resolveLocalizedText,
} from '@macro-deck/runtime';

import { LocalizationService, TranslatePipe } from '../../../localization';
import { NotificationPopupService } from '../../../services/notification-popup.service';
import { Toast, ToastService, ToastVariant } from '../../../services/toast.service';
import {
  notificationActionVariant,
  notificationProgressPercent,
  notificationSource,
} from '../../../util/notification-view';
import { ButtonComponent } from '../../button/button.component';

const MAX_VISIBLE = 3;

interface ToastView {
  key: string;
  variant: ToastVariant;
  title: LocalizedText;
  message: LocalizedText | undefined;
  notification: ActiveNotification | null;
  localId: number | null;
}

@Component({
  selector: 'shared-toast-host',
  standalone: true,
  imports: [ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="th" role="status" aria-live="polite">
      @for (toast of visible(); track toast.key) {
        <div class="th-toast th-{{ toast.variant }}">
          <svg class="th-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            @switch (toast.variant) {
              @case ('error') {
                <circle cx="12" cy="12" r="10"/>
                <path d="M12 8v4"/>
                <path d="M12 16h.01"/>
              }
              @case ('warning') {
                <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/>
                <path d="M12 9v4"/>
                <path d="M12 17h.01"/>
              }
              @case ('info') {
                <circle cx="12" cy="12" r="10"/>
                <path d="M12 16v-4"/>
                <path d="M12 8h.01"/>
              }
              @default {
                <path d="M20 6 9 17l-5-5"/>
              }
            }
          </svg>
          <div class="th-text">
            @if (toast.notification; as n) {
              <span class="th-source">
                <span class="th-source-category">{{ sourceOf(n).categoryKey | translate }}</span>
                @if (sourceOf(n).name; as name) {
                  <span class="th-source-name">{{ name }}</span>
                }
              </span>
            }
            <span class="th-message">{{ text(toast.title) }}</span>
            @if (text(toast.message); as message) {
              <span class="th-detail">{{ message }}</span>
            }
            @if (toast.notification?.progress) {
              @let percent = percentOf(toast.notification!);
              <div
                class="th-progress"
                [class.th-progress-indeterminate]="percent === null"
                role="progressbar"
                aria-valuemin="0"
                aria-valuemax="100"
                [attr.aria-valuenow]="percent">
                @if (percent !== null) {
                  <div class="th-progress-fill" [style.width.%]="percent"></div>
                }
              </div>
            }
            @if (toast.notification?.actions?.length) {
              <div class="th-actions">
                @for (action of toast.notification!.actions; track action.id) {
                  <shared-button
                    size="compact"
                    [variant]="variantOf(action)"
                    (click)="press(toast.notification!, action.id)">
                    <span>{{ text(action.label) }}</span>
                  </shared-button>
                }
              </div>
            }
          </div>
          @if (!toast.notification || toast.notification.dismissible) {
            <button class="th-dismiss" type="button" [attr.aria-label]="appStrings.Feedback.Dismiss | translate" (click)="dismiss(toast)">
              <svg class="th-dismiss-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M18 6 6 18"/>
                <path d="m6 6 12 12"/>
              </svg>
            </button>
          }
        </div>
      }
    </div>
  `,
  styleUrls: ['./toast-host.component.scss'],
})
export class ToastHostComponent {
  protected readonly toasts = inject(ToastService);
  private readonly popups = inject(NotificationPopupService);
  private readonly localization = inject(LocalizationService);
  protected readonly appStrings = AppStrings;

  private readonly views = computed<ToastView[]>(() => [
    ...this.toasts.toasts().map(localView),
    ...this.popups.toasts().map(notificationView),
  ]);

  protected readonly visible = computed(() => this.views().slice(0, MAX_VISIBLE));

  protected text(value: LocalizedText | undefined): string {
    return resolveLocalizedText(value, this.localization);
  }

  protected sourceOf(notification: ActiveNotification) {
    return notificationSource(notification.sourceCategory, notification.sourceName);
  }

  protected percentOf(notification: ActiveNotification): number | null {
    return notificationProgressPercent(notification.progress);
  }

  protected variantOf(action: NotificationActionDto) {
    return notificationActionVariant(action.style);
  }

  protected press(notification: ActiveNotification, actionId: string): void {
    void this.popups.respond(notification.id, actionId);
  }

  protected dismiss(toast: ToastView): void {
    if (toast.notification) {
      void this.popups.respond(toast.notification.id, null);
    } else if (toast.localId !== null) {
      this.toasts.dismiss(toast.localId);
    }
  }
}

function localView(toast: Toast): ToastView {
  return {
    key: `local-${toast.id}`,
    variant: toast.variant,
    title: toast.message,
    message: toast.detail,
    notification: null,
    localId: toast.id,
  };
}

function notificationView(notification: ActiveNotification): ToastView {
  return {
    key: `notification-${notification.id}`,
    variant: notification.severity,
    title: notification.title,
    message: notification.message,
    notification,
    localId: null,
  };
}
