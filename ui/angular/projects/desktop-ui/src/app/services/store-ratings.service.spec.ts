import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { GetStoreRatingsResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { StoreRatingsService } from './store-ratings.service';

describe('StoreRatingsService', () => {
  let service: StoreRatingsService;
  let api: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;

  function ratingsFor(ids: readonly string[]): GetStoreRatingsResponse {
    return {
      available: true,
      ratings: Object.fromEntries(ids.map(id => [id, { rating: 4, ratingCount: 3 }])),
    };
  }

  beforeEach(() => {
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getStoreRatings', 'onNotification']);
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    api.getStoreRatings.and.callFake(async ids => ratingsFor(ids));

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    });
    service = TestBed.inject(StoreRatingsService);
  });

  it('requests only ids it does not already know', async () => {
    await service.ensure(['a', 'b']);
    await service.ensure(['a', 'b', 'c']);

    expect(api.getStoreRatings.calls.allArgs().map(([ids]) => [...ids])).toEqual([['a', 'b'], ['c']]);
    expect(service.summary('c')).toEqual({ rating: 4, ratingCount: 3 });
  });

  it('does not ask again for an id whose request is still in flight', async () => {
    let release!: () => void;
    api.getStoreRatings.and.callFake(ids => new Promise(resolve => {
      release = () => resolve(ratingsFor(ids));
    }));

    const first = service.ensure(['a']);
    const second = service.ensure(['a']);
    release();
    await Promise.all([first, second]);

    expect(api.getStoreRatings).toHaveBeenCalledTimes(1);
  });

  it('sends at most 100 ids per request', async () => {
    const ids = Array.from({ length: 250 }, (_, index) => `pkg-${index}`);

    await service.ensure(ids);

    const sent = api.getStoreRatings.calls.allArgs().map(([chunk]) => chunk);
    expect(sent.every(chunk => chunk.length <= 100)).toBeTrue();
    expect(new Set(sent.flat()).size).toBe(250);
  });

  it('leaves the map empty when the request fails or the ratings are unavailable', async () => {
    api.getStoreRatings.and.rejectWith(new Error('offline'));
    await service.ensure(['a']);
    expect(service.ratings().size).toBe(0);

    api.getStoreRatings.and.resolveTo({ available: false, ratings: {} });
    await service.ensure(['a']);
    expect(service.ratings().size).toBe(0);
  });

  it('asks again once an entry has expired', async () => {
    jasmine.clock().install();
    try {
      jasmine.clock().mockDate(new Date('2026-09-16T12:00:00Z'));
      await service.ensure(['a']);
      jasmine.clock().mockDate(new Date('2026-09-16T12:04:00Z'));
      await service.ensure(['a']);
      expect(api.getStoreRatings).toHaveBeenCalledTimes(1);

      jasmine.clock().mockDate(new Date('2026-09-16T12:05:01Z'));
      await service.ensure(['a']);
      expect(api.getStoreRatings).toHaveBeenCalledTimes(2);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('forgets everything when the catalog changes', async () => {
    await service.ensure(['a']);

    notifications.get('StoreCatalogChangedEvent')!.next({});
    expect(service.ratings().size).toBe(0);
    await service.ensure(['a']);

    expect(api.getStoreRatings).toHaveBeenCalledTimes(2);
  });
});
