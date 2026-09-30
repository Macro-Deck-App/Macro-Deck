import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '@shared';
import { provideLocalizationTesting } from '../../testing/localization-test-support';
import { StoreShareService } from './store-share.service';
import { TextClipboardService } from './text-clipboard.service';

describe('StoreShareService', () => {
  let clipboard: jasmine.SpyObj<TextClipboardService>;
  let toasts: jasmine.SpyObj<ToastService>;
  let service: StoreShareService;

  beforeEach(() => {
    clipboard = jasmine.createSpyObj<TextClipboardService>('TextClipboardService', ['copyText']);
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: TextClipboardService, useValue: clipboard },
        { provide: ToastService, useValue: toasts },
      ],
    });
    service = TestBed.inject(StoreShareService);
  });

  it('copies the public https address of the entry, never an internal registry address', async () => {
    clipboard.copyText.and.resolveTo({ status: 'copied', via: 'clipboard-api' });

    await service.copyLink('com.acme.hue');

    expect(clipboard.copyText).toHaveBeenCalledOnceWith('https://store.macro-deck.app/com.acme.hue');
    expect(toasts.show).toHaveBeenCalledWith('Link copied.', { variant: 'success' });
  });

  it('says why the link could not be copied', async () => {
    clipboard.copyText.and.resolveTo({ status: 'failed', reason: 'denied' });

    await service.copyLink('com.acme.hue');

    expect(toasts.show).toHaveBeenCalledWith(jasmine.any(String), { variant: 'error' });
  });
});
