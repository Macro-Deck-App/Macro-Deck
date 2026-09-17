import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { ConnectAccountService } from '../../services/connect-account.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';
import { StoreViewSwitcherComponent } from './store-view-switcher.component';

describe('StoreViewSwitcherComponent', () => {
  const isSignedIn = signal(false);
  let router: jasmine.SpyObj<Router>;

  function labels(fixture: { nativeElement: HTMLElement }): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).map(button => button.textContent?.trim() ?? '');
  }

  async function create() {
    TestBed.configureTestingModule({
      imports: [StoreViewSwitcherComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: Router, useValue: router },
        { provide: ConnectAccountService, useValue: { isSignedIn } },
      ],
    });
    const fixture = TestBed.createComponent(StoreViewSwitcherComponent);
    fixture.componentRef.setInput('active', 'discover');
    await fixture.whenStable();
    return fixture;
  }

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);
    router.navigateByUrl.and.resolveTo(true);
  });

  it('offers Tests only to a signed-in account and opens its route', async () => {
    isSignedIn.set(false);
    const fixture = await create();
    const tests = TestBed.inject(LocalizationService).translateKey(AppStrings.Store.Tests.Title);
    expect(labels(fixture)).not.toContain(tests);

    isSignedIn.set(true);
    await fixture.whenStable();
    (Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('button')).find(button => button.textContent?.trim() === tests))!.click();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/store/tests');
  });
});
