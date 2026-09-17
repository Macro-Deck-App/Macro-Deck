import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { AppStrings, GetStoreTestsResponse, StoreTestBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { SettingsModalService } from '../../../services/settings-modal.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { StoreTestsTabComponent } from './store-tests-tab.component';

function test(overrides: Partial<StoreTestBody> = {}): StoreTestBody {
  return {
    packageId: 'com.acme.deck-tools',
    displayName: 'Deck Tools',
    joinedAt: '2026-09-01T10:00:00Z',
    hasIcon: false,
    iconSha256: null,
    installedVersion: null,
    installedTestBuildId: null,
    activeOperationId: null,
    builds: [
      {
        id: 'build-2', version: '1.2.0', build: '42', changelog: null, sizeInBytes: 2048,
        uploadedAt: '2026-09-10T10:00:00Z', availableAt: '2026-09-10T10:00:00Z',
      },
      {
        id: 'build-1', version: '1.1.0', build: '41', changelog: null, sizeInBytes: 1024,
        uploadedAt: '2026-09-05T10:00:00Z', availableAt: '2026-09-05T10:00:00Z',
      },
    ],
    ...overrides,
  };
}

describe('StoreTestsTabComponent', () => {
  let fixture: ComponentFixture<StoreTestsTabComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let settingsModal: jasmine.SpyObj<SettingsModalService>;

  function translate(key: string, params?: Record<string, unknown>): string {
    return TestBed.inject(LocalizationService).translateKey(key, params);
  }

  function buttons(label: string): HTMLButtonElement[] {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .filter(button => button.textContent?.trim() === label);
  }

  function rows(): HTMLElement[] {
    return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.test-build'));
  }

  async function settle(): Promise<void> {
    for (let attempt = 0; attempt < 10; attempt++) {
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  // ModalComponent plays a 150ms close animation before its confirm or cancel output fires.
  async function clickInModal(label: string): Promise<void> {
    jasmine.clock().install();
    try {
      const matches = buttons(label);
      matches[matches.length - 1].click();
      jasmine.clock().tick(150);
    } finally {
      jasmine.clock().uninstall();
    }
    await settle();
  }

  async function createFixture(response: GetStoreTestsResponse): Promise<void> {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getStoreOperations', 'getStoreTests', 'installStoreTestBuild', 'getStoreExtensionIconUrl',
    ]);
    api.onNotification.and.callFake(() => new Subject<never>().asObservable() as Observable<never>);
    api.getStoreOperations.and.resolveTo({ operations: [] });
    api.getStoreTests.and.resolveTo(response);
    api.getStoreExtensionIconUrl.and.returnValue('');
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('connected') });
    settingsModal = jasmine.createSpyObj<SettingsModalService>('SettingsModalService', ['open']);

    TestBed.configureTestingModule({
      imports: [StoreTestsTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api },
        { provide: SettingsModalService, useValue: settingsModal },
      ],
    });

    fixture = TestBed.createComponent(StoreTestsTabComponent);
    fixture.detectChanges();
    await settle();
  }

  it('installs a test build only after the unreviewed-build warning is confirmed', async () => {
    await createFixture({ success: true, tests: [test()] });
    api.installStoreTestBuild.and.resolveTo({ success: true });

    buttons(translate(AppStrings.Store.Install))[1].click();
    await settle();

    const modal = fixture.nativeElement.querySelector('shared-confirmation-modal') as HTMLElement | null;
    expect(modal).not.toBeNull();
    expect(modal!.textContent).toContain(translate(AppStrings.Store.Tests.ConfirmMessage,
      { name: 'Deck Tools', version: '1.1.0', build: '41' }));
    expect(api.installStoreTestBuild).not.toHaveBeenCalled();

    await clickInModal(translate(AppStrings.Store.Tests.InstallTestBuild));

    expect(api.installStoreTestBuild).toHaveBeenCalledOnceWith('com.acme.deck-tools', 'build-1');
  });

  it('installs nothing when the warning is cancelled', async () => {
    await createFixture({ success: true, tests: [test()] });

    buttons(translate(AppStrings.Store.Install))[0].click();
    await settle();
    await clickInModal(translate('macrodeck:Common.Cancel'));

    expect(api.installStoreTestBuild).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
  });

  it('shows the active test build as installed and offers the other build as a replacement', async () => {
    await createFixture({
      success: true,
      tests: [test({ installedVersion: '1.2.0', installedTestBuildId: 'build-2' })],
    });

    const [installedRow, otherRow] = rows();
    const installed = Array.from(installedRow.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === translate(AppStrings.Store.Installed));
    expect(installed).toBeDefined();
    expect(installed!.disabled).toBeTrue();
    expect(otherRow.textContent).toContain(translate(AppStrings.Store.Tests.InstallTestBuild));
    expect(fixture.nativeElement.textContent)
      .toContain(translate(AppStrings.Store.Tests.InstalledVersion, { version: '1.2.0' }));
  });

  it('asks the user to sign in when the host reports the session is gone', async () => {
    await createFixture({ success: false, error: { code: 'sign_in_required', message: 'raw' }, tests: [] });

    expect(fixture.nativeElement.textContent).toContain(translate(AppStrings.Store.Tests.SignInHeading));
    expect(rows().length).toBe(0);

    buttons(translate(AppStrings.Settings.Account.SignIn))[0].click();

    expect(settingsModal.open).toHaveBeenCalledWith('account');
  });
});
