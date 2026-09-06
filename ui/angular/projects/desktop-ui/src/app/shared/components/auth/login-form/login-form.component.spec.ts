import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LoginFormComponent } from './login-form.component';
import { AuthService } from '../../../services/auth.service';
import { provideLocalizationTesting } from '../../../localization/localization-test-support';

describe('LoginFormComponent', () => {
  let authStub: { login: jasmine.Spy };

  function createComponent(): LoginFormComponent {
    TestBed.configureTestingModule({
      imports: [LoginFormComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: AuthService, useValue: authStub },
      ],
    });
    const fixture = TestBed.createComponent(LoginFormComponent);
    fixture.componentRef.setInput('scope', 'client');
    return fixture.componentInstance;
  }

  beforeEach(() => {
    authStub = { login: jasmine.createSpy('login').and.resolveTo({ ok: true }) };
  });

  it('cannot submit while username or password is empty', () => {
    const component = createComponent();

    expect(component.canSubmit()).toBeFalse();
    component.username.set('admin');
    expect(component.canSubmit()).toBeFalse();
    component.password.set('secret');
    expect(component.canSubmit()).toBeTrue();
  });

  it('passes trimmed username and scope to the auth service', async () => {
    const component = createComponent();
    component.username.set('  admin  ');
    component.password.set('password123');

    await component.submit();

    expect(authStub.login).toHaveBeenCalledWith('admin', 'password123', 'client');
    expect(component.error()).toBeNull();
  });

  // Issue #839: sessions always persist, so the form must not offer a choice about it.
  it('offers no stay-signed-in control', () => {
    TestBed.configureTestingModule({
      imports: [LoginFormComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: AuthService, useValue: authStub },
      ],
    });
    const fixture = TestBed.createComponent(LoginFormComponent);
    fixture.componentRef.setInput('scope', 'client');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('input[type="checkbox"]').length).toBe(0);
    expect(element.querySelectorAll('shared-checkbox').length).toBe(0);
  });

  it('shows the auth error message on a failed login', async () => {
    authStub.login.and.resolveTo({ ok: false, message: 'Invalid username or password.' });
    const component = createComponent();
    component.username.set('admin');
    component.password.set('wrong');

    await component.submit();

    expect(component.error()).toBe('Invalid username or password.');
    expect(component.submitting()).toBeFalse();
  });

  it('ignores submit while a login is already in flight', async () => {
    const component = createComponent();
    component.username.set('admin');
    component.password.set('password123');
    component.submitting.set(true);

    await component.submit();

    expect(authStub.login).not.toHaveBeenCalled();
  });
});
