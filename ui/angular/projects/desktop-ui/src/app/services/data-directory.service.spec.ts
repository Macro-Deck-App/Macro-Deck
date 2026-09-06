import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService, ToastService } from '@shared';
import { DataDirectoryService } from './data-directory.service';
import { EMPTY } from 'rxjs';

describe('DataDirectoryService', () => {
  let api: jasmine.SpyObj<ApiService>;
  let toastSpy: jasmine.Spy;
  let service: DataDirectoryService;

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getDataDirectory', 'openDataDirectory', 'onNotification']);
    api.getDataDirectory.and.resolveTo({ path: '/Users/x/MacroDeck', canOpen: true });
    api.openDataDirectory.and.resolveTo({ success: true, error: null });
    api.onNotification.and.returnValue(EMPTY);
    toastSpy = jasmine.createSpy('show');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: { show: toastSpy } },
      ],
    });
    service = TestBed.inject(DataDirectoryService);
  });

  it('reads the path and whether this connection may open it from the host', async () => {
    api.getDataDirectory.and.resolveTo({ path: '/Users/x/MacroDeck', canOpen: false });

    await service.refresh();

    expect(service.path()).toBe('/Users/x/MacroDeck');
    expect(service.canOpen()).toBeFalse();
    expect(service.available()).toBeTrue();
  });

  it('keeps the card quiet instead of throwing when the host cannot be reached', async () => {
    api.getDataDirectory.and.rejectWith(new Error('offline'));

    await expectAsync(service.refresh()).toBeResolved();

    expect(service.available()).toBeFalse();
    expect(service.path()).toBeNull();
    expect(toastSpy).not.toHaveBeenCalled();
  });

  it('surfaces the host\'s reason as an error toast when the folder could not be opened', async () => {
    api.openDataDirectory.and.resolveTo({ success: false, error: 'xdg-open is not installed.' });
    await service.refresh();

    await service.open();

    expect(toastSpy).toHaveBeenCalledWith('xdg-open is not installed.', { variant: 'error' });
    expect(service.opening()).toBeFalse();
  });

  it('tells the user the click did nothing when the host cannot be reached', async () => {
    api.openDataDirectory.and.rejectWith(new Error('offline'));
    await service.refresh();

    await expectAsync(service.open()).toBeResolved();

    expect(toastSpy).toHaveBeenCalledWith(jasmine.any(String), { variant: 'error' });
    expect(service.opening()).toBeFalse();
  });

  it('does not fire a second request while one is still in flight', async () => {
    let resolveOpen!: (value: { success: boolean; error: string | null }) => void;
    api.openDataDirectory.and.returnValue(
      new Promise((resolve) => {
        resolveOpen = resolve;
      })
    );
    await service.refresh();

    const first = service.open();
    const second = service.open();

    expect(service.opening()).toBeTrue();
    expect(api.openDataDirectory).toHaveBeenCalledTimes(1);

    resolveOpen({ success: true, error: null });
    await Promise.all([first, second]);

    expect(service.opening()).toBeFalse();
  });
});
