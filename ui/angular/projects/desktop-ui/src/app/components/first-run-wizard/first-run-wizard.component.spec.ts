import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@shared';
import { FirstRunWizardComponent } from './first-run-wizard.component';
import { MigrationOfferService } from '../../services/migration-offer.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('FirstRunWizardComponent', () => {
  let authStub: { setup: jasmine.Spy };

  function createFixture(): ComponentFixture<FirstRunWizardComponent> {
    TestBed.configureTestingModule({
      imports: [FirstRunWizardComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: AuthService, useValue: authStub },
      ],
    });
    return TestBed.createComponent(FirstRunWizardComponent);
  }

  function createComponent(): FirstRunWizardComponent {
    return createFixture().componentInstance;
  }

  beforeEach(() => {
    localStorage.clear();
    authStub = { setup: jasmine.createSpy('setup').and.resolveTo({ ok: true }) };
  });

  it('requires a username, a password of at least 8 characters and a matching confirmation', () => {
    const component = createComponent();

    component.username.set('admin');
    component.password.set('short');
    component.passwordConfirm.set('short');
    expect(component.passwordTooShort()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();

    component.password.set('password123');
    component.passwordConfirm.set('password124');
    expect(component.passwordsMismatch()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();

    component.passwordConfirm.set('password123');
    expect(component.canSubmit()).toBeTrue();
  });

  it('submits the trimmed username and password', async () => {
    const component = createComponent();
    component.username.set('  admin  ');
    component.password.set('password123');
    component.passwordConfirm.set('password123');

    await component.submit();

    expect(authStub.setup).toHaveBeenCalledWith('admin', 'password123');
    expect(component.error()).toBeNull();
  });

  it('marks the backdrop as a window drag region for the undecorated shell window', () => {
    const fixture = createFixture();
    fixture.detectChanges();

    const backdrop = fixture.nativeElement.querySelector('.wizard-backdrop') as HTMLElement;
    expect(backdrop.hasAttribute('data-tauri-drag-region')).toBeTrue();
  });

  it('arms the migration offer before awaiting setup, since a state flip can tear this down mid-await', async () => {
    let armedBeforeSetupResolved = false;
    authStub.setup.and.callFake(() => {
      armedBeforeSetupResolved = TestBed.inject(MigrationOfferService).pending();
      return Promise.resolve({ ok: true });
    });

    const component = createComponent();
    component.username.set('admin');
    component.password.set('password123');
    component.passwordConfirm.set('password123');

    await component.submit();

    expect(armedBeforeSetupResolved).toBeTrue();
    expect(TestBed.inject(MigrationOfferService).pending()).toBeTrue();
  });

  it('surfaces a setup failure as an error banner message', async () => {
    authStub.setup.and.resolveTo({ ok: false, message: 'A user already exists.' });
    const component = createComponent();
    component.username.set('admin');
    component.password.set('password123');
    component.passwordConfirm.set('password123');

    await component.submit();

    expect(component.error()).toBe('A user already exists.');
    expect(component.submitting()).toBeFalse();
  });
});
