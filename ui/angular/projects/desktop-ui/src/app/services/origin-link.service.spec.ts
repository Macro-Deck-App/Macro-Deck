import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService } from '@shared';
import { Subject } from 'rxjs';
import type { UiOpenUrlEvent } from '@macro-deck/runtime';
import { ExternalLinkService } from './external-link.service';
import { OriginLinkService } from './origin-link.service';

describe('OriginLinkService', () => {
  let urls: Subject<UiOpenUrlEvent>;
  let links: jasmine.SpyObj<ExternalLinkService>;

  beforeEach(() => {
    urls = new Subject<UiOpenUrlEvent>();
    const api = jasmine.createSpyObj<ApiService>('ApiService', ['onUiOpenUrl']);
    api.onUiOpenUrl.and.returnValue(urls.asObservable());
    links = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ExternalLinkService, useValue: links },
      ],
    });
    TestBed.inject(OriginLinkService);
  });

  it('opens a web link the host sends in the browser', () => {
    urls.next({ url: 'https://example.com/article' });

    expect(links.open).toHaveBeenCalledOnceWith('https://example.com/article');
  });

  it('ignores a link that is not a web address', () => {
    urls.next({ url: 'javascript:alert(1)' });
    urls.next({ url: 'file:///etc/passwd' });

    expect(links.open).not.toHaveBeenCalled();
  });
});
