import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiService } from '@shared';
import { DeveloperSettingsComponent } from './developer-settings.component';
import { EMPTY } from 'rxjs';

describe('DeveloperSettingsComponent', () => {
  let fixture: ComponentFixture<DeveloperSettingsComponent>;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getDeveloperSettings',
      'updateDeveloperSettings',
      'onNotification',
    ]);
    api.getDeveloperSettings.and.resolveTo({ enabled: false });
    api.onNotification.and.returnValue(EMPTY);
    api.updateDeveloperSettings.and.callFake(request => Promise.resolve({ enabled: request.enabled ?? false }));

    await TestBed.configureTestingModule({
      imports: [DeveloperSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function settle(f: ComponentFixture<DeveloperSettingsComponent>): Promise<void> {
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  }

  async function create(): Promise<ComponentFixture<DeveloperSettingsComponent>> {
    const f = TestBed.createComponent(DeveloperSettingsComponent);
    await settle(f);
    return f;
  }

  function toggle(f: ComponentFixture<DeveloperSettingsComponent>): HTMLInputElement {
    return f.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('reflects the value the host returns', async () => {
    api.getDeveloperSettings.and.resolveTo({ enabled: true });
    fixture = await create();

    expect(fixture.componentInstance.enabled()).toBeTrue();
    expect(toggle(fixture).checked).toBeTrue();
  });

  it('reflects Developer Mode being off by default', async () => {
    fixture = await create();

    expect(fixture.componentInstance.enabled()).toBeFalse();
  });

  it('persists the value the user picks', async () => {
    fixture = await create();

    fixture.componentInstance.requestSetEnabled(true);
    fixture.componentInstance.confirmEnable();
    await fixture.whenStable();

    expect(api.updateDeveloperSettings).toHaveBeenCalledWith({ enabled: true });
    expect(fixture.componentInstance.enabled()).toBeTrue();
  });

  it('rolls back to the persisted value when the update fails', async () => {
    api.getDeveloperSettings.and.resolveTo({ enabled: false });
    fixture = await create();
    api.updateDeveloperSettings.and.rejectWith(new Error('offline'));

    fixture.componentInstance.requestSetEnabled(true);
    fixture.componentInstance.confirmEnable();
    await fixture.whenStable();

    expect(fixture.componentInstance.enabled()).toBeFalse();
  });

  // Enabling is what lets an application the user runs themselves join as a plugin and permits
  // installing unsigned ones, so it must not happen on the toggle alone.
  it('asks before enabling, and does nothing if the confirmation is dismissed', async () => {
    fixture = await create();

    fixture.componentInstance.requestSetEnabled(true);
    await fixture.whenStable();

    expect(api.updateDeveloperSettings).not.toHaveBeenCalled();
    expect(fixture.componentInstance.enabled()).toBeFalse();

    fixture.componentInstance.cancelEnable();
    await fixture.whenStable();

    expect(api.updateDeveloperSettings).not.toHaveBeenCalled();
    expect(fixture.componentInstance.enabled()).toBeFalse();
  });

  // Turning it off only ever narrows what the host accepts, so it applies straight away.
  it('turns Developer Mode off without asking', async () => {
    api.getDeveloperSettings.and.resolveTo({ enabled: true });
    api.updateDeveloperSettings.and.resolveTo({ enabled: false });
    fixture = await create();

    fixture.componentInstance.requestSetEnabled(false);
    await fixture.whenStable();

    expect(api.updateDeveloperSettings).toHaveBeenCalledWith({ enabled: false });
    expect(fixture.componentInstance.enabled()).toBeFalse();
  });
});
