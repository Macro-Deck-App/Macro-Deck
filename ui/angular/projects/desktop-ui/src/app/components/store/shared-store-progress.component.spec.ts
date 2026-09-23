import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppStrings, StoreOperationBody } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { StoreOperationService } from '../../services/store-operation.service';
import { UpdateModalService } from '../../services/update-modal.service';
import { UpdateService } from '../../services/update.service';
import { SharedStoreProgressComponent } from './shared-store-progress.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('SharedStoreProgressComponent', () => {
  let updates: { hasBridge: boolean; check: jasmine.Spy };
  let updateModal: jasmine.SpyObj<UpdateModalService>;
  let operations: { all: ReturnType<typeof signal<StoreOperationBody[]>>; retry: jasmine.Spy; dismiss: jasmine.Spy };

  function failed(error: string, canRetry: boolean): StoreOperationBody {
    return {
      id: 'op-1', kind: 'Install', extensionKind: 'Plugin', packageId: 'app.soundbox', version: '1.0.0',
      displayName: 'SoundBox', state: 'Failed', bytesDownloaded: 0, startedAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:01Z', error, canRetry,
      errorMessage: "The plugin needs Macro Deck '>=3.0.0-beta.12'; this host is 3.0.0-beta.11.",
    };
  }

  function render(operation: StoreOperationBody, hasBridge = true): HTMLElement {
    updates = { hasBridge, check: jasmine.createSpy('check').and.resolveTo() };
    updateModal = jasmine.createSpyObj<UpdateModalService>('UpdateModalService', ['open']);
    operations = { all: signal([operation]), retry: jasmine.createSpy('retry'), dismiss: jasmine.createSpy('dismiss') };
    TestBed.configureTestingModule({
      imports: [SharedStoreProgressComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: StoreOperationService, useValue: operations },
        { provide: UpdateService, useValue: updates },
        { provide: UpdateModalService, useValue: updateModal },
      ],
    });
    const fixture = TestBed.createComponent(SharedStoreProgressComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function translate(key: string): string {
    return TestBed.inject(LocalizationService).translateKey(key);
  }

  function labels(host: HTMLElement): string[] {
    return Array.from(host.querySelectorAll('button.retry')).map(button => button.textContent?.trim() ?? '');
  }

  it('offers a Macro Deck update check instead of Retry when the plugin needs a newer Macro Deck', () => {
    const host = render(failed('RequiresNewerMacroDeck', false));

    expect(labels(host)).toEqual([translate('macrodeck.app:Settings.Update.CheckForUpdatesAction')]);
    (host.querySelector('button.retry') as HTMLElement).click();
    expect(updates.check).toHaveBeenCalled();
    expect(updateModal.open).toHaveBeenCalled();
  });

  it('leads with the translated reason and keeps what the host reported as detail', () => {
    const host = render(failed('RequiresNewerMacroDeck', false));

    expect(host.querySelector('.failed-hint')?.textContent).toContain(translate(AppStrings.Store.Error.RequiresNewerMacroDeck));
    expect(host.querySelector('.failed-detail')?.textContent).toContain('3.0.0-beta.12');
  });

  it('offers no action for a failure that neither retrying nor updating can fix', () => {
    expect(labels(render(failed('Unsupported', false)))).toEqual([]);
  });

  it('offers no update check where Macro Deck cannot update itself from here', () => {
    expect(labels(render(failed('RequiresNewerMacroDeck', false), false))).toEqual([]);
  });

  it('still offers Retry when trying again can help', () => {
    expect(labels(render(failed('DownloadFailed', true)))).toEqual([translate('macrodeck:Common.Retry')]);
  });
});
