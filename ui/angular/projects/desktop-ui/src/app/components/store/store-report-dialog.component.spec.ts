import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { AppStrings, GetConnectSessionResponse, StoreReportResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ToastService } from '@shared';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';
import { ConnectAccountService } from '../../services/connect-account.service';
import { StoreReportDialogComponent } from './store-report-dialog.component';

describe('StoreReportDialogComponent', () => {
  let fixture: ComponentFixture<StoreReportDialogComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let reported: number;

  beforeEach(() => {
    for (const key of Object.keys(localStorage).filter(key => key.startsWith('md.localization.'))) {
      localStorage.removeItem(key);
    }
  });

  async function setup(
    status: GetConnectSessionResponse['status'] = 'signedIn',
    response?: StoreReportResponse,
  ): Promise<void> {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['reportStoreEntry', 'reportStoreReview', 'onNotification']);
    api.onNotification.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    api.reportStoreEntry.and.resolveTo(response ?? { success: true });
    TestBed.configureTestingModule({
      imports: [StoreReportDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api },
        { provide: ConnectAccountService, useValue: { session: signal({ status } as GetConnectSessionResponse) } },
      ],
    });

    fixture = TestBed.createComponent(StoreReportDialogComponent);
    fixture.componentRef.setInput('extensionKind', 'Plugin');
    fixture.componentRef.setInput('extensionId', 'com.acme.deck-tools');
    fixture.componentRef.setInput('target', { type: 'entry', name: 'Deck Tools' });
    reported = 0;
    fixture.componentInstance.reported.subscribe(() => reported++);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let pass = 0; pass < 3; pass++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(key: string, args?: Record<string, unknown>): string {
    return TestBed.inject(LocalizationService).translateKey(key, args);
  }

  async function reportAs(label: string): Promise<void> {
    const option = Array.from(host().querySelectorAll<HTMLLabelElement>('.report-reason'))
      .find(item => item.textContent!.trim() === label)!;
    option.querySelector<HTMLInputElement>('input')!.click();
    await settle();
    host().querySelector<HTMLButtonElement>('.report-submit button')!.click();
    await settle();
  }

  it('names the item and offers the Store entry reasons', async () => {
    await setup();

    expect(host().textContent).toContain(text(AppStrings.Store.Report.EntryHeading, { name: 'Deck Tools' }));
    expect(Array.from(host().querySelectorAll('.report-reason')).map(label => label.textContent!.trim())).toEqual([
      text(AppStrings.Store.Report.Reason.InappropriateContent),
      text(AppStrings.Store.Report.Reason.Misleading),
      text(AppStrings.Store.Report.Reason.Impersonation),
      text(AppStrings.Store.Report.Reason.Malicious),
      text(AppStrings.Store.Report.Reason.Spam),
      text(AppStrings.Store.Report.Reason.Other),
    ]);
  });

  it('asks for a reason before sending anything', async () => {
    await setup();

    host().querySelector<HTMLButtonElement>('.report-submit button')!.click();
    await settle();

    expect(host().textContent).toContain(text(AppStrings.Store.Report.Validation.ReasonRequired));
    expect(api.reportStoreEntry).not.toHaveBeenCalled();
  });

  it('sends the chosen reason without a description when none was given', async () => {
    await setup();

    await reportAs(text(AppStrings.Store.Report.Reason.Malicious));

    expect(api.reportStoreEntry).toHaveBeenCalledOnceWith('Plugin', 'com.acme.deck-tools', {
      category: 'Malicious',
      detail: null,
    });
    expect(reported).toBe(1);
  });

  it('treats a second report of the same item as done and says it was already reported', async () => {
    await setup('signedIn', { success: false, error: { code: 'already_reported' } });

    await reportAs(text(AppStrings.Store.Report.Reason.Spam));

    expect(reported).toBe(1);
    expect(TestBed.inject(ToastService).toasts().map(toast => toast.message))
      .toContain(text(AppStrings.Store.Report.Error.AlreadyReported));
  });

  it('explains when the Store cannot take reports for items yet', async () => {
    await setup('signedIn', { success: false, error: { code: 'report_unavailable' } });

    await reportAs(text(AppStrings.Store.Report.Reason.Spam));

    expect(host().querySelector('.report-error')?.textContent).toContain(text(AppStrings.Store.Report.Error.Unavailable));
    expect(reported).toBe(0);
  });

  it('shows a suspended account the reason it cannot report instead of the form', async () => {
    await setup('suspended');

    expect(host().textContent).toContain(text(AppStrings.Store.Reviews.Error.AccountSuspended));
    expect(host().querySelector('form')).toBeNull();
    expect(host().querySelector('.report-submit')).toBeNull();
  });
});
