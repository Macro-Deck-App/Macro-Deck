import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetBackupSettingsResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { BackupService } from '../../../../services/backup.service';
import { BackupScheduleSettingsComponent } from './backup-schedule-settings.component';

function settings(overrides: Partial<GetBackupSettingsResponse> = {}): GetBackupSettingsResponse {
  return {
    scheduleFrequency: 'daily',
    scheduleTimeOfDay: '03:00',
    scheduleDayOfWeek: 'Sunday',
    scheduleDayOfMonth: 1,
    retentionPolicy: 'keep-latest',
    retentionKeepLatest: 7,
    beforeHostUpdate: true,
    beforePluginUpdate: true,
    preUpdateBackupSupported: true,
    minimumRetentionCount: 1,
    maximumRetentionCount: 100,
    ...overrides,
  };
}

describe('BackupScheduleSettingsComponent', () => {
  let fixture: ComponentFixture<BackupScheduleSettingsComponent>;
  let backupServiceSpy: jasmine.SpyObj<BackupService>;
  let settingsSignal: WritableSignal<GetBackupSettingsResponse | null>;

  function configure(initial: GetBackupSettingsResponse | null): void {
    backupServiceSpy = jasmine.createSpyObj<BackupService>('BackupService', ['updateSettings']);
    settingsSignal = signal(initial);
    Object.defineProperty(backupServiceSpy, 'settings', { value: settingsSignal });
    backupServiceSpy.updateSettings.and.resolveTo({ ok: true });

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.returnValue(EMPTY);

    TestBed.configureTestingModule({
      imports: [BackupScheduleSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: BackupService, useValue: backupServiceSpy },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<BackupScheduleSettingsComponent>> {
    const f = TestBed.createComponent(BackupScheduleSettingsComponent);
    f.detectChanges();
    await f.whenStable();
    return f;
  }

  function rowContaining(label: string): HTMLElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-settings-row'))
      .find(row => (row as HTMLElement).textContent?.includes(label)) as HTMLElement;
  }

  it('shows a loading state until settings arrive', async () => {
    configure(null);
    fixture = await create();

    expect(fixture.nativeElement.querySelector('shared-loading-state')).toBeTruthy();
  });

  it('keeps the pre-update row visible but disabled, showing the reason, when unsupported (issue #36)', async () => {
    configure(settings({
      preUpdateBackupSupported: false,
      preUpdateBackupUnsupportedReason: 'Macro Deck does not install updates itself on Linux.',
    }));
    fixture = await create();

    const row = rowContaining('Before Macro Deck updates');
    expect(row).toBeTruthy();
    expect(row.textContent).toContain('Macro Deck does not install updates itself on Linux.');

    const toggle = row.querySelector('input[type="checkbox"]') as HTMLInputElement;
    expect(toggle.disabled).toBeTrue();
  });

  it('leaves the pre-update row enabled when the host supports it', async () => {
    configure(settings({ preUpdateBackupSupported: true }));
    fixture = await create();

    const row = rowContaining('Before Macro Deck updates');
    const toggle = row.querySelector('input[type="checkbox"]') as HTMLInputElement;
    expect(toggle.disabled).toBeFalse();
  });

  it('hides the frequency/day/time selects while scheduling is off', async () => {
    configure(settings({ scheduleFrequency: 'off' }));
    fixture = await create();

    expect(fixture.nativeElement.textContent).not.toContain('Frequency');
  });

  it('defaults a freshly-enabled schedule to daily', async () => {
    configure(settings({ scheduleFrequency: 'off' }));
    fixture = await create();

    const row = rowContaining('Scheduled backups');
    const toggle = row.querySelector('input[type="checkbox"]') as HTMLInputElement;
    toggle.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(backupServiceSpy.updateSettings).toHaveBeenCalledWith({ scheduleFrequency: 'daily' });
  });
});
