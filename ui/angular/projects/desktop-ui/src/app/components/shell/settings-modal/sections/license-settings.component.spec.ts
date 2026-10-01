import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompanionLicenseStatus } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConnectAccountService } from '../../../../services/connect-account.service';
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
  let signedIn: WritableSignal<boolean>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getCompanionLicense', 'redeemCompanionPromoCode', 'onNotification']);
    api.getCompanionLicense.and.resolveTo(UNLICENSED);
    signedIn = signal(true);
    api.onNotification.and.returnValue(EMPTY);

    await TestBed.configureTestingModule({
      imports: [LicenseSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ConnectAccountService, useValue: { isSignedIn: signedIn } },
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

  function openDetails(fixture: ComponentFixture<LicenseSettingsComponent>): void {
    (find(fixture, 'license-details-toggle') as HTMLElement).click();
    fixture.detectChanges();
  }

  it('offers no way to issue or revoke a test license', async () => {
    const fixture = await create();

    expect(fixture.componentInstance.status()?.licensed).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('button').length).toBe(1);
    expect(find(fixture, 'license-redeem')?.querySelectorAll('button').length).toBe(1);
    expect(find(fixture, 'license-test-marker')).toBeNull();
  });

  it('marks a test license so it is never mistaken for a purchased one', async () => {
    api.getCompanionLicense.and.resolveTo(TEST_LICENSE);
    const fixture = await create();

    expect(find(fixture, 'license-test-marker')).not.toBeNull();
    openDetails(fixture);
    expect(fixture.nativeElement.textContent).toContain('abc123');
  });

  it('shows only the license status until the details are opened', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, accountSync: 'synced' });
    const fixture = await create();
    const toggle = find(fixture, 'license-details-toggle') as HTMLElement;

    const collapsed = { details: find(fixture, 'license-details'), expanded: toggle.getAttribute('aria-expanded') };
    openDetails(fixture);
    const text = fixture.nativeElement.textContent as string;
    openDetails(fixture);

    expect(collapsed).toEqual({ details: null, expanded: 'false' });
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(text).toContain('0190f3a2b4c64d8e9f0a1b2c3d4e5f60');
    expect(find(fixture, 'license-details')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('0190f3a2b4c64d8e9f0a1b2c3d4e5f60');
  });

  it('never shows the signing key', async () => {
    api.getCompanionLicense.and.resolveTo(PURCHASED);
    const fixture = await create();
    openDetails(fixture);

    expect(fixture.nativeElement.textContent).not.toContain('prod-2026');
  });

  it('offers no details toggle without a license', async () => {
    const fixture = await create();

    expect(find(fixture, 'license-details-toggle')).toBeNull();
  });

  it('says a license is saved to the Macro Deck account', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, accountSync: 'synced' });
    const fixture = await create();
    openDetails(fixture);

    expect(find(fixture, 'license-account-synced')).not.toBeNull();
    expect(find(fixture, 'license-account-signed-out')).toBeNull();
  });

  it('asks a signed-out owner to sign in to use the license elsewhere', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, accountSync: 'signedOut' });
    const fixture = await create();
    openDetails(fixture);

    expect(find(fixture, 'license-account-signed-out')).not.toBeNull();
    expect(find(fixture, 'license-account-synced')).toBeNull();
  });

  it('says nothing about the account when the host cannot tell', async () => {
    api.getCompanionLicense.and.resolveTo(PURCHASED);
    const fixture = await create();
    openDetails(fixture);

    expect(find(fixture, 'license-account-synced')).toBeNull();
    expect(find(fixture, 'license-account-signed-out')).toBeNull();
  });

  it('shows the purchase details of a purchased license', async () => {
    api.getCompanionLicense.and.resolveTo(PURCHASED);
    const fixture = await create();
    openDetails(fixture);

    expect(find(fixture, 'license-purchased-at')?.textContent?.trim()).toBeTruthy();
    expect(find(fixture, 'license-billing-id')?.textContent).toContain('GPA.3344-5566');
    expect(fixture.nativeElement.textContent).toContain('0190f3a2b4c64d8e9f0a1b2c3d4e5f60');
  });

  it('leaves out purchase details the license does not carry', async () => {
    api.getCompanionLicense.and.resolveTo({ ...PURCHASED, purchasedAt: null, billingId: null });
    const fixture = await create();
    openDetails(fixture);

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

  describe('promo codes', () => {
    it('offers the redeem button to a host without a license or with only a test license', async () => {
      const unlicensed = await create();
      api.getCompanionLicense.and.resolveTo(TEST_LICENSE);
      const test = await create();

      expect(find(unlicensed, 'license-redeem')).not.toBeNull();
      expect(find(test, 'license-redeem')).not.toBeNull();
    });

    it('disables the redeem button and says to sign in while signed out', async () => {
      signedIn.set(false);
      const fixture = await create();
      const button = find(fixture, 'license-redeem')!.querySelector('button')!;

      button.click();
      fixture.detectChanges();

      expect(button.disabled).toBeTrue();
      expect(fixture.nativeElement.querySelector('app-promo-code-modal')).toBeNull();
      expect(fixture.nativeElement.textContent).toContain(
        fixture.componentInstance['localization'].translateKey('macrodeck.app:Settings.License.Redeem.Result.SignedOut'),
      );
    });

    it('hides the redeem button while a real license is held', async () => {
      api.getCompanionLicense.and.resolveTo(PURCHASED);
      const fixture = await create();

      expect(find(fixture, 'license-redeem')).toBeNull();
    });

    it('opens the promo code dialog from the button and reloads the status when it closes', async () => {
      const fixture = await create();

      const heightBefore = fixture.nativeElement.offsetHeight;
      (find(fixture, 'license-redeem') as HTMLElement).querySelector('button')!.click();
      fixture.detectChanges();
      const opened = fixture.nativeElement.querySelector('app-promo-code-modal');
      const heightOpen = fixture.nativeElement.offsetHeight;
      api.getCompanionLicense.calls.reset();
      api.getCompanionLicense.and.resolveTo(PURCHASED);
      fixture.componentInstance.closeRedeem();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(opened).not.toBeNull();
      expect(heightOpen).toBe(heightBefore);
      expect(fixture.nativeElement.querySelector('app-promo-code-modal')).toBeNull();
      expect(api.getCompanionLicense).toHaveBeenCalledTimes(1);
      expect(fixture.componentInstance.status()).toEqual(PURCHASED);
    });

    it('labels a promo code license by its source', async () => {
      api.getCompanionLicense.and.resolveTo({ ...PURCHASED, source: 'promo-code' });
      const fixture = await create();

      expect(fixture.componentInstance.sourceLabel()).not.toBe('promo-code');
      expect(fixture.componentInstance.sourceLabel()).not.toBe('-');
    });
  });
});
