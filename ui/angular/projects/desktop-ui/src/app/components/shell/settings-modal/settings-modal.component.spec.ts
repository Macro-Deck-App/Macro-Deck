import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { LogEntryLevel } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { SettingsModalService } from '../../../services/settings-modal.service';
import { SETTINGS_RAIL_COLLAPSED_KEY, SettingsModalComponent } from './settings-modal.component';

const TLS_DEFAULTS = {
  tlsEnabled: false,
  tlsMode: 'Additional' as const,
  tlsHttpsPort: 8443,
  defaultTlsHttpsPort: 8443,
  activeTlsEnabled: false,
  activeTlsMode: 'Disabled' as const,
  activeTlsHttpsPort: null,
  tlsFailure: '',
  tlsRejection: '',
  tlsCertificateConfigured: false,
  tlsCertificateSource: null,
  tlsCertificateSubject: null,
  tlsCertificateFingerprint: null,
  tlsCertificateNotBefore: null,
  tlsCertificateNotAfter: null,
  tlsCertificateExpired: false,
  tlsCertificateNotYetValid: false,
  tlsCertificateIssuedByAuthority: false,
  tlsAuthorityConfigured: false,
  tlsAuthoritySubject: null,
  tlsAuthorityFingerprint: null,
  tlsAuthorityNotBefore: null,
  tlsAuthorityNotAfter: null,
};

const ENGLISH_SETTINGS_LABELS: Record<string, string> = {
  'macrodeck:Settings.General': 'General',
  'macrodeck:Settings.Appearance': 'Appearance',
  'macrodeck:Settings.Startup': 'Startup',
  'macrodeck:Settings.Language': 'Language',
  'macrodeck:Settings.Connectivity': 'Connectivity',
  'macrodeck:Settings.Network': 'Network',
  'macrodeck:Settings.Devices': 'Devices',
  'macrodeck:Settings.PrivacyAndSecurity': 'Privacy & Security',
  'macrodeck:Settings.Security': 'Security',
  'macrodeck:Settings.BackupAndData': 'Backup & Data',
  'macrodeck:Settings.Backups': 'Backups',
  'macrodeck:Settings.Advanced': 'Advanced',
  'macrodeck:Settings.Adb': 'ADB',
  'macrodeck:Settings.Logging': 'Logging',
  'macrodeck:Settings.Developer': 'Developer',
  'macrodeck:Settings.About': 'About',
};

class FakeMediaQueryList extends EventTarget {
  matches = false;

  constructor(readonly media: string) {
    super();
  }

  emit(matches: boolean): void {
    this.matches = matches;
    this.dispatchEvent(Object.assign(new Event('change'), { matches }));
  }
}

describe('SettingsModalComponent', () => {
  let fixture: ComponentFixture<SettingsModalComponent>;
  let component: SettingsModalComponent;
  let service: SettingsModalService;
  let api: jasmine.SpyObj<ApiService>;
  let narrowQuery: FakeMediaQueryList;

  beforeEach(async () => {
    localStorage.clear();
    localStorage.setItem('md.localization.translations', JSON.stringify(ENGLISH_SETTINGS_LABELS));

    narrowQuery = new FakeMediaQueryList('(max-width: 768px)');
    const realMatchMedia = window.matchMedia.bind(window);
    spyOn(window, 'matchMedia').and.callFake((query: string) =>
      query === narrowQuery.media ? (narrowQuery as unknown as MediaQueryList) : realMatchMedia(query));

    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getAppearanceSettings',
      'updateAppearanceSettings',
      'getLoggingSettings',
      'updateLoggingSettings',
      'getNetworkSettings',
      'restartApplication',
      'onNotification',
      'getDeveloperSettings',
      'updateDeveloperSettings',
      'getBackups',
      'getBackupSettings',
      'getBackupRecoveryKeyState',
      'getBackupStatus',
      'getConnectSession',
      'getConnectAvatarUrl',
      'onConnectSessionChanged',
      'getLocalization',
      'updateLocalizationSettings',
      'getMigrationSources',
    ]);
    // ConnectAccountService reads this in a constructor effect, so every category in the modal
    // needs it - createSpyObj only stubs methods, never signal-valued properties.
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    api.onNotification.and.returnValue(EMPTY);
    api.onConnectSessionChanged.and.returnValue(EMPTY);
    api.getBackups.and.resolveTo({ backups: [], retentionKeepLatest: 7 });
    api.getBackupSettings.and.resolveTo({
      scheduleFrequency: 'off',
      scheduleTimeOfDay: '03:00',
      scheduleDayOfWeek: 'Sunday',
      scheduleDayOfMonth: 1,
      retentionPolicy: 'keep-latest',
      retentionKeepLatest: 7,
      beforeHostUpdate: false,
      beforePluginUpdate: false,
      preUpdateBackupSupported: true,
      minimumRetentionCount: 1,
      maximumRetentionCount: 100,
    });
    api.getBackupRecoveryKeyState.and.resolveTo({ availability: 'None' });
    api.getBackupStatus.and.resolveTo({
      kind: 'Create', stage: 'Idle', trigger: 'Manual', updatedAt: '2026-08-18T00:00:00Z',
    });
    api.getAppearanceSettings.and.resolveTo({ themeMode: 'dark', accentColor: '#2196F3' });
    api.updateAppearanceSettings.and.resolveTo({ themeMode: 'dark', accentColor: '#2196F3' });
    const loggingSettings = {
      minimumLevel: LogEntryLevel.Information,
      defaultMinimumLevel: LogEntryLevel.Information,
    };
    api.getNetworkSettings.and.resolveTo({
      publicPort: 9100,
      defaultPublicPort: 8193,
      activePublicPort: 8193,
      overriddenByEnvironment: false,
      configuredPortIgnored: false,
      publicListenerUnavailable: false,
      restartRequired: true,
      restartSupported: true,
      restartUnsupportedReason: null,
      minimumPublicPort: 1024,
      maximumPublicPort: 65535,
      ...TLS_DEFAULTS,
    });
    api.restartApplication.and.resolveTo({ success: true, supported: true, error: null });
    api.getLoggingSettings.and.resolveTo(loggingSettings);
    api.updateLoggingSettings.and.resolveTo(loggingSettings);
    api.getDeveloperSettings.and.resolveTo({ enabled: false });
    api.updateDeveloperSettings.and.resolveTo({ enabled: false });
    api.getConnectSession.and.resolveTo({
      status: 'signedOut',
      connectivity: 'ok',
      offlineSince: null,
      account: null,
      lastSuccessfulRefreshUtc: null,
      message: null,
      signInFailure: null,
      accountManagementUrl: 'https://accounts.macro-deck.app/',
    });
    api.getConnectAvatarUrl.and.returnValue('http://localhost/api/connect/avatar');
    api.getLocalization.and.resolveTo({
      culture: 'en', fallbackCulture: 'en', translations: {}, availableCultures: ['en'], followSystem: false,
    });
    api.updateLocalizationSettings.and.resolveTo({
      success: true, error: null, culture: 'en', fallbackCulture: 'en', followSystem: false,
    });
    api.getMigrationSources.and.resolveTo({ sources: [] });

    await TestBed.configureTestingModule({
      imports: [SettingsModalComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();

    service = TestBed.inject(SettingsModalService);
    service.open('appearance');

    fixture = TestBed.createComponent(SettingsModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the category rail and the appearance section by default', () => {
    const railButtons = fixture.nativeElement.querySelectorAll('.settings-nav__item');
    expect(railButtons.length).toBe(13);
    expect(fixture.nativeElement.querySelector('app-appearance-settings')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.settings-modal__title')?.textContent).toContain('Appearance');
  });

  it('announces a pending port restart under the header of every category', async () => {
    await fixture.whenStable();
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('.settings-modal__restart');
    expect(banner).not.toBeNull();
    expect(banner.textContent).toContain('9100');

    (banner.querySelector('shared-button button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(api.restartApplication).toHaveBeenCalledTimes(1);
  });

  it('explains instead of offering a restart the host cannot perform', async () => {
    api.getNetworkSettings.and.resolveTo({
      publicPort: 9100,
      defaultPublicPort: 8193,
      activePublicPort: 8193,
      overriddenByEnvironment: false,
      configuredPortIgnored: false,
      publicListenerUnavailable: false,
      restartRequired: true,
      restartSupported: false,
      restartUnsupportedReason: 'Restarting is only available in the installed desktop app',
      minimumPublicPort: 1024,
      maximumPublicPort: 65535,
      ...TLS_DEFAULTS,
    });
    const standalone = TestBed.createComponent(SettingsModalComponent);
    standalone.detectChanges();
    await standalone.whenStable();
    standalone.detectChanges();

    const banner = standalone.nativeElement.querySelector('.settings-modal__restart');
    expect(banner.textContent).toContain('only available in the installed desktop app');
    expect(banner.querySelector('shared-button')).toBeNull();
  });

  it('exposes a close affordance labelled for assistive tech', () => {
    const close = fixture.nativeElement.querySelector('.settings-modal__close') as HTMLElement;
    expect(close).toBeTruthy();
    expect(close.getAttribute('aria-label')).toBe('Close settings');
  });

  it('switches the active category through the service', () => {
    component.selectCategory('security');
    expect(service.activeCategory()).toBe('security');
    expect(component.activeLabel()).toBe('Security');
  });

  it('renders the backups section when its category is selected', async () => {
    component.selectCategory('backups');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.activeLabel()).toBe('Backups');
    expect(fixture.nativeElement.querySelector('app-backups-settings')).toBeTruthy();
  });

  it('renders the migration section when its category is selected', () => {
    component.selectCategory('migration');
    fixture.detectChanges();

    expect(component.activeLabel()).toBe('Migration');
    expect(fixture.nativeElement.querySelector('app-migration-settings')).toBeTruthy();
  });

  it('re-labels the rail in place when the language changes', async () => {
    const rail = (): (string | undefined)[] =>
      [...fixture.nativeElement.querySelectorAll('.settings-nav__item')].map(
        (item: Element) => item.textContent?.trim());

    expect(rail()).toContain('Appearance');

    // The surface is already on screen: a language change has to re-label what is rendered, not just
    // what is rendered next. Nothing re-opens the modal and no new request is made for the tree.
    api.getLocalization.and.resolveTo({
      culture: 'de',
      fallbackCulture: 'en',
      translations: {
        ...ENGLISH_SETTINGS_LABELS,
        'macrodeck:Settings.Appearance': 'Darstellung',
        'macrodeck:Settings.Language': 'Sprache',
        'macrodeck:Settings.Advanced': 'Erweitert',
      },
      availableCultures: ['de', 'en'],
      followSystem: false,
    });

    await TestBed.inject(LocalizationService).loadFromHost();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(rail()).toContain('Darstellung');
    expect(rail()).toContain('Sprache');
    expect(rail()).not.toContain('Appearance');
  });

  it('renders the language section when its category is selected', () => {
    component.selectCategory('language');
    fixture.detectChanges();

    expect(component.activeLabel()).toBe('Language');
    expect(fixture.nativeElement.querySelector('app-language-settings')).toBeTruthy();
  });

  it('groups the categories and keeps About apart from the configuration groups', () => {
    const groups = Array.from(
      fixture.nativeElement.querySelectorAll('.settings-nav__group'),
    ) as HTMLElement[];

    expect(groups.map(group => group.querySelector('.settings-nav__group-label')?.textContent?.trim()))
      .toEqual(['General', 'Connectivity', 'Privacy & Security', 'Backup & Data', 'Advanced', undefined]);

    expect(groups.map(group => Array.from(group.querySelectorAll('.settings-nav__item'))
      .map(item => item.textContent?.trim())))
      .toEqual([
        ['Appearance', 'Startup', 'Language'],
        ['Network', 'Devices', 'Device clients'],
        ['Security'],
        ['Backups', 'Migration'],
        ['ADB', 'Logging', 'Developer'],
        ['About'],
      ]);

    expect(groups[5].classList).toContain('settings-nav__group--divided');
  });

  it('renders the logging section when its category is selected', () => {
    component.selectCategory('logging');
    fixture.detectChanges();

    expect(component.activeLabel()).toBe('Logging');
    expect(fixture.nativeElement.querySelector('app-logging-settings')).toBeTruthy();
  });

  it('renders the developer section when its category is selected', () => {
    component.selectCategory('developer');
    fixture.detectChanges();

    expect(component.activeLabel()).toBe('Developer');
    expect(fixture.nativeElement.querySelector('app-developer-settings')).toBeTruthy();
  });

  it('clears open state when the modal signals close (escape/backdrop)', () => {
    component.onModalClose();
    expect(service.isOpen()).toBeFalse();
  });

  it('plays the exit animation before closing from the custom close button', () => {
    jasmine.clock().install();
    try {
      const close = fixture.nativeElement.querySelector('.settings-modal__close') as HTMLButtonElement;
      close.click();

      expect(service.isOpen()).toBeTrue();

      jasmine.clock().tick(150);
      expect(service.isOpen()).toBeFalse();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('sizes the dialog with a higher width and height ceiling than the old fixed cap', () => {
    const dialog = fixture.nativeElement.querySelector('.modal-dialog') as HTMLElement;
    expect(dialog.style.maxWidth).toBe('1280px');

    const host = fixture.nativeElement.querySelector('shared-modal') as HTMLElement;
    expect(getComputedStyle(host).getPropertyValue('--modal-height').trim()).toBe('min(48.75rem, 90vh)');
  });

  it('collapses the rail to an icon column when the viewport crosses the breakpoint while open', async () => {
    const modal = (): HTMLElement => fixture.nativeElement.querySelector('.settings-modal');
    expect(modal().classList).not.toContain('settings-modal--collapsed');

    narrowQuery.emit(true);
    await fixture.whenStable();

    expect(modal().classList).toContain('settings-modal--collapsed');

    narrowQuery.emit(false);
    await fixture.whenStable();

    expect(modal().classList).not.toContain('settings-modal--collapsed');
  });

  it('stays a scrolling vertical rail at every width instead of an unscrollable tab strip', async () => {
    const rail = (): HTMLElement => fixture.nativeElement.querySelector('.settings-modal__rail');
    expect(getComputedStyle(rail()).overflowY).toBe('auto');

    narrowQuery.emit(true);
    await fixture.whenStable();
    fixture.detectChanges();

    const styles = getComputedStyle(rail());
    expect(styles.overflowY).toBe('auto');
    expect(styles.flexDirection).toBe('column');
  });

  it('collapses and expands on the toggle, and remembers the choice', async () => {
    const toggle = fixture.nativeElement.querySelector('.settings-modal__rail-toggle') as HTMLButtonElement;
    expect(component.railCollapsed()).toBeFalse();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');

    toggle.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.railCollapsed()).toBeTrue();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(localStorage.getItem(SETTINGS_RAIL_COLLAPSED_KEY)).toBe('1');

    toggle.click();
    await fixture.whenStable();

    expect(component.railCollapsed()).toBeFalse();
    expect(localStorage.getItem(SETTINGS_RAIL_COLLAPSED_KEY)).toBe('0');
  });

  it('opens collapsed when that was the remembered choice', async () => {
    localStorage.setItem(SETTINGS_RAIL_COLLAPSED_KEY, '1');

    const reopened = TestBed.createComponent(SettingsModalComponent);
    reopened.detectChanges();
    await reopened.whenStable();

    expect(reopened.componentInstance.railCollapsed()).toBeTrue();
  });

  it('expands on the toggle even while the narrow viewport forces the collapse', async () => {
    narrowQuery.emit(true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component.railCollapsed()).toBeTrue();

    (fixture.nativeElement.querySelector('.settings-modal__rail-toggle') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(component.railCollapsed()).toBeFalse();
  });

  it('re-applies the responsive collapse once the constraint genuinely changes again', async () => {
    narrowQuery.emit(true);
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.settings-modal__rail-toggle') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(component.railCollapsed()).toBeFalse();

    narrowQuery.emit(false);
    await fixture.whenStable();
    narrowQuery.emit(true);
    await fixture.whenStable();

    expect(component.railCollapsed()).toBeTrue();
  });

  it('does not persist a collapse the narrow viewport forced', async () => {
    narrowQuery.emit(true);
    await fixture.whenStable();

    expect(component.railCollapsed()).toBeTrue();
    expect(localStorage.getItem(SETTINGS_RAIL_COLLAPSED_KEY)).toBeNull();
  });

  it('lets the section cards fill the scrolling body instead of a fixed content column', () => {
    const body = fixture.nativeElement.querySelector('.settings-modal__body') as HTMLElement;
    const section = fixture.nativeElement.querySelector('.appearance') as HTMLElement;

    expect(body.contains(section)).toBeTrue();
    expect(fixture.nativeElement.querySelector('.settings-modal__content')).toBeNull();
    expect(getComputedStyle(section).maxWidth).toBe('none');

    expect(getComputedStyle(body).overflowY).toBe('auto');
  });

  it('pins the account block above every settings category', () => {
    const account = fixture.nativeElement.querySelector('.settings-modal__account') as Node;
    const rail = fixture.nativeElement.querySelector('shared-settings-nav') as Node;

    expect(account).toBeTruthy();
    expect(rail).toBeTruthy();
    // DOCUMENT_POSITION_FOLLOWING on the rail (relative to account) means account comes first.
    expect(account.compareDocumentPosition(rail) & Node.DOCUMENT_POSITION_FOLLOWING)
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  it('keeps the account block first when the rail collapses', async () => {
    const account = (): Node => fixture.nativeElement.querySelector('.settings-modal__account');
    const rail = (): Node => fixture.nativeElement.querySelector('shared-settings-nav');

    expect(account().compareDocumentPosition(rail()) & Node.DOCUMENT_POSITION_FOLLOWING)
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING);

    narrowQuery.emit(true);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(account().compareDocumentPosition(rail()) & Node.DOCUMENT_POSITION_FOLLOWING)
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  it('switches to the account pane and labels it, without adding it to the category rail', () => {
    expect(fixture.nativeElement.querySelectorAll('.settings-nav__item').length).toBe(13);

    component.selectAccount();
    fixture.detectChanges();

    expect(service.activeCategory()).toBe('account');
    expect(component.activeLabel()).toBe('Account');
    expect(fixture.nativeElement.querySelector('app-account-settings')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.settings-modal__account')?.getAttribute('aria-current')).toBe('true');
  });
});
