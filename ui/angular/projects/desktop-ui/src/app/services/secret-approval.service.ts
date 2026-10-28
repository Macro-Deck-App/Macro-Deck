import { Injectable, inject, signal } from '@angular/core';
import {
  AppStrings,
  SecretApprovalItem,
  UserNotification,
  UserNotificationsChangedEvent,
  resolveLocalizedText
} from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

export type SecretApprovalResult = { ok: true } | { ok: false; message: string };

export function secretApprovalKey(item: SecretApprovalItem): string {
  return `${item.ownerKind}:${item.ownerId}:${item.blockId}`;
}

function asksForApproval(notification: UserNotification): boolean {
  return notification.action?.kind === 'OpenSecretApprovals'
    || (notification.actions ?? []).some(action => action.kind === 'OpenSecretApprovals');
}

@Injectable({ providedIn: 'root' })
export class SecretApprovalService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly items = signal<SecretApprovalItem[]>([]);
  readonly count = signal(0);
  readonly isLoading = signal(false);
  readonly hasLoaded = signal(false);
  readonly loadError = signal<string | null>(null);

  private readonly seenNotifications = new Set<string>();
  private generation = 0;

  constructor() {
    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.load();
      }
    });

    this.api
      .onNotification<UserNotificationsChangedEvent>('UserNotificationsChangedEvent')
      .subscribe(event => {
        const fresh = (event.notifications ?? [])
          .filter(asksForApproval)
          .map(notification => `${notification.id}:${notification.sequence}`)
          .filter(key => !this.seenNotifications.has(key));
        if (fresh.length > 0) {
          fresh.forEach(key => this.seenNotifications.add(key));
          void this.load();
        }
      });
  }

  async load(): Promise<void> {
    const generation = ++this.generation;
    this.isLoading.set(true);

    try {
      const response = await this.api.getSecretApprovals();
      if (generation !== this.generation) {
        return;
      }
      this.items.set(response.items ?? []);
      this.count.set(response.count ?? response.items?.length ?? 0);
      this.loadError.set(null);
      this.hasLoaded.set(true);
    } catch (error) {
      if (generation === this.generation) {
        console.error('Failed to load secret approvals:', error);
        this.loadError.set(this.localization.translateKey(AppStrings.SecretApprovals.LoadFailed));
      }
    } finally {
      if (generation === this.generation) {
        this.isLoading.set(false);
      }
    }
  }

  async approve(item: SecretApprovalItem): Promise<SecretApprovalResult> {
    let response;
    try {
      response = await this.api.approveSecretBlock({
        ownerKind: item.ownerKind,
        ownerId: item.ownerId,
        blockId: item.blockId,
        fingerprint: item.fingerprint
      });
    } catch (error) {
      console.error('Failed to approve secret block:', error);
      return { ok: false, message: this.localization.translateKey(AppStrings.SecretApprovals.ApproveFailed) };
    }

    if (response.success) {
      const key = secretApprovalKey(item);
      this.items.update(items => items.filter(candidate => secretApprovalKey(candidate) !== key));
      this.count.update(count => Math.max(0, count - 1));
      void this.load();
      return { ok: true };
    }

    void this.load();
    const message = resolveLocalizedText(response.error?.message ?? null, this.localization);
    return {
      ok: false,
      message: message || this.localization.translateKey(AppStrings.SecretApprovals.ApproveFailed)
    };
  }
}
