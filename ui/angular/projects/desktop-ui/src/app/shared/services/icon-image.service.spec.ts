import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService } from '../transport';
import { IconImageService } from './icon-image.service';

describe('IconImageService', () => {
  let service: IconImageService;

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconImageUrl']);
    apiSpy.getIconImageUrl.and.callFake((iconId: string, size?: number) =>
      `http://host/api/icons/${iconId}/image${size ? `?size=${size}` : ''}`);

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

  it('returns null for a missing icon id', () => {
    expect(service.getIconUrl(undefined)).toBeNull();
    expect(service.getIconUrl(null)).toBeNull();
  });
});
