import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LogEntryLevel } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { LoggingSettingsComponent } from './logging-settings.component';
import { EMPTY } from 'rxjs';

describe('LoggingSettingsComponent', () => {
  let fixture: ComponentFixture<LoggingSettingsComponent>;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getLoggingSettings',
      'updateLoggingSettings',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    api.getLoggingSettings.and.resolveTo({
      minimumLevel: LogEntryLevel.Information,
      defaultMinimumLevel: LogEntryLevel.Information,
    });
    api.updateLoggingSettings.and.callFake(request =>
      Promise.resolve({ ...request, defaultMinimumLevel: LogEntryLevel.Information })
    );

    await TestBed.configureTestingModule({
      imports: [LoggingSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function settle(f: ComponentFixture<LoggingSettingsComponent>): Promise<void> {
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  }

  async function create(): Promise<ComponentFixture<LoggingSettingsComponent>> {
    const f = TestBed.createComponent(LoggingSettingsComponent);
    await settle(f);
    await settle(f);
    return f;
  }

  function trigger(f: ComponentFixture<LoggingSettingsComponent>): HTMLButtonElement {
    return f.nativeElement.querySelector('shared-select .control');
  }

  function optionLabels(f: ComponentFixture<LoggingSettingsComponent>): string[] {
    return Array.from(f.nativeElement.querySelectorAll('.sel-option')).map(option =>
      (option as HTMLElement).textContent!.trim()
    );
  }

  it('shows the level the host has persisted', async () => {
    api.getLoggingSettings.and.resolveTo({
      minimumLevel: LogEntryLevel.Warning,
      defaultMinimumLevel: LogEntryLevel.Information,
    });
    fixture = await create();

    expect(trigger(fixture).textContent).toContain('Warning');
  });

  it('offers every level and marks the build default', async () => {
    api.getLoggingSettings.and.resolveTo({
      minimumLevel: LogEntryLevel.Warning,
      defaultMinimumLevel: LogEntryLevel.Debug,
    });
    fixture = await create();

    trigger(fixture).click();
    fixture.detectChanges();

    expect(optionLabels(fixture)).toEqual([
      'Fatal',
      'Error',
      'Warning',
      'Information',
      'Debug (default)',
      'Verbose',
    ]);
  });

  it('persists the level the user picks', async () => {
    fixture = await create();

    trigger(fixture).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('.sel-option')[5] as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(api.updateLoggingSettings).toHaveBeenCalledWith({ minimumLevel: LogEntryLevel.Verbose });
  });

  it('settles on the level the host applied, not the one that was requested', async () => {
    api.updateLoggingSettings.and.resolveTo({
      minimumLevel: LogEntryLevel.Information,
      defaultMinimumLevel: LogEntryLevel.Information,
    });
    fixture = await create();

    await fixture.componentInstance.setLevel(LogEntryLevel.Verbose);
    fixture.detectChanges();

    expect(fixture.componentInstance.minimumLevel()).toBe(LogEntryLevel.Information);
  });

  it('restores the persisted level when the update fails', async () => {
    api.getLoggingSettings.and.resolveTo({
      minimumLevel: LogEntryLevel.Warning,
      defaultMinimumLevel: LogEntryLevel.Information,
    });
    api.updateLoggingSettings.and.rejectWith(new Error('offline'));
    fixture = await create();

    await fixture.componentInstance.setLevel(LogEntryLevel.Verbose);
    fixture.detectChanges();

    expect(fixture.componentInstance.minimumLevel()).toBe(LogEntryLevel.Warning);
  });

  it('still renders when the host is unreachable', async () => {
    api.getLoggingSettings.and.rejectWith(new Error('offline'));
    fixture = await create();

    expect(trigger(fixture)).toBeTruthy();
    expect(fixture.componentInstance.loaded()).toBeTrue();
  });
});
