import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject,
  linkedSignal, signal, untracked, viewChild,
} from '@angular/core';
import {
  ActiveNotification,
  AppStrings,
  LocalizedText,
  NotificationActionDto,
  NotificationDraft,
  UiNode,
  UiNodeEvent,
  applyNotificationDraftEvent,
  createNotificationDraft,
  overlayNotificationDraft,
  resolveLocalizedText,
  type UiComponentBox,
} from '@macro-deck/runtime';

import { LocalizationService, TranslatePipe } from '../../../localization';
import { NotificationPopupService } from '../../../services/notification-popup.service';
import {
  notificationActionVariant,
  notificationProgressPercent,
  notificationSource,
} from '../../../util/notification-view';
import { ButtonComponent } from '../../button/button.component';
import { ModalComponent } from '../../overlay/modal/modal.component';
import { UiWidgetTreeComponent } from '../../ui-render/ui-widget-tree.component';
import { UiWidgetTreeContext } from '../../ui-render/ui-widget-tree-context';

const SEVERITY_ICON: Record<ActiveNotification['severity'], string> = {
  info: 'icon-info',
  success: 'icon-check',
  warning: 'icon-alert-triangle',
  error: 'icon-alert-triangle',
};

interface DraftSource {
  id: string;
  content: UiNode | undefined;
}

@Component({
  selector: 'shared-notification-dialog-host',
  standalone: true,
  imports: [ButtonComponent, ModalComponent, TranslatePipe, UiWidgetTreeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiWidgetTreeContext],
  template: `
    @if (dialog(); as n) {
      @for (key of [attempt()]; track key) {
        <shared-modal
          [heading]="text(n.title)"
          [showClose]="n.dismissible"
          [closeOnOverlay]="n.dismissible"
          [closeOnOverlayClick]="false"
          [showFooter]="n.actions.length > 0 || n.dismissible"
          [zIndex]="1100"
          maxWidth="32rem"
          (close)="dismiss(n)">
          <div class="nd-body">
            <div class="nd-source">
              <span class="nd-source-category">{{ sourceOf(n).categoryKey | translate }}</span>
              @if (sourceOf(n).name; as name) {
                <span class="nd-source-name">{{ name }}</span>
              }
            </div>
            @if (text(n.message); as message) {
              <div class="nd-message-row">
                <span class="icon {{ severityIcon(n) }} icon-md nd-severity nd-severity-{{ n.severity }}" aria-hidden="true"></span>
                <p class="nd-message">{{ message }}</p>
              </div>
            }
            @if (n.progress) {
              @let percent = percentOf(n);
              <div
                class="nd-progress"
                [class.nd-progress-indeterminate]="percent === null"
                role="progressbar"
                aria-valuemin="0"
                aria-valuemax="100"
                [attr.aria-valuenow]="percent">
                @if (percent !== null) {
                  <div class="nd-progress-fill" [style.width.%]="percent"></div>
                }
              </div>
              <span class="nd-progress-label">{{ progressLabel(n) }}</span>
            }
            @if (rejected()) {
              <p class="nd-error" role="alert">{{ appStrings.Feedback.Notification.Rejected | translate }}</p>
            }
            @if (content(); as tree) {
              <div #content class="nd-content widget-dialog-content">
                <shared-ui-widget-tree [root]="tree" [box]="box()" (nodeEvent)="onTreeEvent($event)" />
              </div>
            }
          </div>

          <div modal-footer>
            @for (action of n.actions; track action.id) {
              <shared-button
                class="nd-action"
                [class.nd-primary]="action.style === 'primary'"
                [variant]="variantOf(action)"
                [disabled]="busy()"
                (click)="press(n, action.id)">
                <span>{{ text(action.label) }}</span>
              </shared-button>
            }
            @if (n.actions.length === 0 && n.dismissible) {
              <shared-button class="nd-action nd-primary" variant="primary" [disabled]="busy()" (click)="dismiss(n)">
                <span>{{ appStrings.Feedback.Notification.Ok | translate }}</span>
              </shared-button>
            }
          </div>
        </shared-modal>
      }
    }
  `,
  styles: [`
    .nd-body {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    .nd-source {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .nd-source-name::before {
      content: '\\00b7';
      margin-inline-end: var(--space-1);
    }

    .nd-message-row {
      display: flex;
      align-items: flex-start;
      gap: var(--space-3);
    }

    .nd-message {
      flex: 1;
      min-width: 0;
      margin: 0;
      color: var(--color-text-primary);
      line-height: var(--leading-normal);
      overflow-wrap: anywhere;
      white-space: pre-line;
    }

    .nd-severity {
      flex: none;
      margin-top: 0.125rem;
    }

    .nd-severity-info {
      color: var(--color-accent);
    }

    .nd-severity-success {
      color: var(--color-success);
    }

    .nd-severity-warning {
      color: var(--color-warning);
    }

    .nd-severity-error {
      color: var(--color-danger);
    }

    .nd-error {
      margin: 0;
      color: var(--color-danger);
      font-size: var(--text-base);
    }

    .nd-progress {
      position: relative;
      height: 4px;
      border-radius: 2px;
      background: var(--color-bg-tertiary);
      overflow: hidden;
    }

    .nd-progress-fill {
      height: 100%;
      border-radius: inherit;
      background: var(--color-accent);
      transition: width var(--transition-fast);
    }

    .nd-progress-indeterminate::after {
      content: '';
      position: absolute;
      inset: 0;
      width: 40%;
      border-radius: inherit;
      background: var(--color-accent);
      animation: nd-progress-slide 1.2s ease-in-out infinite;
    }

    .nd-progress-label {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .nd-content {
      position: relative;
      width: 100%;
      height: min(18rem, 40vh);
      overflow: hidden;
    }

    @keyframes nd-progress-slide {
      from { transform: translateX(-100%); }
      to { transform: translateX(350%); }
    }

    @media (prefers-reduced-motion: reduce) {
      .nd-progress-indeterminate::after {
        animation: none;
        width: 100%;
        opacity: 0.5;
      }
    }
  `],
})
export class NotificationDialogHostComponent {
  private readonly popups = inject(NotificationPopupService);
  private readonly localization = inject(LocalizationService);
  private readonly treeContext = inject(UiWidgetTreeContext);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly appStrings = AppStrings;
  protected readonly dialog = this.popups.dialog;
  protected readonly busy = signal(false);
  protected readonly rejected = signal(false);
  protected readonly box = signal<UiComponentBox>({ width: null, height: null });

  protected readonly attempt = signal(0);

  private readonly contentElement = viewChild<ElementRef<HTMLElement>>('content');

  private readonly draft = linkedSignal<DraftSource, NotificationDraft>({
    source: () => {
      const n = this.dialog();
      return { id: n?.id ?? '', content: n?.content };
    },
    computation: (source, previous) => {
      const seeded = source.content ? createNotificationDraft(source.content) : {};
      if (previous && previous.source.id === source.id) {
        for (const key of Object.keys(seeded)) {
          if (key in previous.value && typeof previous.value[key] === typeof seeded[key]) {
            seeded[key] = previous.value[key];
          }
        }
      }
      return seeded;
    },
  });

  protected readonly content = computed<UiNode | null>(() => {
    const content = this.dialog()?.content;
    return content ? overlayNotificationDraft(content, this.draft()) : null;
  });

  private resizeObserver: ResizeObserver | null = null;
  private shownId: string | null = null;
  private focusReturn: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const element = this.contentElement()?.nativeElement;
      if (element) this.observe(element);
    });

    effect(() => {
      const id = this.dialog()?.id ?? null;
      untracked(() => this.dialogChanged(id));
    });

    inject(DestroyRef).onDestroy(() => this.resizeObserver?.disconnect());
  }

  protected text(value: LocalizedText | undefined): string {
    return resolveLocalizedText(value, this.localization);
  }

  protected sourceOf(notification: ActiveNotification) {
    return notificationSource(notification.sourceCategory, notification.sourceName);
  }

  protected severityIcon(notification: ActiveNotification): string {
    return SEVERITY_ICON[notification.severity] ?? SEVERITY_ICON.info;
  }

  protected percentOf(notification: ActiveNotification): number | null {
    return notificationProgressPercent(notification.progress);
  }

  protected progressLabel(notification: ActiveNotification): string {
    const progress = notification.progress;
    if (!progress) return '';

    return progress.total != null
      ? this.localization.translateKey(AppStrings.Shell.Notifications.ProcessingOfTotal, {
        processed: progress.processed,
        total: progress.total,
      })
      : this.localization.translateKey(AppStrings.Shell.Notifications.ProcessingCount, {
        processed: progress.processed,
      });
  }

  protected variantOf(action: NotificationActionDto) {
    return notificationActionVariant(action.style);
  }

  protected onTreeEvent(event: UiNodeEvent): void {
    const content = this.dialog()?.content;
    if (!content) return;

    this.draft.update(draft => applyNotificationDraftEvent(content, draft, event));
    this.rejected.set(false);
  }

  protected press(notification: ActiveNotification, actionId: string): void {
    void this.respond(notification, actionId, this.draft());
  }

  protected dismiss(notification: ActiveNotification): void {
    void this.respond(notification, null, {});
  }

  private async respond(
    notification: ActiveNotification,
    actionId: string | null,
    values: NotificationDraft,
  ): Promise<void> {
    if (this.busy()) return;

    this.busy.set(true);
    let accepted = false;
    try {
      const status = (await this.popups.respond(notification.id, actionId, values))?.status;
      accepted = status === 'accepted';
      this.rejected.set(status === 'invalid');
    } finally {
      this.busy.set(false);
    }

    if (actionId === null && !accepted && this.dialog()?.id === notification.id) {
      this.attempt.update(value => value + 1);
    }
  }

  private dialogChanged(id: string | null): void {
    if (id === this.shownId) return;

    this.rejected.set(false);
    if (id !== null) {
      if (this.shownId === null) {
        const active = document.activeElement;
        this.focusReturn = active instanceof HTMLElement && active !== document.body ? active : null;
      }
      afterNextRender(() => this.focusDefaultAction(), { injector: this.injector });
    } else if (this.focusReturn?.isConnected) {
      this.focusReturn.focus();
      this.focusReturn = null;
    } else {
      this.focusReturn = null;
    }

    this.shownId = id;
  }

  private focusDefaultAction(): void {
    const target = this.host.querySelector<HTMLElement>('.nd-primary button:not(:disabled)')
      ?? this.host.querySelector<HTMLElement>('.nd-action button:not(:disabled)');
    target?.focus();
  }

  private observe(element: HTMLElement): void {
    this.measure(element.clientWidth, element.clientHeight);

    this.resizeObserver?.disconnect();
    this.resizeObserver = new ResizeObserver(entries => {
      const rect = entries[0]?.contentRect;
      if (!rect) return;
      this.measure(rect.width, rect.height);
    });
    this.resizeObserver.observe(element);
  }

  private measure(width: number, height: number): void {
    this.treeContext.setBasis(Math.min(width, height));
    this.box.set({ width: width || null, height: height || null });
  }
}
