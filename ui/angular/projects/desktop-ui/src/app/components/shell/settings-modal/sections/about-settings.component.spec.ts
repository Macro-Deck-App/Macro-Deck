import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetAboutInfoResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { AboutSettingsComponent } from './about-settings.component';

describe('AboutSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  const aboutInfo: GetAboutInfoResponse = {
    version: '3.0.0-beta.42',
    isBeta: true,
    isDevelopmentBuild: false,
    commit: 'abc1234',
    buildTimestamp: '2026-07-18T12:00:00Z',
    buildNumber: '42',
    license: 'Apache-2.0',
    runtimeVersion: '.NET 10.0.0',
    operatingSystem: 'macOS 15 (Arm64)',
  };

  beforeEach(() => localStorage.clear());

  afterEach(() => {
    localStorage.clear();
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  async function createFixture(
    info: Partial<GetAboutInfoResponse> = {},
    options: { fail?: boolean } = {}
  ): Promise<ComponentFixture<AboutSettingsComponent>> {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getAboutInfo', 'onNotification', 'connectionStateSignal']);
    api.onNotification.and.returnValue(EMPTY);
    // UpdateCheckComponent (nested via AboutSettingsComponent) pulls in UpdateService, which
    // watches this to fire a reconnect re-check - never 'connected' here, so it stays inert.
    api.connectionStateSignal.and.returnValue('disconnected');
    if (options.fail) {
      api.getAboutInfo.and.rejectWith(new Error('offline'));
    } else {
      api.getAboutInfo.and.resolveTo({ ...aboutInfo, ...info });
    }

    await TestBed.configureTestingModule({
      imports: [AboutSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(AboutSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('renders version, build number, commit, runtime and operating system from the host', async () => {
    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('3.0.0-beta.42');
    expect(text).toContain('42');
    expect(text).toContain('abc1234');
    expect(text).toContain('.NET 10.0.0');
    expect(text).toContain('macOS 15 (Arm64)');
  });

  it('uses the same Development version label as the footer', async () => {
    const fixture = await createFixture({ isDevelopmentBuild: true, commit: 'abc1234' });

    const rows = Array.from(fixture.nativeElement.querySelectorAll('.settings-row')) as HTMLElement[];
    const versionRow = rows.find(row => row.textContent?.includes('Version'));
    expect(versionRow?.textContent).toContain('dev/abc1234');
  });

  it('falls back to a dev label when no build number is stamped', async () => {
    const fixture = await createFixture({ buildNumber: null });

    const rows = Array.from(fixture.nativeElement.querySelectorAll('.settings-row')) as HTMLElement[];
    const buildNumberRow = rows.find(row => row.textContent?.includes('Build number'));
    expect(buildNumberRow?.textContent).toContain('Development build');
  });

  it('formats the build timestamp as a local date string in the app language', async () => {
    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain(new Date('2026-07-18T12:00:00Z').toLocaleString('en'));
    expect(text).not.toContain('Development build');
  });

  it('falls back to a dev label when no build timestamp is stamped', async () => {
    const fixture = await createFixture({ buildTimestamp: null, commit: null });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Development build');
  });

  it('shows a readable license name', async () => {
    const fixture = await createFixture();

    expect(fixture.nativeElement.textContent).toContain('Apache License 2.0');
  });

  it('hides the bootstrapper row outside of the desktop shell', async () => {
    const fixture = await createFixture();

    expect(fixture.nativeElement.textContent).not.toContain('Bootstrapper');
  });

  it('shows the shell versions when running inside the desktop shell', async () => {
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      getShellInfo: () =>
        Promise.resolve({ shellVersion: '3.0.0-beta.42', tauriVersion: '2.9.0', webviewVersion: '620.1' }),
    };

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Bootstrapper');
    expect(text).toContain('3.0.0-beta.42 - Tauri 2.9.0 (WebView 620.1)');
  });

  it('shows an error note when the host request fails', async () => {
    const fixture = await createFixture({}, { fail: true });

    expect(fixture.nativeElement.querySelector('.about__error')).toBeTruthy();
  });
});
