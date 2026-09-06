import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, inject, input } from '@angular/core';
import { AppStrings, IntegrationIssueSeverity, IpcIntegrationIssue } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, LocalizedTextPipe, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';

const SEVERITY_RANK: Record<IntegrationIssueSeverity, number> = { info: 0, warning: 1, error: 2 };

@Component({
  selector: 'app-integration-issues-card',
  standalone: true,
  imports: [
    ButtonComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    LocalizedTextPipe,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents;' },
  templateUrl: './integration-issues-card.component.html',
  styleUrls: ['./integration-issues-card.component.scss'],
})
export class IntegrationIssuesCardComponent {
  private readonly localization = inject(LocalizationService);

  readonly issues = input.required<IpcIntegrationIssue[]>();
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly resolvingIssueId = input<string | null>(null);

  @Output() dismissError = new EventEmitter<void>();
  @Output() resolveIssue = new EventEmitter<IpcIntegrationIssue>();

  protected readonly severity = computed<IntegrationIssueSeverity | null>(() => {
    const issues = this.issues();
    if (issues.length === 0) {
      return null;
    }
    return issues.reduce<IntegrationIssueSeverity>(
      (max, issue) => (SEVERITY_RANK[issue.severity] > SEVERITY_RANK[max] ? issue.severity : max),
      issues[0].severity
    );
  });

  protected readonly issueCountLabel = computed(() =>
    this.localization.translateKey(AppStrings.Integrations.Page.IssueCount, { count: this.issues().length }));
}
