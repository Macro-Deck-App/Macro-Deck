import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetStoreRatingPromptResponse, StoreOwnReviewWriteResponse, StoreRatingPromptCandidateBody } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { provideLocalizationTesting } from '../../testing/localization-test-support';
import { ConnectAccountService } from './connect-account.service';
import {
  STORE_RATING_PROMPT_MAX_RETRIES,
  STORE_RATING_PROMPT_RECHECK_MS,
  STORE_RATING_PROMPT_RETRY_MS,
  StoreRatingPromptService,
} from './store-rating-prompt.service';

describe('StoreRatingPromptService', () => {
  const candidate: StoreRatingPromptCandidateBody = { kind: 'Plugin', id: 'com.acme.hue', name: 'Hue Bridge', hasIcon: false };

  let connection: ReturnType<typeof signal<string>>;
  let signedIn: ReturnType<typeof signal<boolean>>;
  let getPrompt: jasmine.Spy<() => Promise<GetStoreRatingPromptResponse>>;
  let markShown: jasmine.Spy;
  let putReview: jasmine.Spy<(...args: unknown[]) => Promise<StoreOwnReviewWriteResponse>>;

  beforeEach(() => {
    jasmine.clock().install();
    connection = signal('connected');
    signedIn = signal(true);
    getPrompt = jasmine.createSpy('getStoreRatingPrompt').and.resolveTo({ ready: true, candidate });
    markShown = jasmine.createSpy('markStoreRatingPromptShown').and.resolveTo(undefined);
    putReview = jasmine.createSpy('putOwnStoreReview').and.resolveTo({ success: true });
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: ApiService,
          useValue: {
            onNotification: () => EMPTY,
            connectionStateSignal: connection,
            getStoreRatingPrompt: getPrompt,
            markStoreRatingPromptShown: markShown,
            putOwnStoreReview: putReview,
          },
        },
        { provide: ConnectAccountService, useValue: { isSignedIn: signedIn } },
      ],
    });
  });

  afterEach(() => jasmine.clock().uninstall());

  async function start(): Promise<StoreRatingPromptService> {
    const service = TestBed.inject(StoreRatingPromptService);
    TestBed.tick();
    await flush();
    return service;
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
  }

  async function advance(ms: number): Promise<void> {
    jasmine.clock().tick(ms);
    await flush();
  }

  it('asks the host for a package once connected and signed in', async () => {
    const service = await start();

    expect(getPrompt).toHaveBeenCalledTimes(1);
    expect(service.pending()).toEqual(candidate);
  });

  it('asks nothing while signed out and starts once the user signs in', async () => {
    signedIn.set(false);
    const service = await start();
    expect(getPrompt).not.toHaveBeenCalled();

    signedIn.set(true);
    TestBed.tick();
    await flush();

    expect(service.pending()).toEqual(candidate);
  });

  it('retries while the host is still loading the catalog, then gives up after a bounded number of tries', async () => {
    getPrompt.and.resolveTo({ ready: false });
    const service = await start();
    expect(service.pending()).toBeNull();

    getPrompt.and.resolveTo({ ready: true, candidate });
    await advance(STORE_RATING_PROMPT_RETRY_MS);
    expect(service.pending()).toEqual(candidate);

    TestBed.resetTestingModule();
  });

  it('stops retrying an unready host after the retry limit', async () => {
    getPrompt.and.resolveTo({ ready: false });
    await start();

    for (let i = 0; i < STORE_RATING_PROMPT_MAX_RETRIES + 3; i++) {
      await advance(STORE_RATING_PROMPT_RETRY_MS);
    }

    expect(getPrompt).toHaveBeenCalledTimes(STORE_RATING_PROMPT_MAX_RETRIES + 1);
  });

  it('looks again after a few hours when nothing is due, for an app left open for days', async () => {
    getPrompt.and.resolveTo({ ready: true, candidate: null });
    const service = await start();
    expect(service.pending()).toBeNull();

    getPrompt.and.resolveTo({ ready: true, candidate });
    await advance(STORE_RATING_PROMPT_RECHECK_MS);

    expect(service.pending()).toEqual(candidate);
  });

  it('reports the prompt as shown once, and only when it is actually shown', async () => {
    const service = await start();
    expect(markShown).not.toHaveBeenCalled();

    service.markShown();
    service.markShown();

    expect(markShown).toHaveBeenCalledOnceWith({ kind: 'Plugin', id: 'com.acme.hue' });
  });

  it('closes the prompt on dismiss without sending anything', async () => {
    const service = await start();

    service.dismiss();

    expect(service.pending()).toBeNull();
    expect(putReview).not.toHaveBeenCalled();
  });

  it('sends the chosen rating without a review text and closes the prompt', async () => {
    const service = await start();

    const result = await service.submit(4, '  ');

    expect(result).toEqual({ ok: true });
    expect(putReview).toHaveBeenCalledOnceWith('Plugin', 'com.acme.hue', { rating: 4, body: null });
    expect(service.pending()).toBeNull();
  });

  it('sends an optional review text along with the rating', async () => {
    const service = await start();

    await service.submit(5, ' Works great ');

    expect(putReview).toHaveBeenCalledOnceWith('Plugin', 'com.acme.hue', { rating: 5, body: 'Works great' });
  });

  it('keeps the prompt open with a message when sending fails', async () => {
    putReview.and.resolveTo({ success: false, error: { code: 'validation', field: 'Body' } });
    const service = await start();

    const result = await service.submit(5, 'a');

    expect(result.ok).toBeFalse();
    expect(result.ok ? '' : result.message).toContain('3 and 2000');
    expect(service.pending()).toEqual(candidate);
  });

  it('keeps the prompt open when the request itself fails', async () => {
    putReview.and.rejectWith(new Error('offline'));
    spyOn(console, 'error');
    const service = await start();

    const result = await service.submit(5, '');

    expect(result.ok).toBeFalse();
    expect(service.pending()).toEqual(candidate);
  });
});
