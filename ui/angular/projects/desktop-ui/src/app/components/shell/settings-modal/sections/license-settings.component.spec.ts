import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompanionLicenseStatus } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { EMPTY, Subject } from 'rxjs';
import { LicenseSettingsComponent } from './license-settings.component';

const UNLICENSED: CompanionLicenseStatus = {
  licensed: false,
  licenseId: null,
  source: null,
  keyId: null,
  issuedAt: null,
  purchasedAt: null,
  billingId: null,
  isTest: false,
  accountSync: 'unknown',
  issuePending: false,
  nextIssueAttemptAt: null,
};

const TEST_LICENSE: CompanionLicenseStatus = {
  licensed: true,
  licenseId: 'abc123',
  source: 'test',
  keyId: 'test-2026',
  issuedAt: Date.UTC(2026, 0, 1),
  purchasedAt: null,
  billingId: null,
  isTest: true,
  accountSync: 'unknown',
  issuePending: false,
  nextIssueAttemptAt: null,
};

const PURCHASED: CompanionLicenseStatus = {
  ...TEST_LICENSE,
  licenseId: '0190f3a2b4c64d8e9f0a1b2c3d4e5f60',
  source: 'google-play',
  keyId: 'prod-2026',
  purchasedAt: Date.UTC(2026, 0, 1),
  billingId: 'GPA.3344-5566',
  isTest: false,
};

describe('LicenseSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getCompanionLicense', 'onNotification']);
    api.getCompanionLicense.and.resolveTo(UNLICENSED);
    api.onNotification.and.returnValue(EMPTY);

    await TestBed.configureTestingModule({
      imports: [LicenseSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
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

  it('offers no way to issue or revoke a test license', async () => {
    const fixture = await create();

    expect(fixture.componentInstance.status()?.licensed).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('button').length).toBe(0);
    expect(find(fixture, 'license-test-marker')).toBeNull();
  });

  it('marks a test license so it is never mistaken for a purchased one', async () => {
    api.getCompanionLicense.and.resolveTo(TEST_LICENSE);
    const fixture = await create();

    expect(find(fixture, 'license-test-marker')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('abc123');
    expect(fixture.nativeElement.textContent).toContain('test-2026');
  });

  it('says a license is saved to the Macro Deck account', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, accountSync: 'synced' });
    const fixture = await create();

    expect(find(fixture, 'license-account-synced')).not.toBeNull();
    expect(find(fixture, 'license-account-signed-out')).toBeNull();
  });

  it('asks a signed-out owner to sign in to use the license elsewhere', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, accountSync: 'signedOut' });
    const fixture = await create();

    expect(find(fixture, 'license-account-signed-out')).not.toBeNull();
    expect(find(fixture, 'license-account-synced')).toBeNull();
  });

  it('says nothing about the account when the host cannot tell', async () => {
    api.getCompanionLicense.and.resolveTo(PURCHASED);
    const fixture = await create();

    expect(find(fixture, 'license-account-synced')).toBeNull();
    expect(find(fixture, 'license-account-signed-out')).toBeNull();
  });

  it('shows the purchase details of a purchased license', async () => {
    api.getCompanionLicense.and.resolveTo(PURCHASED);
    const fixture = await create();

    expect(find(fixture, 'license-purchased-at')?.textContent?.trim()).toBeTruthy();
    expect(find(fixture, 'license-billing-id')?.textContent).toContain('GPA.3344-5566');
    expect(fixture.nativeElement.textContent).toContain('0190f3a2b4c64d8e9f0a1b2c3d4e5f60');
  });

  it('leaves out purchase details the license does not carry', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, purchasedAt: null, billingId: null });
    const fixture = await create();

    expect(find(fixture, 'license-purchased-at')).toBeNull();
    expect(find(fixture, 'license-billing-id')).toBeNull();
  });

  it('says a license is being issued and when the next attempt runs', async () => {
    const nextAttempt = Date.now() + 60_000;
    api.getCompanionLicense.and.resolveTo({ ...UNLICENSED, issuePending: true, nextIssueAttemptAt: nextAttempt });
    const fixture = await create();

    expect(find(fixture, 'license-issue-pending')).not.toBeNull();
    expect(fixture.componentInstance.nextAttempt()).not.toBeNull();
    expect(find(fixture, 'license-next-attempt')?.textContent).toContain(fixture.componentInstance.nextAttempt()!);
  });

  it('never shows a next attempt time that has already passed', async () => {
    jasmine.clock().install();
    try {
      jasmine.clock().mockDate(new Date(Date.UTC(2026, 0, 1, 12)));
      api.getCompanionLicense.and.resolveTo({
        ...UNLICENSED,
        issuePending: true,
        nextIssueAttemptAt: Date.UTC(2026, 0, 1, 12, 0, 30),
      });
      const fixture = await create();
      const before = fixture.componentInstance.nextAttempt();

      jasmine.clock().tick(31_000);

      expect(before).not.toBeNull();
      expect(fixture.componentInstance.nextAttempt()).toBeNull();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('reloads when the host reports a license change', async () => {
    const changes = new Subject<unknown>();
    api.onNotification.and.returnValue(changes.asObservable());
    const fixture = await create();
    api.getCompanionLicense.and.resolveTo(PURCHASED);

    changes.next({});
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.onNotification).toHaveBeenCalledWith('CompanionLicenseChangedEvent');
    expect(fixture.componentInstance.status()).toEqual(PURCHASED);
    expect(find(fixture, 'license-issue-pending')).toBeNull();
  });

  it('keeps the newest status when two reloads answer out of order', async () => {
    const changes = new Subject<unknown>();
    api.onNotification.and.returnValue(changes.asObservable());
    const fixture = await create();
    let answerFirst!: (status: CompanionLicenseStatus) => void;
    api.getCompanionLicense.and.returnValues(
      new Promise<CompanionLicenseStatus>(resolve => (answerFirst = resolve)),
      Promise.resolve(PURCHASED),
    );

    changes.next({});
    changes.next({});
    await fixture.whenStable();
    answerFirst({ ...UNLICENSED, issuePending: true, nextIssueAttemptAt: Date.now() + 60_000 });
    await fixture.whenStable();

    expect(fixture.componentInstance.status()).toEqual(PURCHASED);
  });
});
