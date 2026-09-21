import { DatePipe, NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
  GetStoreOwnReviewResponse,
  GetStoreRatingResponse,
  StoreExtensionKind,
  StoreOwnReviewWriteResponse,
  StoreReviewBody,
  StoreReviewSortOrder,
  StoreReviewWriteError,
} from '@macro-deck/runtime';
import { ApiService, ButtonComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { AvatarComponent } from '../account/avatar.component';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { ConnectAccountService } from '../../services/connect-account.service';
import { SettingsModalService } from '../../services/settings-modal.service';
import { StoreRatingsService } from '../../services/store-ratings.service';
import { formatStoreRating } from '../../util/store-rating-format';
import { STORE_STAR_PATH, StoreRatingStarsComponent } from './store-rating-stars.component';
import { StoreReportDialogComponent, StoreReportTarget } from './store-report-dialog.component';

export const STORE_REVIEWS_PAGE_SIZE = 20;
export const STORE_REVIEW_TITLE_MAX = 120;
export const STORE_REVIEW_BODY_MIN = 3;
export const STORE_REVIEW_BODY_MAX = 2000;

const MODERATED_VISIBILITIES = ['hidden', 'removed'];
const STAR_VALUES = [1, 2, 3, 4, 5] as const;
const FILTER_ALL = 'all';
const LOW_RATING_THRESHOLD = 3;

type ComposeMode = 'signIn' | 'suspended' | 'notEntitled' | 'unavailable' | 'form';

@Component({
  selector: 'app-store-reviews-section',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    NgTemplateOutlet,
    AvatarComponent,
    ButtonComponent,
    ConfirmationModalComponent,
    InputComponent,
    ModalComponent,
    SelectComponent,
    StoreRatingStarsComponent,
    StoreReportDialogComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-reviews-section.component.html',
  styleUrls: ['./store-reviews-section.component.scss'],
  host: {
    '[style.display]': "available() ? null : 'none'",
  },
})
export class StoreReviewsSectionComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly account = inject(ConnectAccountService);
  private readonly settingsModal = inject(SettingsModalService);
  private readonly ratingsService = inject(StoreRatingsService);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly kind = input.required<StoreExtensionKind>();
  readonly id = input.required<string>();
  readonly repository = input<string | null>(null);

  protected readonly starValues = STAR_VALUES;
  protected readonly starPath = STORE_STAR_PATH;

  protected readonly aggregate = signal<GetStoreRatingResponse | null>(null);
  readonly available = computed(() => this.aggregate()?.available === true);

  protected readonly reviews = signal<StoreReviewBody[]>([]);
  protected readonly totalReviews = signal(0);
  protected readonly reviewsLoading = signal(false);
  protected readonly reviewsFailed = signal(false);
  protected readonly sort = signal<StoreReviewSortOrder>('Newest');
  protected readonly filter = signal<string>(FILTER_ALL);
  private reviewsPage = 0;
  private reviewsGeneration = 0;

  protected readonly own = signal<GetStoreOwnReviewResponse | null>(null);
  private ownGeneration = 0;

  protected readonly formRating = signal<number | null>(null);
  protected readonly formTitle = signal('');
  protected readonly formBody = signal('');
  protected readonly submitted = signal(false);
  protected readonly busy = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly writeStatus = signal<string | null>(null);
  protected readonly deleteConfirmOpen = signal(false);
  protected readonly editorOpen = signal(false);
  protected readonly reportTarget = signal<StoreReportTarget | null>(null);
  protected readonly reportedReviewIds = signal<ReadonlySet<string>>(new Set());

  protected readonly ratingText = computed(() => {
    const rating = this.aggregate()?.rating;
    return rating === null || rating === undefined ? null : formatStoreRating(rating, this.localization.culture());
  });

  protected readonly ratingCountText = computed(() => this.localization.translateKey(
    AppStrings.Store.Reviews.RatingCount, { count: this.aggregate()?.ratingCount ?? 0 }));

  protected readonly distribution = computed(() => {
    const aggregate = this.aggregate();
    const total = aggregate?.ratingCount ?? 0;
    return [5, 4, 3, 2, 1].map(stars => {
      const count = aggregate?.distribution?.find(bucket => bucket.stars === stars)?.count ?? 0;
      return {
        stars,
        count,
        total,
        percent: total > 0 ? Math.round((count / total) * 100) : 0,
        label: this.starCountLabel(stars),
        valueText: this.localization.translateKey(AppStrings.Store.Reviews.RatingCount, { count }),
      };
    });
  });

  protected readonly sortOptions = computed<SelectOption[]>(() => [
    { value: 'Newest', label: this.localization.translateKey(AppStrings.Store.Reviews.SortNewest) },
    { value: 'Oldest', label: this.localization.translateKey(AppStrings.Store.Reviews.SortOldest) },
  ]);

  protected readonly filterOptions = computed<SelectOption[]>(() => [
    { value: FILTER_ALL, label: this.localization.translateKey(AppStrings.Store.Reviews.FilterAll) },
    ...[5, 4, 3, 2, 1].map(stars => ({ value: String(stars), label: this.starCountLabel(stars) })),
  ]);

  protected readonly hasMoreReviews = computed(() => this.reviews().length < this.totalReviews());

  protected readonly issueUrl = computed(() => githubNewIssueUrl(this.repository()));

  protected readonly suggestIssue = computed(() => {
    const rating = this.formRating();
    return this.issueUrl() !== null && rating !== null && rating < LOW_RATING_THRESHOLD;
  });

  protected readonly ownReview = computed(() => this.own()?.review ?? null);

  protected readonly ownReviewId = computed(() => this.ownReview()?.id ?? null);

  protected readonly moderated = computed(() => {
    const visibility = this.ownReview()?.visibility?.toLowerCase();
    return visibility !== undefined && MODERATED_VISIBILITIES.includes(visibility);
  });

  protected readonly mode = computed<ComposeMode | null>(() => {
    const status = this.account.session()?.status;
    if (status === 'suspended') {
      return 'suspended';
    }
    if (status === 'signedOut' || status === 'reauthenticationRequired') {
      return 'signIn';
    }
    switch (this.own()?.state) {
      case 'SignedOut':
        return 'signIn';
      case 'NotEntitled':
        return 'notEntitled';
      case 'Unavailable':
        return 'unavailable';
      case 'Entitled':
        return 'form';
      default:
        return null;
    }
  });

  protected readonly canDelete = computed(() =>
    this.ownReview() !== null && !this.moderated() && !this.busy() && this.mode() === 'form');

  protected readonly bodyCounter = computed(() => this.localization.translateKey(
    AppStrings.Store.Reviews.BodyCounter, { length: codePoints(this.formBody()), max: STORE_REVIEW_BODY_MAX }));

  protected readonly validationErrors = computed(() => {
    const errors: { rating?: string; title?: string; body?: string } = {};
    const rating = this.formRating();
    if (rating === null || rating < 1 || rating > 5) {
      errors.rating = this.localization.translateKey(AppStrings.Store.Reviews.Validation.RatingRequired);
    }

    const title = this.formTitle().trim();
    if (/[\r\n]/.test(title)) {
      errors.title = this.localization.translateKey(AppStrings.Store.Reviews.Validation.TitleSingleLine);
    } else if (codePoints(title) > STORE_REVIEW_TITLE_MAX) {
      errors.title = this.localization.translateKey(AppStrings.Store.Reviews.Validation.TitleTooLong);
    }

    const body = this.formBody().trim();
    const loadedBody = (this.ownReview()?.body ?? '').trim();
    if (body.length > 0
      && (codePoints(body) < STORE_REVIEW_BODY_MIN || (body !== loadedBody && codePoints(body) > STORE_REVIEW_BODY_MAX))) {
      errors.body = this.localization.translateKey(AppStrings.Store.Reviews.Validation.BodyLength);
    }
    return errors;
  });

  protected readonly visibleErrors = computed(() => this.submitted() ? this.validationErrors() : {});

  constructor() {
    effect(() => {
      const kind = this.kind();
      const id = this.id();
      untracked(() => {
        void this.loadAggregate(kind, id);
        void this.loadReviews(true);
      });
    });

    effect(() => {
      const kind = this.kind();
      const id = this.id();
      this.account.session()?.status;
      untracked(() => void this.loadOwn(kind, id));
    });

    this.api.onNotification('StoreCatalogChangedEvent')
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.loadOwn(this.kind(), this.id()));
  }

  protected reviewAvatarUrl(review: StoreReviewBody): string | null {
    return review.authorAvatarUrl ? this.api.getStoreReviewAvatarUrl(review.authorAvatarUrl) : null;
  }

  protected starCountLabel(stars: number): string {
    return this.localization.translateKey(AppStrings.Store.Reviews.StarCount, { count: stars });
  }

  protected openSignIn(): void {
    this.settingsModal.open('account');
  }

  protected reviewReportLabel(review: StoreReviewBody): string {
    return this.localization.translateKey(AppStrings.Store.Report.ReviewAction, { author: review.authorDisplayName });
  }

  protected requestReport(review: StoreReviewBody): void {
    if (this.mode() === 'signIn') {
      this.openSignIn();
      return;
    }
    this.reportTarget.set({ type: 'review', reviewId: review.id });
  }

  protected closeReport(): void {
    this.reportTarget.set(null);
  }

  protected onReported(): void {
    const target = this.reportTarget();
    if (target?.type === 'review') {
      this.reportedReviewIds.update(ids => new Set([...ids, target.reviewId]));
      afterNextRender(() => this.element.nativeElement
        .querySelector<HTMLElement>(`[data-reported-review="${target.reviewId}"]`)?.focus(), { injector: this.injector });
    }
    this.reportTarget.set(null);
  }

  protected onSortChange(value: StoreReviewSortOrder): void {
    this.sort.set(value);
    void this.loadReviews(true);
  }

  protected onFilterChange(value: string): void {
    this.filter.set(value);
    void this.loadReviews(true);
  }

  protected loadMore(): void {
    void this.loadReviews(false);
  }

  protected openEditor(): void {
    if (this.mode() !== 'form' || this.moderated() || this.busy()) {
      return;
    }

    const review = this.ownReview();
    this.formRating.set(review?.rating ?? null);
    this.formTitle.set(review?.title ?? '');
    this.formBody.set(review?.body ?? '');
    this.submitted.set(false);
    this.writeError.set(null);
    this.writeStatus.set(null);
    this.editorOpen.set(true);
  }

  protected closeEditor(): void {
    if (!this.busy()) {
      this.editorOpen.set(false);
      this.writeError.set(null);
    }
  }

  protected selectRating(value: number): void {
    if (this.busy()) {
      return;
    }
    this.formRating.set(value);
    this.writeStatus.set(null);
  }

  protected onStarKeydown(event: KeyboardEvent, current: number): void {
    const step = event.key === 'ArrowRight' || event.key === 'ArrowUp' ? 1
      : event.key === 'ArrowLeft' || event.key === 'ArrowDown' ? -1
        : 0;
    let next: number | null = null;
    if (step !== 0) {
      next = ((current - 1 + step + STAR_VALUES.length) % STAR_VALUES.length) + 1;
    } else if (event.key === 'Home') {
      next = 1;
    } else if (event.key === 'End') {
      next = STAR_VALUES.length;
    }
    if (next === null) {
      return;
    }

    event.preventDefault();
    this.selectRating(next);
    const group = (event.currentTarget as HTMLElement).parentElement;
    group?.querySelector<HTMLElement>(`[data-star="${next}"]`)?.focus();
  }

  protected starTabIndex(value: number): number {
    const selected = this.formRating();
    return (selected ?? 1) === value ? 0 : -1;
  }

  protected async save(): Promise<void> {
    this.submitted.set(true);
    this.writeError.set(null);
    this.writeStatus.set(null);
    const errors = this.validationErrors();
    if (errors.rating || errors.title || errors.body || this.busy() || this.moderated()) {
      return;
    }

    const title = this.formTitle().trim();
    const body = this.formBody().trim();
    await this.write(
      () => this.api.putOwnStoreReview(this.kind(), this.id(), {
        rating: this.formRating()!,
        title: title || null,
        body: body || null,
      }),
      AppStrings.Store.Reviews.Saved);
  }

  protected requestDelete(): void {
    if (this.canDelete()) {
      this.deleteConfirmOpen.set(true);
    }
  }

  protected cancelDelete(): void {
    this.deleteConfirmOpen.set(false);
  }

  protected async confirmDelete(): Promise<void> {
    this.deleteConfirmOpen.set(false);
    this.writeError.set(null);
    this.writeStatus.set(null);
    await this.write(() => this.api.deleteOwnStoreReview(this.kind(), this.id()), AppStrings.Store.Reviews.Deleted);
  }

  private async write(request: () => Promise<StoreOwnReviewWriteResponse>, successKey: string): Promise<void> {
    const kind = this.kind();
    const id = this.id();
    this.busy.set(true);
    try {
      let response: StoreOwnReviewWriteResponse;
      try {
        response = await request();
      } catch (error) {
        console.error('Failed to write the store review:', error);
        this.writeError.set(this.errorMessage(null));
        return;
      }

      if (!response.success) {
        this.writeError.set(this.errorMessage(response.error ?? null));
        return;
      }

      this.submitted.set(false);
      this.editorOpen.set(false);
      this.writeStatus.set(this.localization.translateKey(successKey));
      this.ratingsService.invalidate(id);
      await Promise.all([this.loadAggregate(kind, id), this.loadReviews(true), this.loadOwn(kind, id)]);
    } finally {
      this.busy.set(false);
    }
  }

  private errorMessage(error: StoreReviewWriteError | null): string {
    const strings = AppStrings.Store.Reviews;
    const seconds = error?.retryAfterSeconds;
    const hasSeconds = typeof seconds === 'number' && seconds > 0;
    switch (error?.code) {
      case 'sign_in_required':
        return this.localization.translateKey(strings.Error.SignInRequired);
      case 'download_required':
        return this.localization.translateKey(strings.Error.DownloadRequired);
      case 'account_suspended':
        return this.localization.translateKey(strings.Error.AccountSuspended);
      case 'forbidden':
        return this.localization.translateKey(strings.Error.Forbidden);
      case 'moderated':
        return this.localization.translateKey(strings.Error.Moderated);
      case 'gone':
        return this.localization.translateKey(strings.Error.Gone);
      case 'cooldown':
        return hasSeconds
          ? this.localization.translateKey(strings.Error.CooldownSeconds, { count: Math.ceil(seconds) })
          : this.localization.translateKey(strings.Error.Cooldown);
      case 'retry_later':
        return hasSeconds
          ? this.localization.translateKey(strings.Error.RetryLaterSeconds, { count: Math.ceil(seconds) })
          : this.localization.translateKey(strings.Error.RetryLater);
      case 'validation':
        switch (error.field) {
          case 'Rating':
            return this.localization.translateKey(strings.Validation.RatingRequired);
          case 'Title':
            return this.localization.translateKey(strings.Error.InvalidTitle);
          default:
            return this.localization.translateKey(strings.Error.Validation);
        }
      case 'not_found':
        return this.localization.translateKey(strings.Error.NotFound);
      default:
        return this.localization.translateKey(strings.Error.PlatformUnavailable);
    }
  }

  private async loadAggregate(kind: StoreExtensionKind, id: string): Promise<void> {
    try {
      const response = await this.api.getStoreRating(kind, id);
      if (kind === this.kind() && id === this.id()) {
        this.aggregate.set(response);
      }
    } catch (error) {
      console.error('Failed to load the store rating:', error);
      if (kind === this.kind() && id === this.id()) {
        this.aggregate.set(null);
      }
    }
  }

  private async loadReviews(reset: boolean): Promise<void> {
    if (!reset && (this.reviewsLoading() || !this.hasMoreReviews())) {
      return;
    }

    const generation = ++this.reviewsGeneration;
    const page = reset ? 1 : this.reviewsPage + 1;
    const filter = this.filter();
    this.reviewsLoading.set(true);
    this.reviewsFailed.set(false);
    try {
      const response = await this.api.getStoreReviews(this.kind(), this.id(), {
        page,
        pageSize: STORE_REVIEWS_PAGE_SIZE,
        sort: this.sort(),
        rating: filter === FILTER_ALL ? null : Number(filter),
      });
      if (generation !== this.reviewsGeneration) {
        return;
      }

      if (!response.available) {
        this.reviewsFailed.set(true);
        return;
      }
      const items = response.items ?? [];
      this.reviews.update(current => reset ? items : [...current, ...items.filter(item =>
        !current.some(existing => existing.id === item.id))]);
      this.totalReviews.set(response.totalCount ?? 0);
      this.reviewsPage = page;
    } catch (error) {
      console.error('Failed to load store reviews:', error);
      if (generation === this.reviewsGeneration) {
        this.reviewsFailed.set(true);
      }
    } finally {
      if (generation === this.reviewsGeneration) {
        this.reviewsLoading.set(false);
      }
    }
  }

  private async loadOwn(kind: StoreExtensionKind, id: string): Promise<void> {
    const generation = ++this.ownGeneration;
    let response: GetStoreOwnReviewResponse;
    try {
      response = await this.api.getOwnStoreReview(kind, id);
    } catch (error) {
      console.error('Failed to load the own store review:', error);
      response = { state: 'Unavailable', review: null };
    }
    if (generation !== this.ownGeneration) {
      return;
    }

    this.own.set(response);
  }
}

function codePoints(value: string): number {
  return [...value].length;
}

function githubNewIssueUrl(repository: string | null): string | null {
  if (!repository) {
    return null;
  }

  let url: URL;
  try {
    url = new URL(repository);
  } catch {
    return null;
  }

  const [owner, name] = url.pathname.split('/').filter(Boolean);
  if (url.protocol !== 'https:' || url.hostname.toLowerCase() !== 'github.com' || !owner || !name) {
    return null;
  }

  return `https://github.com/${encodeURIComponent(owner)}/${encodeURIComponent(name.replace(/\.git$/i, ''))}/issues/new`;
}
