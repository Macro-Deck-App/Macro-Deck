import { computed, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService } from '../transport';
import { IconImageService } from './icon-image.service';

describe('IconImageService', () => {
  let service: IconImageService;

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconImageUrl']);
    apiSpy.getIconImageUrl.and.callFake((iconId: string, size?: number, version?: string | null) =>
      `http://host/api/icons/${iconId}/image${size ? `?size=${size}` : ''}${version ? `#${version}` : ''}`);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(IconImageService);
  });

  it('builds the image URL with a size', () => {
    expect(service.getIconUrl('abc', 128)).toBe('http://host/api/icons/abc/image?size=128');
  });

  it('builds the image URL without a size', () => {
    expect(service.getIconUrl('abc')).toBe('http://host/api/icons/abc/image');
  });

  it('versions a bare icon id once its content hash is known, so a replaced icon gets a new URL', () => {
    service.rememberVersions([{ id: 'abc', contentHash: 'h1' }]);
    const before = service.getIconUrl('abc', 128);

    service.rememberVersions([{ id: 'abc', contentHash: 'h2' }]);

    expect(before).toBe('http://host/api/icons/abc/image?size=128#h1');
    expect(service.getIconUrl('abc', 128)).toBe('http://host/api/icons/abc/image?size=128#h2');
  });

  it('prefers the version the caller holds over a remembered one', () => {
    service.rememberVersions([{ id: 'abc', contentHash: 'old' }]);

    expect(service.getIconUrl('abc', 128, 'new')).toBe('http://host/api/icons/abc/image?size=128#new');
  });

  it('recomputes a derived URL when the version changes', () => {
    const url = computed(() => service.getIconUrl('abc', 128));
    expect(url()).toBe('http://host/api/icons/abc/image?size=128');

    service.rememberVersions([{ id: 'abc', contentHash: 'h1' }]);

    expect(url()).toBe('http://host/api/icons/abc/image?size=128#h1');
  });

  it('returns null for a missing icon id', () => {
    expect(service.getIconUrl(undefined)).toBeNull();
    expect(service.getIconUrl(null)).toBeNull();
  });
});
