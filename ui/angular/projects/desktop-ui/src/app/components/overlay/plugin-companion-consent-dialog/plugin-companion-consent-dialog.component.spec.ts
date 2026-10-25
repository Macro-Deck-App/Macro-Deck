import { computed, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CompanionAppStatus, UserNotification } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { NotificationCenterService } from '../../../services/notification-center.service';
import { SettingsModalService } from '../../../services/settings-modal.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { PluginCompanionConsentDialogComponent } from './plugin-companion-consent-dialog.component';

describe('PluginCompanionConsentDialogComponent', () => {
  let fixture: ComponentFixture<PluginCompanionConsentDialogComponent>;
  let notifications: ReturnType<typeof signal<UserNotification[]>>;
  let resolveLocallySpy: jasmine.Spy;
  let settingsOpenSpy: jasmine.Spy;
  let pluginAccessSpy: jasmine.Spy;

  const companionRequest: UserNotification = {
    id: 'companion-1',
    sequence: 1,
    timestamp: '2026-10-12T10:00:00+00:00',
    severity: 'Warning',
    kind: 'Security',
    title: 'A plugin wants to use your Companion devices',
    message: 'Phone Tools wants to work with the phones and tablets connected through Macro Deck Companion.',
    sourceName: 'Phone Tools',
    actions: [{ kind: 'EnablePluginCompanionDevices' }, { kind: 'DismissNotification' }],
  };

  beforeEach(async () => {
    notifications = signal<UserNotification[]>([]);
    resolveLocallySpy = jasmine.createSpy('resolveLocally');
    settingsOpenSpy = jasmine.createSpy('open');

    await TestBed.configureTestingModule({
      imports: [PluginCompanionConsentDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: NotificationCenterService,
          useValue: { notifications: computed(() => notifications()), resolveLocally: resolveLocallySpy },
        },
        { provide: SettingsModalService, useValue: { open: settingsOpenSpy } },
      ],
    }).compileComponents();

    pluginAccessSpy = spyOn(TestBed.inject(ApiService), 'updateCompanionAppPluginAccess')
      .and.resolveTo({ allowPlugins: true } as CompanionAppStatus);

    fixture = TestBed.createComponent(PluginCompanionConsentDialogComponent);
  });

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function button(label: string): HTMLButtonElement {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(candidate => candidate.textContent!.includes(label))!;
  }

  function expectResolvedLocally(): void {
    expect(resolveLocallySpy).toHaveBeenCalledTimes(1);
    const matches = resolveLocallySpy.calls.mostRecent().args[0] as (entry: UserNotification) => boolean;
    expect(matches(companionRequest)).toBeTrue();
    expect(matches({ ...companionRequest, id: 'other-consent', action: undefined, actions: [{ kind: 'EnablePluginAdb' }] })).toBeFalse();
    expect(matches({ ...companionRequest, id: 'unrelated', action: undefined, actions: [{ kind: 'OpenLogs' }] })).toBeFalse();
  }

  it('stays out of the way while no plugin is waiting for Companion devices', async () => {
    notifications.set([{ ...companionRequest, id: 'other', actions: [{ kind: 'EnablePluginAdb' }], title: 'A plugin wants to use ADB' }]);

    await render();

    expect(fixture.nativeElement.textContent).not.toContain('A plugin wants to use your Companion devices');
  });

  it('asks with the plugin name and says what an allowed plugin can do', async () => {
    notifications.set([companionRequest]);

    await render();

    expect(fixture.nativeElement.textContent).toContain('Phone Tools wants to work with the phones and tablets');
    expect(fixture.nativeElement.textContent).toContain('A full screen screenshot still has to be approved on the device');
  });

  it('allows plugins to use Companion devices and clears the question', async () => {
    notifications.set([companionRequest]);
    await render();

    button('Allow').click();
    await new Promise(resolve => setTimeout(resolve));

    expect(pluginAccessSpy).toHaveBeenCalledOnceWith(true);
    expectResolvedLocally();
    expect(settingsOpenSpy).not.toHaveBeenCalled();
  });

  it('opens the Companion app settings when the change could not be saved', async () => {
    pluginAccessSpy.and.rejectWith(new Error('offline'));
    notifications.set([companionRequest]);
    await render();

    button('Allow').click();
    await new Promise(resolve => setTimeout(resolve));

    expect(resolveLocallySpy).not.toHaveBeenCalled();
    expect(settingsOpenSpy).toHaveBeenCalledWith('license');
  });

  it('closes on Not now but keeps the request in the notifications', async () => {
    notifications.set([companionRequest]);
    await render();

    button('Not now').click();
    await render();

    expect(pluginAccessSpy).not.toHaveBeenCalled();
    expect(resolveLocallySpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).not.toContain('Phone Tools wants to work');
  });
});
