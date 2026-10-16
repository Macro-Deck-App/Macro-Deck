import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { AppStrings, GetStoreRepositoryIssuesResponse, StoreRepositoryIssuesError } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationKey, TranslatePipe } from '@shared';

const ERROR_KEYS: Readonly<Record<StoreRepositoryIssuesError, LocalizationKey>> = {
  NoRepository: AppStrings.Store.Page.Issues.NoRepository,
  NotFound: AppStrings.Store.Page.Issues.NotFound,
  IssuesDisabled: AppStrings.Store.Page.Issues.Disabled,
  RateLimited: AppStrings.Store.Page.Issues.RateLimited,
  Unavailable: AppStrings.Store.Page.Issues.Unavailable,
};

@Component({
  selector: 'app-store-issues-panel',
  standalone: true,
  imports: [ButtonComponent, DatePipe, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-issues-panel.component.html',
  styleUrls: ['./store-issues-panel.component.scss'],
})
export class StoreIssuesPanelComponent {
  readonly response = input<GetStoreRepositoryIssuesResponse | null>(null);

  readonly openUrl = output<string>();

  protected readonly errorKey = computed(() => {
    const error = this.response()?.error;
    return error ? ERROR_KEYS[error] ?? AppStrings.Store.Page.Issues.Unavailable : null;
  });

  protected readonly allIssuesUrl = computed(() => {
    const response = this.response();
    const newIssueUrl = response?.newIssueUrl;
    if (!response || response.error || response.issues.length === 0 || !newIssueUrl?.endsWith('/issues/new')) {
      return null;
    }
    return newIssueUrl.slice(0, -'/new'.length);
  });

  protected open(event: MouseEvent, url: string): void {
    event.preventDefault();
    this.openUrl.emit(url);
  }
}
