import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { IconPrefetchService } from './icon-prefetch.service';
import { IconImageService } from './icon-image.service';

describe('IconPrefetchService', () => {
  let service: IconPrefetchService;
  let loaded: string[];
  let resolvers: Array<() => void>;

  function loader(url: string): Promise<void> {
    loaded.push(url);
    return new Promise<void>(resolve => resolvers.push(resolve));
  }

  beforeEach(() => {
    loaded = [];
    resolvers = [];

    const iconImageStub = {
      getIconUrl: (iconId: string | null | undefined, size?: number) =>
        iconId ? `/api/icons/${iconId}/image?size=${size}` : null
    };

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: IconImageService, useValue: iconImageStub },
      ],
    });

    jasmine.clock().install();
    spyOn(window, 'requestIdleCallback').and.callFake(((callback: IdleRequestCallback) => {
      setTimeout(() => callback({} as IdleDeadline), 0);
      return 1;
    }) as typeof requestIdleCallback);

    service = TestBed.inject(IconPrefetchService);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  it('resolves the provider only when idle loading starts and limits concurrency', () => {
    let provided = false;
    service.prefetch(() => {
      provided = true;
      return [
        { iconId: 'a', size: 128 },
        { iconId: 'b', size: 128 },
        { iconId: 'c', size: 128 },
        { iconId: 'd', size: 128 },
      ];
    }, loader);

    expect(provided).toBeFalse();
    expect(loaded).toEqual([]);
    jasmine.clock().tick(1);

    expect(provided).toBeTrue();
    expect(loaded).toEqual([
      '/api/icons/a/image?size=128',
      '/api/icons/b/image?size=128',
      '/api/icons/c/image?size=128',
    ]);
  });

  it('continues with the next queued url when a load finishes', async () => {
    service.prefetch(() => [
      { iconId: 'a', size: 128 },
      { iconId: 'b', size: 128 },
      { iconId: 'c', size: 128 },
      { iconId: 'd', size: 128 },
    ], loader);
    jasmine.clock().tick(1);

    resolvers[0]();
    await Promise.resolve();
    await Promise.resolve();

    expect(loaded).toContain('/api/icons/d/image?size=128');
    expect(loaded.length).toBe(4);
  });

  it('requests each rendition only once across calls', () => {
    service.prefetch(() => [{ iconId: 'a', size: 128 }], loader);
    jasmine.clock().tick(1);
    service.prefetch(() => [{ iconId: 'a', size: 128 }, { iconId: 'a', size: 256 }], loader);
    jasmine.clock().tick(1);

    expect(loaded).toEqual(['/api/icons/a/image?size=128', '/api/icons/a/image?size=256']);
  });

  it('retries a not-ready provider (null) until it yields targets', () => {
    let ready = false;
    service.prefetch(() => ready ? [{ iconId: 'a', size: 256 }] : null, loader);

    jasmine.clock().tick(1);
    expect(loaded).toEqual([]);

    ready = true;
    jasmine.clock().tick(1500);
    expect(loaded).toEqual(['/api/icons/a/image?size=256']);
  });
});
