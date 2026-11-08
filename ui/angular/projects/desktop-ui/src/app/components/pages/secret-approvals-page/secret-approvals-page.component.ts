import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  AppStrings,
  SecretApprovalItem,
  SecretApprovalOwnerKind,
  SecretApprovalParameter,
  resolveLocalizedText
} from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { SecretApprovalService, secretApprovalKey } from '../../../services/secret-approval.service';

type Decision = 'approve' | 'decline';

interface ApprovalRow {
  name: string;
  label: string;
  secret: boolean;
  changed: boolean;
  approved: string;
  current: string;
  alsoUsedBy: string | null;
}

interface ApprovalCard {
  key: string;
  item: SecretApprovalItem;
  ownerKind: string;
  owner: string;
  actionName: string;
  approvedBefore: boolean;
  unknown: boolean;
  rows: ApprovalRow[];
}

const OWNER_KIND_LABEL: Record<SecretApprovalOwnerKind, string> = {
  widget: AppStrings.SecretApprovals.OwnerKind.Widget,
  automation: AppStrings.SecretApprovals.OwnerKind.Automation,
  script: AppStrings.SecretApprovals.OwnerKind.Script
};

@Component({
  selector: 'app-secret-approvals-page',
  standalone: true,
  imports: [ButtonComponent, EmptyStateComponent, ErrorBannerComponent, LoadingStateComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './secret-approvals-page.component.html',
  styleUrls: ['./secret-approvals-page.component.scss']
})
export class SecretApprovalsPageComponent implements OnInit {
  protected readonly approvals = inject(SecretApprovalService);
  private readonly localization = inject(LocalizationService);

  protected readonly decisionError = signal<string | null>(null);
  private readonly deciding = signal<ReadonlyMap<string, Decision>>(new Map());

  protected readonly cards = computed<ApprovalCard[]>(() => {
    const text = (key: string, args?: Record<string, unknown>): string => this.localization.translateKey(key, args);
    const secretText = text(AppStrings.SecretApprovals.SecretValue);
    const notSet = text(AppStrings.SecretApprovals.NotSet);
    const listFormat = this.listFormat();

    return this.approvals.items().map(item => {
      const kind = OWNER_KIND_LABEL[item.ownerKind];
      const row = (parameter: SecretApprovalParameter): ApprovalRow => {
        const label = resolveLocalizedText(parameter.label, this.localization) || parameter.name;
        const others = parameter.secret ? parameter.alsoUsedBy ?? [] : [];
        return {
          name: parameter.name,
          label,
          secret: parameter.secret,
          changed: parameter.changed,
          approved: parameter.secret
            ? item.approvedBefore ? secretText : notSet
            : parameter.approved ?? notSet,
          current: parameter.secret ? secretText : parameter.current ?? notSet,
          alsoUsedBy: others.length > 0
            ? text(AppStrings.SecretApprovals.AlsoUsedBy, { owners: listFormat(others) })
            : null
        };
      };

      return {
        key: secretApprovalKey(item),
        item,
        ownerKind: kind ? text(kind) : item.ownerKind,
        owner: item.ownerContext
          ? text(AppStrings.SecretApprovals.OwnerWithContext, { owner: item.ownerName, context: item.ownerContext })
          : item.ownerName,
        actionName: resolveLocalizedText(item.actionName, this.localization) || item.actionId,
        approvedBefore: item.approvedBefore,
        unknown: item.unknown === true,
        rows: (item.parameters ?? []).map(row)
      };
    });
  });

  protected readonly showLoading = computed(() => !this.approvals.hasLoaded() && this.approvals.isLoading());

  ngOnInit(): void {
    void this.approvals.load();
  }

  protected decidingWith(card: ApprovalCard): Decision | null {
    return this.deciding().get(card.key) ?? null;
  }

  protected approve(card: ApprovalCard): Promise<void> {
    return this.decide(card, 'approve');
  }

  protected decline(card: ApprovalCard): Promise<void> {
    return this.decide(card, 'decline');
  }

  private async decide(card: ApprovalCard, decision: Decision): Promise<void> {
    if (this.decidingWith(card)) {
      return;
    }
    this.decisionError.set(null);
    this.deciding.update(keys => new Map(keys).set(card.key, decision));
    try {
      const result = decision === 'approve'
        ? await this.approvals.approve(card.item)
        : await this.approvals.decline(card.item);
      if (!result.ok) {
        this.decisionError.set(result.message);
      }
    } finally {
      this.deciding.update(keys => {
        const next = new Map(keys);
        next.delete(card.key);
        return next;
      });
    }
  }

  private listFormat(): (values: string[]) => string {
    try {
      const format = new Intl.ListFormat(this.localization.culture(), { type: 'conjunction' });
      return values => format.format(values);
    } catch {
      return values => values.join(', ');
    }
  }
}
