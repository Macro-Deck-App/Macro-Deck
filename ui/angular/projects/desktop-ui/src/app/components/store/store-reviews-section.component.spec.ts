import { formatDate } from '@angular/common';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Observable, Subject } from 'rxjs';

import {
  AppStrings,
  GetConnectSessionResponse,
  GetStoreOwnReviewResponse,
  GetStoreRatingResponse,
  GetStoreReviewsResponse,
  StoreOwnReviewBody,
  StoreReviewBody,
} from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';
import { ConnectAccountService } from '../../services/connect-account.service';
import { SettingsModalService } from '../../services/settings-modal.service';
import { StoreRatingsService } from '../../services/store-ratings.service';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { StoreReviewsSectionComponent } from './store-reviews-section.component';

type SessionStatus = GetConnectSessionResponse['status'];

function review(overrides: Partial<StoreReviewBody> = {}): StoreReviewBody {
  return {
    id: 'review-1',
    rating: 4,
    title: 'Works well',
    body: 'Does what it says.',
    authorDisplayName: 'Ada Lovelace',
    authorAvatarUrl: null,
    createdAt: '2026-09-01T10:00:00Z',
    isEdited: false,
    downloadedBeforeReview: false,
    ...overrides,
  };
}

function ownReview(overrides: Partial<StoreOwnReviewBody> = {}): StoreOwnReviewBody {
  return {
    id: 'own-review',
    rating: 3,
    title: 'Mine',
    body: 'My own words.',
    visibility: 'Visible',
    moderationReason: null,
    createdAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    isEdited: false,
    ...overrides,
  };
}

interface Scenario {
  status?: SessionStatus;
  rating?: Partial<GetStoreRatingResponse>;
  reviews?: StoreReviewBody[];
  own?: GetStoreOwnReviewResponse;
  repository?: string | null;
  publisher?: string | null;
}

describe('StoreReviewsSectionComponent', () => {
  let fixture: ComponentFixture<StoreReviewsSectionComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let settingsModal: jasmine.SpyObj<SettingsModalService>;
  let ratings: jasmine.SpyObj<StoreRatingsService>;
  let session: ReturnType<typeof signal<GetConnectSessionResponse | null>>;

  beforeEach(() => {
    for (const key of Object.keys(localStorage).filter(key => key.startsWith('md.localization.'))) {
      localStorage.removeItem(key);
    }
  });

  async function setup(scenario: Scenario = {}): Promise<void> {
    const notifications = new Map<string, Subject<unknown>>();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getStoreRating', 'getStoreReviews', 'getOwnStoreReview', 'putOwnStoreReview', 'deleteOwnStoreReview',
      'getStoreReviewAvatarUrl', 'onNotification', 'reportStoreReview',
    ]);
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    api.getStoreRating.and.resolveTo({
      available: true,
      rating: 4,
      ratingCount: 1,
      distribution: [{ stars: 4, count: 1 }],
      ...scenario.rating,
    });
    const items = scenario.reviews ?? [review()];
    api.getStoreReviews.and.resolveTo({
      available: true,
      items,
      page: 1,
      pageSize: 20,
      totalCount: items.length,
      reviewCount: items.length,
    } satisfies GetStoreReviewsResponse);
    api.getOwnStoreReview.and.resolveTo(scenario.own ?? { state: 'Entitled', review: null });
    api.getStoreReviewAvatarUrl.and.callFake(path => `http://host${path}`);

    session = signal<GetConnectSessionResponse | null>({ status: scenario.status ?? 'signedIn' } as GetConnectSessionResponse);
    settingsModal = jasmine.createSpyObj<SettingsModalService>('SettingsModalService', ['open']);
    ratings = jasmine.createSpyObj<StoreRatingsService>('StoreRatingsService', ['invalidate']);

    TestBed.configureTestingModule({
      imports: [StoreReviewsSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api },
        { provide: ConnectAccountService, useValue: { session } },
        { provide: SettingsModalService, useValue: settingsModal },
        { provide: StoreRatingsService, useValue: ratings },
      ],
    });

    fixture = TestBed.createComponent(StoreReviewsSectionComponent);
    fixture.componentRef.setInput('kind', 'Plugin');
    fixture.componentRef.setInput('id', 'com.acme.deck-tools');
    fixture.componentRef.setInput('repository', scenario.repository ?? null);
    fixture.componentRef.setInput('publisher', scenario.publisher ?? null);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let pass = 0; pass < 3; pass++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
    fixture.detectChanges();
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(key: string, args?: Record<string, unknown>): string {
    return TestBed.inject(LocalizationService).translateKey(key, args);
  }

  async function openEditor(): Promise<void> {
    host().querySelector<HTMLButtonElement>('.reviews-write button, .reviews-edit button')!.click();
    await settle();
  }

  function starOptions(): HTMLButtonElement[] {
    return Array.from(host().querySelectorAll<HTMLButtonElement>('[role="radiogroup"] [role="radio"]'));
  }

  it('shows a creator reply inside the review it answers and nowhere else', async () => {
    await setup({
      publisher: 'Acme Tools',
      reviews: [
        review({ id: 'unanswered', body: 'No reply here.' }),
        review({
          id: 'answered',
          body: 'Crashes on start.',
          reply: {
            body: 'Fixed in 1.2,\nplease update.',
            createdAt: '2026-09-02T12:00:00Z',
            updatedAt: '2026-09-03T12:00:00Z',
            isEdited: true,
          },
        }),
      ],
    });

    const [unanswered, answered] = Array.from(host().querySelectorAll<HTMLElement>('.reviews-list > li'));
    const reply = answered.querySelector<HTMLElement>('.review-reply')!;

    expect(host().querySelectorAll('.reviews-list > li').length).toBe(2);
    expect(unanswered.querySelector('.review-reply')).toBeNull();
    expect(unanswered.textContent).not.toContain(text(AppStrings.Store.Reviews.DeveloperResponse));
    expect(reply.textContent).toContain(text(AppStrings.Store.Reviews.DeveloperResponse));
    expect(reply.textContent).toContain('Acme Tools');
    expect(reply.textContent).toContain('Fixed in 1.2,\nplease update.');
    expect(reply.textContent).toContain(formatDate('2026-09-02T12:00:00Z', 'mediumDate', 'en-US'));
    expect(reply.textContent).toContain(text(AppStrings.Store.Reviews.ReplyEdited));
  });

  it('marks an unedited reply as a developer response even when the publisher is unknown', async () => {
    await setup({
      publisher: null,
      reviews: [
        review({
          reply: { body: 'Thanks!', createdAt: '2026-09-02T08:00:00Z', updatedAt: '2026-09-02T08:00:00Z', isEdited: false },
        }),
      ],
    });

    const reply = host().querySelector<HTMLElement>('.review-reply')!;

    expect(reply.textContent).toContain(text(AppStrings.Store.Reviews.DeveloperResponse));
    expect(reply.textContent).toContain('Thanks!');
    expect(reply.textContent).not.toContain(text(AppStrings.Store.Reviews.ReplyEdited));
  });

  it('prompts a signed-out reader to sign in and opens the account settings', async () => {
    await setup({ status: 'signedOut', own: { state: 'SignedOut' } });

    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.SignInPrompt));
    expect(host().querySelector('form')).toBeNull();

    host().querySelector<HTMLButtonElement>('.reviews-sign-in button')!.click();

    expect(settingsModal.open).toHaveBeenCalledOnceWith('account');
  });

  it('prompts for sign-in again when the Connect session needs reauthentication', async () => {
    await setup({ status: 'reauthenticationRequired', own: { state: 'SignedOut' } });

    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.SignInPrompt));
  });

  it('explains that the item has to be installed before it can be rated', async () => {
    await setup({ own: { state: 'NotEntitled', review: null } });

    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.NotEntitled));
    expect(host().querySelector('form')).toBeNull();
  });

  it('renders review text literally instead of as markup', async () => {
    const hostile = '<img src=x onerror=alert(1)>';
    await setup({ reviews: [review({ title: hostile, body: hostile })] });

    const item = host().querySelector('.reviews-list li')!;
    expect(item.querySelector('img')).toBeNull();
    expect(item.textContent).toContain(hostile);
  });

  it('hides the whole section while ratings are unavailable', async () => {
    await setup({ rating: { available: false, rating: null, ratingCount: 0, distribution: [] } });

    expect(host().textContent!.trim()).toBe('');
    expect(host().style.display).toBe('none');
  });

  it('shows the cooldown message when the Platform refuses a save', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview() } });
    api.putOwnStoreReview.and.resolveTo({ success: false, error: { code: 'cooldown', retryAfterSeconds: 30 } });
    await openEditor();

    host().querySelector<HTMLButtonElement>('.reviews-save button')!.click();
    await settle();

    expect(host().querySelector('[role="alert"]')?.textContent)
      .toContain(text(AppStrings.Store.Reviews.Error.CooldownSeconds, { count: 30 }));
  });

  it('shows the cooldown message without a wait when the Platform gives none', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview() } });
    api.putOwnStoreReview.and.resolveTo({ success: false, error: { code: 'cooldown' } });
    await openEditor();

    host().querySelector<HTMLButtonElement>('.reviews-save button')!.click();
    await settle();

    expect(host().querySelector('[role="alert"]')?.textContent).toContain(text(AppStrings.Store.Reviews.Error.Cooldown));
  });

  it('holds a moderated own review: notice, reason as text, and nothing editable or deletable', async () => {
    await setup({
      own: { state: 'Entitled', review: ownReview({ visibility: 'Hidden', moderationReason: '<b>Spam</b>' }) },
    });

    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.ModeratedNotice));
    expect(host().textContent).toContain('<b>Spam</b>');
    expect(host().querySelector('.reviews-moderated b')).toBeNull();
    expect(host().querySelector<HTMLButtonElement>('.reviews-edit button')!.disabled).toBeTrue();
    expect(host().querySelector('.reviews-delete')).toBeNull();
    expect(host().querySelector('form')).toBeNull();
  });

  it('lets the arrow keys move the star selection', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview({ rating: 3 }) } });
    await openEditor();
    const checked = () => starOptions().findIndex(option => option.getAttribute('aria-checked') === 'true') + 1;
    expect(checked()).toBe(3);

    starOptions()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await settle();
    expect(checked()).toBe(4);
    expect(document.activeElement).toBe(starOptions()[3]);

    starOptions()[3].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    starOptions()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    await settle();
    expect(checked()).toBe(2);
  });

  it('refuses to save without a rating', async () => {
    await setup({ own: { state: 'Entitled', review: null } });
    await openEditor();

    host().querySelector<HTMLButtonElement>('.reviews-save button')!.click();
    await settle();

    expect(api.putOwnStoreReview).not.toHaveBeenCalled();
    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.Validation.RatingRequired));
  });

  it('clears a removed title and body with null and refreshes the ratings after saving', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview({ rating: 5 }) } });
    api.putOwnStoreReview.and.resolveTo({ success: true, review: ownReview({ rating: 5, title: null, body: null }) });
    await openEditor();
    const title = host().querySelector<HTMLInputElement>('.reviews-title-input input')!;
    title.value = '   ';
    title.dispatchEvent(new Event('input'));
    const body = host().querySelector<HTMLTextAreaElement>('.reviews-body-input textarea')!;
    body.value = '';
    body.dispatchEvent(new Event('input'));
    await settle();

    host().querySelector<HTMLButtonElement>('.reviews-save button')!.click();
    await settle();

    expect(api.putOwnStoreReview).toHaveBeenCalledOnceWith('Plugin', 'com.acme.deck-tools', { rating: 5, title: null, body: null });
    expect(ratings.invalidate).toHaveBeenCalledWith('com.acme.deck-tools');
    expect(api.getStoreRating).toHaveBeenCalledTimes(2);
    expect(api.getOwnStoreReview).toHaveBeenCalledTimes(2);
    expect(host().querySelector('form')).toBeNull();
  });

  it('keeps a review being written when the session refreshes', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview() } });
    await openEditor();
    const body = host().querySelector<HTMLTextAreaElement>('.reviews-body-input textarea')!;
    body.value = 'A draft that is not saved yet';
    body.dispatchEvent(new Event('input'));
    await settle();

    session.set({ status: 'signedIn' } as GetConnectSessionResponse);
    await settle();

    expect(api.getOwnStoreReview).toHaveBeenCalledTimes(2);
    expect(host().querySelector<HTMLTextAreaElement>('.reviews-body-input textarea')!.value)
      .toBe('A draft that is not saved yet');
  });

  it('counts a title in characters the way the Platform does, not in UTF-16 units', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview({ rating: 4 }) } });
    api.putOwnStoreReview.and.resolveTo({ success: true, review: ownReview({ rating: 4 }) });
    await openEditor();
    const title = host().querySelector<HTMLInputElement>('.reviews-title-input input')!;
    title.value = '\u{1F680}'.repeat(100);
    title.dispatchEvent(new Event('input'));
    await settle();

    host().querySelector<HTMLButtonElement>('.reviews-save button')!.click();
    await settle();

    expect(api.putOwnStoreReview).toHaveBeenCalled();
  });

  it('keeps the section compact and writes a review in a modal', async () => {
    await setup({ own: { state: 'Entitled', review: null } });

    expect(host().querySelector('form')).toBeNull();
    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.WritePrompt));

    await openEditor();

    const dialog = host().querySelector('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog!.querySelector('form')).not.toBeNull();
    expect(dialog!.textContent).toContain(text(AppStrings.Store.Reviews.WriteHeading));
  });

  it('suggests opening a GitHub issue first when rating below three stars', async () => {
    await setup({ own: { state: 'Entitled', review: null }, repository: 'https://github.com/acme/deck-tools.git' });
    await openEditor();

    starOptions()[1].click();
    await settle();
    const link = host().querySelector<HTMLAnchorElement>('.reviews-issue-hint a');
    expect(link?.href).toBe('https://github.com/acme/deck-tools/issues/new');
    expect(link?.textContent).toContain(text(AppStrings.Store.Reviews.IssueAction));

    starOptions()[2].click();
    await settle();
    expect(host().querySelector('.reviews-issue-hint')).toBeNull();
  });

  it('offers no issue link when the repository is not on GitHub', async () => {
    await setup({ own: { state: 'Entitled', review: null }, repository: 'https://gitlab.com/acme/deck-tools' });
    await openEditor();

    starOptions()[0].click();
    await settle();

    expect(host().querySelector('.reviews-issue-hint')).toBeNull();
  });

  it('offers deleting the own review only inside the edit dialog and deletes it after confirmation', async () => {
    await setup({ own: { state: 'Entitled', review: ownReview() } });
    api.deleteOwnStoreReview.and.resolveTo({ success: true });

    expect(host().querySelector('.reviews-delete')).toBeNull();
    await openEditor();
    const deleteButton = host().querySelector<HTMLButtonElement>('[role="dialog"] .reviews-delete button');
    expect(deleteButton?.textContent).toContain(text(AppStrings.Store.Reviews.DeleteAction));

    deleteButton!.click();
    await settle();
    const confirmation = fixture.debugElement.query(By.directive(ConfirmationModalComponent));
    expect(confirmation).not.toBeNull();
    (confirmation.componentInstance as ConfirmationModalComponent).confirm.emit();
    await settle();

    expect(api.deleteOwnStoreReview).toHaveBeenCalledOnceWith('Plugin', 'com.acme.deck-tools');
    expect(host().querySelector('form')).toBeNull();
  });

  describe('reporting a review', () => {
    function reportButton(reviewId: string): HTMLButtonElement | null {
      const items = Array.from(host().querySelectorAll<HTMLElement>('.reviews-list li'));
      const index = currentReviews.findIndex(item => item.id === reviewId);
      return items[index]?.querySelector<HTMLButtonElement>('.review-report button') ?? null;
    }

    function reasonLabels(): string[] {
      return Array.from(host().querySelectorAll<HTMLElement>('.report-reason')).map(label => label.textContent!.trim());
    }

    function chooseReason(label: string): void {
      const option = Array.from(host().querySelectorAll<HTMLLabelElement>('.report-reason'))
        .find(item => item.textContent!.trim() === label)!;
      option.querySelector<HTMLInputElement>('input')!.click();
    }

    async function typeDetail(value: string): Promise<void> {
      const field = host().querySelector<HTMLTextAreaElement>('.report-detail-input textarea')!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
      await settle();
    }

    async function submit(): Promise<void> {
      host().querySelector<HTMLButtonElement>('.report-submit button')!.click();
      await settle();
    }

    let currentReviews: StoreReviewBody[];

    async function setupReviews(scenario: Scenario = {}): Promise<void> {
      currentReviews = scenario.reviews ?? [
        review({ id: 'own-review', authorDisplayName: 'Me' }),
        review({ id: 'review-2', authorDisplayName: 'Grace Hopper' }),
      ];
      await setup({ own: { state: 'Entitled', review: ownReview() }, ...scenario, reviews: currentReviews });
    }

    it('offers Report on every review except the reader\'s own, named after its author', async () => {
      await setupReviews();

      expect(reportButton('own-review')).toBeNull();
      expect(reportButton('review-2')?.getAttribute('aria-label'))
        .toBe(text(AppStrings.Store.Report.ReviewAction, { author: 'Grace Hopper' }));
    });

    it('sends a signed-out reader to sign in instead of opening the report', async () => {
      await setupReviews({ status: 'signedOut', own: { state: 'SignedOut' } });

      reportButton('review-2')!.click();
      await settle();

      expect(settingsModal.open).toHaveBeenCalledOnceWith('account');
      expect(host().querySelector('app-store-report-dialog')).toBeNull();
    });

    it('offers the review reasons, needs a description for Other and confirms the report', async () => {
      await setupReviews();
      api.reportStoreReview.and.resolveTo({ success: true });
      reportButton('review-2')!.click();
      await settle();

      expect(reasonLabels()).toEqual([
        text(AppStrings.Store.Report.Reason.Spam),
        text(AppStrings.Store.Report.Reason.Abuse),
        text(AppStrings.Store.Report.Reason.OffTopic),
        text(AppStrings.Store.Report.Reason.Other),
      ]);

      chooseReason(text(AppStrings.Store.Report.Reason.Other));
      await settle();
      await submit();

      expect(host().textContent).toContain(text(AppStrings.Store.Report.Validation.DetailRequired));
      expect(api.reportStoreReview).not.toHaveBeenCalled();

      await typeDetail('  Links to a scam site  ');
      await submit();

      expect(api.reportStoreReview).toHaveBeenCalledOnceWith('Plugin', 'com.acme.deck-tools', 'review-2', {
        category: 'Other',
        detail: 'Links to a scam site',
      });
      expect(host().querySelector('app-store-report-dialog')).toBeNull();
      expect(TestBed.inject(ToastService).toasts().map(toast => toast.message))
        .toContain(text(AppStrings.Store.Report.Received));
      expect(reportButton('review-2')).toBeNull();
      expect(host().querySelector('.review-reported')?.textContent).toContain(text(AppStrings.Store.Report.Reported));
      expect(document.activeElement).toBe(host().querySelector('.review-reported'));
    });

    it('tells the reader to wait when the Platform limits reports', async () => {
      await setupReviews();
      api.reportStoreReview.and.resolveTo({ success: false, error: { code: 'cooldown', retryAfterSeconds: 30 } });
      reportButton('review-2')!.click();
      await settle();

      chooseReason(text(AppStrings.Store.Report.Reason.Spam));
      await settle();
      await submit();

      expect(host().querySelector('.report-error')?.textContent)
        .toContain(text(AppStrings.Store.Report.Error.CooldownSeconds, { count: 30 }));
      expect(host().querySelector('app-store-report-dialog')).not.toBeNull();
    });
  });

  describe('compact layout', () => {
    function host(): HTMLElement {
      return fixture.nativeElement as HTMLElement;
    }

    function translate(key: string, params?: Record<string, unknown>): string {
      return TestBed.inject(LocalizationService).translateKey(key, params);
    }

    it('says there are no ratings yet instead of drawing an empty distribution', async () => {
      await setup({ rating: { available: true, rating: null, ratingCount: 0, distribution: [] }, reviews: [] });

      expect(host().querySelector('.reviews-distribution')).toBeNull();
      expect(host().querySelector('.reviews-controls')).toBeNull();
      expect(host().textContent).toContain(translate('macrodeck.app:Store.Reviews.NoRatingsYet'));
    });

    it('explains an empty list when every rating came without text', async () => {
      await setup({ rating: { available: true, rating: 5, ratingCount: 2, distribution: [{ stars: 5, count: 2 }] }, reviews: [] });

      expect(host().querySelector('.reviews-distribution')).not.toBeNull();
      expect(host().querySelector('.reviews-empty')?.textContent).toContain(translate('macrodeck.app:Store.Reviews.OnlyRatings'));
    });

    it('shortens a long review until the reader asks for the rest', async () => {
      await setup({ reviews: [review({ body: 'Works well. '.repeat(80) })] });
      const body = () => host().querySelector('.reviews-list .review-body')!;

      expect(body().classList).toContain('clamped');
      (host().querySelector('.review-more') as HTMLElement).click();
      await settle();
      expect(body().classList).not.toContain('clamped');
    });

    it('does not offer to expand a short review', async () => {
      await setup({ reviews: [review({ body: 'Short and sweet.' })] });

      expect(host().querySelector('.review-more')).toBeNull();
    });

    it("marks the reader's own review in the list without offering to report it", async () => {
      const own = ownReview({ rating: 5 });
      await setup({ own: { state: 'Entitled', review: own }, reviews: [review({ id: own.id })] });

      const item = host().querySelector('.reviews-list .review')!;
      expect(item.classList).toContain('review--mine');
      expect(item.querySelector('.review-report')).toBeNull();
    });
  });
});
