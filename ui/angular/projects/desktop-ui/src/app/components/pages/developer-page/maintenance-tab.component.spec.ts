import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { CopyValueComponent } from '../../copy-value/copy-value.component';

import { DataDirectoryService, RestartNoticeService } from '../../../services';
import { MaintenanceTabComponent } from './maintenance-tab.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('MaintenanceTabComponent', () => {
  let fixture: ComponentFixture<MaintenanceTabComponent>;
  let canRestart: ReturnType<typeof signal<boolean>>;
  let unsupportedReason: ReturnType<typeof signal<string | null>>;
  let restartSpy: jasmine.Spy;
  let refreshSpy: jasmine.Spy;

  let dataDirAvailable: ReturnType<typeof signal<boolean>>;
  let dataDirPath: ReturnType<typeof signal<string | null>>;
  let dataDirCanOpen: ReturnType<typeof signal<boolean>>;
  let dataDirOpening: ReturnType<typeof signal<boolean>>;
  let dataDirRefreshSpy: jasmine.Spy;
  let dataDirOpenSpy: jasmine.Spy;

  async function create(): Promise<void> {
    fixture = TestBed.createComponent(MaintenanceTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    canRestart = signal(true);
    unsupportedReason = signal<string | null>(null);
    restartSpy = jasmine.createSpy('restartNow').and.resolveTo(undefined);
    refreshSpy = jasmine.createSpy('refresh').and.resolveTo(undefined);

    dataDirAvailable = signal(false);
    dataDirPath = signal<string | null>(null);
    dataDirCanOpen = signal(false);
    dataDirOpening = signal(false);
    dataDirRefreshSpy = jasmine.createSpy('refresh').and.resolveTo(undefined);
    dataDirOpenSpy = jasmine.createSpy('open').and.resolveTo(undefined);

    await TestBed.configureTestingModule({
      imports: [MaintenanceTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: RestartNoticeService,
          useValue: {
            canRestart,
            unsupportedReason,
            restarting: signal(false),
            restartNow: restartSpy,
            refresh: refreshSpy,
          },
        },
        {
          provide: DataDirectoryService,
          useValue: {
            available: dataDirAvailable,
            path: dataDirPath,
            canOpen: dataDirCanOpen,
            opening: dataDirOpening,
            refresh: dataDirRefreshSpy,
            open: dataDirOpenSpy,
          },
        },
      ],
    }).compileComponents();
  });

  it('asks the host whether it can restart at all, without waiting for a settings visit', async () => {
    await create();

    expect(refreshSpy).toHaveBeenCalled();
  });

  it('confirms before restarting rather than acting on the first click', async () => {
    await create();

    (fixture.nativeElement.querySelector('[data-testid="restart-card"] shared-button') as HTMLElement)
      .click();
    fixture.detectChanges();

    expect(restartSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).not.toBeNull();
  });

  it('restarts through the shared notice once confirmed, tagged with where it came from', async () => {
    await create();

    fixture.componentInstance.askToRestart();
    await fixture.componentInstance.restartNow();

    expect(restartSpy).toHaveBeenCalledWith('developer');
  });

  it('explains itself instead of offering a dead button when the host cannot restart', async () => {
    canRestart.set(false);
    unsupportedReason.set('Macro Deck was not started by the desktop app.');
    await create();

    expect(fixture.nativeElement.querySelector('[data-testid="restart-card"] shared-button')).toBeNull();
    expect((fixture.nativeElement.querySelector('.maintenance__unavailable') as HTMLElement).textContent)
      .toContain('not started by the desktop app');
  });

  it('asks the host for the data directory as soon as the tab opens', async () => {
    await create();

    expect(dataDirRefreshSpy).toHaveBeenCalled();
  });

  it('shows the data directory path even when this connection cannot open it', async () => {
    dataDirAvailable.set(true);
    dataDirPath.set('/Users/x/MacroDeck');
    dataDirCanOpen.set(false);
    await create();

    const copyValue = fixture.debugElement.query(By.directive(CopyValueComponent));
    expect(copyValue).not.toBeNull();
    expect((copyValue.componentInstance as CopyValueComponent).value).toBe('/Users/x/MacroDeck');
    // Direct child only: shared-copy-value renders its own nested copy button.
    expect(
      fixture.nativeElement.querySelector('[data-testid="data-directory-card"] > shared-button')
    ).toBeNull();
  });

  it('opens the data directory through the service when the connection allows it', async () => {
    dataDirAvailable.set(true);
    dataDirPath.set('/Users/x/MacroDeck');
    dataDirCanOpen.set(true);
    await create();

    (
      fixture.nativeElement.querySelector(
        '[data-testid="data-directory-card"] > shared-button'
      ) as HTMLElement
    ).click();
    fixture.detectChanges();

    expect(dataDirOpenSpy).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
  });

  it('never says File Explorer or Finder', async () => {
    dataDirAvailable.set(true);
    dataDirPath.set('/Users/x/MacroDeck');
    dataDirCanOpen.set(false);
    await create();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toMatch(/Finder/i);
    expect(text).not.toMatch(/File Explorer/i);
    expect(text).not.toMatch(/Explorer/i);
  });
});
