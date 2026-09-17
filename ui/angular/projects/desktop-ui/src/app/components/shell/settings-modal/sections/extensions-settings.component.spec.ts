import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ExtensionSettingsBody } from '@macro-deck/runtime';
import { ApiService, ToggleSwitchComponent } from '@shared';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';
import { ExtensionsSettingsComponent } from './extensions-settings.component';

describe('ExtensionsSettingsComponent', () => {
  const stored: ExtensionSettingsBody = {
    storeEnabled: true,
    checkForUpdates: true,
    notifyOnUpdates: true,
    refreshIntervalMinutes: 60,
    autoUpdate: false,
  };

  async function render(api: jasmine.SpyObj<ApiService>) {
    TestBed.configureTestingModule({
      imports: [ExtensionsSettingsComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting(), { provide: ApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(ExtensionsSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('turns automatic updates on without touching the other extension settings', async () => {
    const api = jasmine.createSpyObj<ApiService>('ApiService',
      ['getExtensionSettings', 'updateExtensionSettings', 'onNotification']);
    api.onNotification.and.returnValue(new Subject<never>().asObservable());
    api.getExtensionSettings.and.resolveTo(stored);
    api.updateExtensionSettings.and.resolveTo({ ...stored, autoUpdate: true });
    const fixture = await render(api);

    const toggles = fixture.debugElement.queryAll(By.directive(ToggleSwitchComponent));
    toggles[1].componentInstance.changed.emit(true);
    await fixture.whenStable();

    expect(api.updateExtensionSettings).toHaveBeenCalledOnceWith({ autoUpdate: true });
    expect(fixture.componentInstance.settings()?.autoUpdate).toBeTrue();
  });

  it('starts with automatic updates off and update notifications on', async () => {
    const api = jasmine.createSpyObj<ApiService>('ApiService',
      ['getExtensionSettings', 'updateExtensionSettings', 'onNotification']);
    api.onNotification.and.returnValue(new Subject<never>().asObservable());
    api.getExtensionSettings.and.resolveTo(stored);
    const fixture = await render(api);

    const toggles = fixture.debugElement.queryAll(By.directive(ToggleSwitchComponent));
    expect(toggles.map(toggle => toggle.componentInstance.checkedState())).toEqual([true, false]);
  });
});
