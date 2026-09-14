import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompanionLicenseStatus } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { EMPTY } from 'rxjs';
import { DeveloperModeService } from '../../../../services/developer-mode.service';
import { LicenseSettingsComponent } from './license-settings.component';

const UNLICENSED: CompanionLicenseStatus = {
  licensed: false,
  licenseId: null,
  source: null,
  keyId: null,
  issuedAt: null,
  isTest: false,
  testLicenseStored: false,
};

const TEST_LICENSE: CompanionLicenseStatus = {
  licensed: true,
  licenseId: 'abc123',
  source: 'test',
  keyId: 'test-2026',
  issuedAt: Date.UTC(2026, 0, 1),
  isTest: true,
  testLicenseStored: true,
};

describe('LicenseSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  const developerMode = signal(false);

  beforeEach(async () => {
    developerMode.set(false);
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getCompanionLicense',
      'issueTestCompanionLicense',
      'revokeTestCompanionLicense',
      'onNotification',
    ]);
    api.getCompanionLicense.and.resolveTo(UNLICENSED);
    api.issueTestCompanionLicense.and.resolveTo(TEST_LICENSE);
    api.revokeTestCompanionLicense.and.resolveTo(UNLICENSED);
    api.onNotification.and.returnValue(EMPTY);

    await TestBed.configureTestingModule({
      imports: [LicenseSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: DeveloperModeService, useValue: { enabled: developerMode } },
      ],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<LicenseSettingsComponent>> {
    const fixture = TestBed.createComponent(LicenseSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function find(fixture: ComponentFixture<LicenseSettingsComponent>, testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  it('shows an unlicensed host without the test issuer outside developer mode', async () => {
    const fixture = await create();

    expect(fixture.componentInstance.status()?.licensed).toBeFalse();
    expect(find(fixture, 'issue-test-license')).toBeNull();
    expect(find(fixture, 'license-test-marker')).toBeNull();
    expect(find(fixture, 'revoke-test-license')).toBeNull();
  });

  it('revokes a test license outside developer mode and shows the status the host returns', async () => {
    api.getCompanionLicense.and.resolveTo({ ...UNLICENSED, testLicenseStored: true });
    const fixture = await create();

    find(fixture, 'revoke-test-license')?.querySelector('button')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.revokeTestCompanionLicense).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.status()).toEqual(UNLICENSED);
    expect(find(fixture, 'revoke-test-license')).toBeNull();
    expect(find(fixture, 'license-test-marker')).toBeNull();
  });

  it('offers no revoke for a purchased license', async () => {
    api.getCompanionLicense.and.resolveTo({
      ...TEST_LICENSE,
      source: 'google-play',
      keyId: 'prod-2026',
      isTest: false,
      testLicenseStored: false,
    });
    const fixture = await create();

    expect(find(fixture, 'revoke-test-license')).toBeNull();
  });

  it('marks a test license so it is never mistaken for a purchased one', async () => {
    api.getCompanionLicense.and.resolveTo(TEST_LICENSE);
    const fixture = await create();

    expect(find(fixture, 'license-test-marker')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('abc123');
    expect(fixture.nativeElement.textContent).toContain('test-2026');
  });

  it('offers the test issuer in developer mode and shows the license it returns', async () => {
    developerMode.set(true);
    const fixture = await create();

    expect(find(fixture, 'issue-test-license')).not.toBeNull();
    await fixture.componentInstance.issueTestLicense();
    fixture.detectChanges();

    expect(api.issueTestCompanionLicense).toHaveBeenCalledTimes(1);
    expect(find(fixture, 'license-test-marker')).not.toBeNull();
  });

  it('reports a failed issue without losing the current status', async () => {
    developerMode.set(true);
    api.issueTestCompanionLicense.and.rejectWith(new Error('not found'));
    const fixture = await create();

    await fixture.componentInstance.issueTestLicense();

    expect(fixture.componentInstance.issueFailed()).toBeTrue();
    expect(fixture.componentInstance.status()).toEqual(UNLICENSED);
  });
});
