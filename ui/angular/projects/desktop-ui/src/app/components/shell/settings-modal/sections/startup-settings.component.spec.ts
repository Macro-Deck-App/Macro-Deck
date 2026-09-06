import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GetAutostartSettingsResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { StartupSettingsComponent } from './startup-settings.component';
import { EMPTY } from 'rxjs';

describe('StartupSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  function installApi(state: GetAutostartSettingsResponse): void {
    api.getAutostartSettings.and.resolveTo(state);
    api.updateAutostartSettings.and.callFake(request =>
      Promise.resolve({ ...state, ...request, success: true, error: null }));
  }

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getAutostartSettings',
      'updateAutostartSettings',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    installApi({ supported: false, enabled: false, openMinimized: false });

    await TestBed.configureTestingModule({
      imports: [StartupSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function create(): Promise<ComponentFixture<StartupSettingsComponent>> {
    const fixture = TestBed.createComponent(StartupSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function toggles(f: ComponentFixture<StartupSettingsComponent>): HTMLInputElement[] {
    return Array.from(f.nativeElement.querySelectorAll('.ts-input'));
  }

  it('reflects the enabled autostart state from the host', async () => {
    installApi({ supported: true, enabled: true, openMinimized: false });
    const fixture = await create();

    const inputs = toggles(fixture);
    expect(inputs.length).toBe(2);
    expect(inputs[0].checked).toBeTrue();
    expect(inputs[0].disabled).toBeFalse();
    expect(inputs[1].disabled).toBeFalse();
    expect(fixture.nativeElement.querySelector('.startup__note')).toBeNull();
  });

  it('disables the "start minimized" toggle while autostart is off', async () => {
    installApi({ supported: true, enabled: false, openMinimized: false });
    const fixture = await create();

    const inputs = toggles(fixture);
    expect(inputs[0].disabled).toBeFalse();
    expect(inputs[1].disabled).toBeTrue();
  });

  it('enables autostart through the host API and reflects the applied state', async () => {
    installApi({ supported: true, enabled: false, openMinimized: false });
    const fixture = await create();

    toggles(fixture)[0].click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.updateAutostartSettings).toHaveBeenCalledWith({ enabled: true, openMinimized: false });
    expect(fixture.componentInstance.enabled()).toBeTrue();
  });

  it('does not toggle "start minimized" while autostart is off', async () => {
    installApi({ supported: true, enabled: false, openMinimized: false });
    const fixture = await create();

    await fixture.componentInstance.setOpenMinimized(true);
    await fixture.whenStable();

    expect(api.updateAutostartSettings).not.toHaveBeenCalled();
    expect(fixture.componentInstance.openMinimized()).toBeFalse();
  });

  it('persists "start minimized" through the host API when autostart is on', async () => {
    installApi({ supported: true, enabled: true, openMinimized: false });
    const fixture = await create();

    toggles(fixture)[1].click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.updateAutostartSettings).toHaveBeenCalledWith({ enabled: true, openMinimized: true });
    expect(fixture.componentInstance.openMinimized()).toBeTrue();
  });

  it('shows a busy spinner and ignores further clicks while a change is in flight', async () => {
    installApi({ supported: true, enabled: false, openMinimized: false });
    const fixture = await create();

    let resolveUpdate!: (state: Awaited<ReturnType<ApiService['updateAutostartSettings']>>) => void;
    api.updateAutostartSettings.and.returnValue(new Promise(resolve => resolveUpdate = resolve));

    toggles(fixture)[0].click();
    fixture.detectChanges();

    expect(fixture.componentInstance.enabledBusy()).toBeTrue();
    expect(fixture.nativeElement.querySelector('.ts-spinner')).toBeTruthy();

    toggles(fixture)[0].click();
    fixture.detectChanges();

    expect(api.updateAutostartSettings).toHaveBeenCalledTimes(1);

    resolveUpdate({ success: true, error: null, supported: true, enabled: true, openMinimized: false });
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.enabled()).toBeTrue();
    expect(fixture.componentInstance.enabledBusy()).toBeFalse();
    expect(fixture.nativeElement.querySelector('.ts-spinner')).toBeNull();
  });

  it('reloads the persisted state when an update fails', async () => {
    installApi({ supported: true, enabled: false, openMinimized: false });
    const fixture = await create();

    api.updateAutostartSettings.and.rejectWith(new Error('registration failed'));

    await fixture.componentInstance.setEnabled(true);
    await fixture.whenStable();

    expect(api.getAutostartSettings.calls.count()).toBe(2);
    expect(fixture.componentInstance.enabled()).toBeFalse();
    expect(fixture.componentInstance.enabledBusy()).toBeFalse();
  });

  it('shows the unsupported note and disables both toggles when the host is unreachable', async () => {
    api.getAutostartSettings.and.rejectWith(new Error('offline'));
    const fixture = await create();

    const inputs = toggles(fixture);
    expect(inputs.every(input => input.disabled)).toBeTrue();
    expect(fixture.nativeElement.querySelector('.startup__note')).toBeTruthy();
    expect(fixture.componentInstance.supported()).toBeFalse();
  });

  it('shows the unsupported note when the host reports autostart is not supported', async () => {
    const fixture = await create();

    const inputs = toggles(fixture);
    expect(inputs.every(input => input.disabled)).toBeTrue();
    expect(fixture.nativeElement.querySelector('.startup__note')).toBeTruthy();
  });
});
