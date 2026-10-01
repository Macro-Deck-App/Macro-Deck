import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RedeemCompanionPromoCodeResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { EMPTY } from 'rxjs';
import { ConnectAccountService } from '../../../../services/connect-account.service';
import { PromoCodeModalComponent } from './promo-code-modal.component';

describe('PromoCodeModalComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let signedIn: WritableSignal<boolean>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['redeemCompanionPromoCode', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    signedIn = signal(true);

    await TestBed.configureTestingModule({
      imports: [PromoCodeModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ConnectAccountService, useValue: { isSignedIn: signedIn } },
      ],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<PromoCodeModalComponent>> {
    const fixture = TestBed.createComponent(PromoCodeModalComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function find(fixture: ComponentFixture<PromoCodeModalComponent>, testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  async function redeem(fixture: ComponentFixture<PromoCodeModalComponent>, code: string): Promise<void> {
    fixture.componentInstance.code.set(code);
    await fixture.componentInstance.redeem();
    fixture.detectChanges();
  }

  const resultText = (fixture: ComponentFixture<PromoCodeModalComponent>) =>
    find(fixture, 'promo-code-result')?.textContent?.trim();

  it('asks a signed-out user to sign in and does not let the code be sent', async () => {
    signedIn.set(false);
    const fixture = await create();

    await redeem(fixture, 'ABCD');

    expect(find(fixture, 'promo-code-sign-in-hint')).not.toBeNull();
    expect(fixture.componentInstance.canRedeem()).toBeFalse();
    expect(api.redeemCompanionPromoCode).not.toHaveBeenCalled();
  });

  it('says that store codes are not redeemed here', async () => {
    const fixture = await create();
    const note = find(fixture, 'promo-code-store-note')?.textContent ?? '';

    expect(note).toContain('Google Play');
    expect(note).toContain('App Store');
  });

  it('shows no sign-in hint to a signed-in user', async () => {
    const fixture = await create();

    expect(find(fixture, 'promo-code-sign-in-hint')).toBeNull();
  });

  it('does not submit a blank code', async () => {
    const fixture = await create();

    await redeem(fixture, '   ');

    expect(api.redeemCompanionPromoCode).not.toHaveBeenCalled();
    expect(fixture.componentInstance.canRedeem()).toBeFalse();
  });

  it('sends the code as typed apart from surrounding spaces', async () => {
    api.redeemCompanionPromoCode.and.resolveTo({ status: 'redeemed', retryAfterSeconds: null });
    const fixture = await create();

    await redeem(fixture, ' abcd-efgh ');

    expect(api.redeemCompanionPromoCode).toHaveBeenCalledOnceWith('abcd-efgh');
  });

  for (const status of ['redeemed', 'accountLicenseExists'] as const) {
    it(`reports success and stops offering the field for the answer ${status}`, async () => {
      api.redeemCompanionPromoCode.and.resolveTo({ status, retryAfterSeconds: null });
      const fixture = await create();

      await redeem(fixture, 'ABCD');

      expect(fixture.componentInstance.outcome()?.success).toBeTrue();
      expect(find(fixture, 'promo-code-result')!.classList).not.toContain('promo-code__result--error');
      expect(resultText(fixture)).toBeTruthy();
      expect(fixture.nativeElement.querySelector('shared-input')).toBeNull();
      expect(fixture.componentInstance.canRedeem()).toBeFalse();
    });
  }

  for (const status of [
    'invalid',
    'expired',
    'alreadyRedeemed',
    'revoked',
    'accountSuspended',
    'unavailable',
    'signedOut',
    'alreadyLicensed',
  ] as const) {
    it(`shows its own message and keeps the code for the answer ${status}`, async () => {
      api.redeemCompanionPromoCode.and.resolveTo({ status, retryAfterSeconds: null });
      const fixture = await create();

      await redeem(fixture, 'ABCD');

      expect(resultText(fixture)).toBeTruthy();
      expect(find(fixture, 'promo-code-result')!.classList).toContain('promo-code__result--error');
      expect(resultText(fixture)).not.toContain('macrodeck.app:');
      expect(fixture.componentInstance.outcome()?.success).toBeFalse();
      expect(fixture.componentInstance.code()).toBe('ABCD');
    });
  }

  it('names the time to retry after a rate limit and still reads sensibly without one', async () => {
    api.redeemCompanionPromoCode.and.resolveTo({ status: 'rateLimited', retryAfterSeconds: 600 });
    const fixture = await create();
    await redeem(fixture, 'ABCD');
    const withTime = resultText(fixture);

    api.redeemCompanionPromoCode.and.resolveTo({ status: 'rateLimited', retryAfterSeconds: null });
    await redeem(fixture, 'ABCD');
    const withoutTime = resultText(fixture);

    expect(withTime).not.toContain('{time}');
    expect(withoutTime).not.toContain('{time}');
    expect(withTime).not.toBe(withoutTime);
  });

  it('shows a generic failure when the host cannot be reached and lets the user try again', async () => {
    api.redeemCompanionPromoCode.and.rejectWith(new Error('offline'));
    const fixture = await create();

    await redeem(fixture, 'ABCD');

    expect(resultText(fixture)).toBeTruthy();
    expect(fixture.componentInstance.redeeming()).toBeFalse();
    expect(fixture.componentInstance.canRedeem()).toBeTrue();
  });

  it('ignores a second submit while the first is still running', async () => {
    let answer!: (value: RedeemCompanionPromoCodeResponse) => void;
    api.redeemCompanionPromoCode.and.returnValue(new Promise(resolve => (answer = resolve)));
    const fixture = await create();
    fixture.componentInstance.code.set('ABCD');

    const first = fixture.componentInstance.redeem();
    const second = fixture.componentInstance.redeem();
    answer({ status: 'redeemed', retryAfterSeconds: null });
    await Promise.all([first, second]);

    expect(api.redeemCompanionPromoCode).toHaveBeenCalledTimes(1);
  });
});
