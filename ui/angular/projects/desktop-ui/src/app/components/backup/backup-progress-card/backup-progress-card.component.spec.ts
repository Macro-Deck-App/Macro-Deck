import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GetBackupStatusResponse } from '@macro-deck/runtime';
import { BackupProgressCardComponent } from './backup-progress-card.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('BackupProgressCardComponent', () => {
  let fixture: ComponentFixture<BackupProgressCardComponent>;

  function status(overrides: Partial<GetBackupStatusResponse> = {}): GetBackupStatusResponse {
    return {
      operationId: 'op-1',
      kind: 'Create',
      stage: 'Saving',
      trigger: 'Manual',
      updatedAt: '2026-01-01T00:00:00Z',
      ...overrides,
    };
  }

  async function render(operation: GetBackupStatusResponse): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [BackupProgressCardComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(BackupProgressCardComponent);
    fixture.componentRef.setInput('operation', operation);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  it('shows the percentage from the very start of the work', async () => {
    const element = await render(status({ percentComplete: 0 }));

    expect(element.querySelector('.bpc-percent')?.textContent?.trim()).toBe('0%');
    expect(element.querySelector('.bpc-bar')?.classList).not.toContain('bpc-bar-indeterminate');
  });

  it('falls back to an indeterminate bar without a percentage', async () => {
    const element = await render(status({ stage: 'CreatingSnapshot' }));

    expect(element.querySelector('.bpc-percent')).toBeNull();
    expect(element.querySelector('.bpc-bar')?.classList).toContain('bpc-bar-indeterminate');
  });

  it('shows the remaining time next to the processed data', async () => {
    const element = await render(status({ percentComplete: 40, bytesProcessed: 1024, totalBytes: 4096, etaSeconds: 30 }));

    expect(element.querySelector('.bpc-detail')?.textContent).toContain('30 s');
  });

  function stages(element: HTMLElement): string[] {
    return Array.from(element.querySelectorAll('.bpc-stage') as NodeListOf<HTMLElement>).map(stage =>
      `${stage.textContent?.trim()}:${stage.classList.contains('bpc-stage-current') ? 'current' : stage.classList.contains('bpc-stage-done') ? 'done' : 'pending'}`);
  }

  it('lists where a backup is: preparing done, copying now, verifying next', async () => {
    const element = await render(status({ percentComplete: 30 }));

    expect(stages(element)).toEqual([
      'Preparing:done',
      'Copying and encrypting your data:current',
      'Verifying the backup:pending',
    ]);
  });

  it('names what started the backup', async () => {
    const element = await render(status({ trigger: 'BeforePluginUpdate', percentComplete: 10 }));

    expect(element.querySelector('.bpc-title')?.textContent).toContain('Creating backup');
    expect(element.querySelector('.bpc-subtitle')?.textContent).toContain('Before plugin update');
  });

  it('places checking the chosen backup as the first step of a restore', async () => {
    const element = await render(status({ kind: 'Restore', stage: 'Validating', trigger: 'BeforeRestore' }));

    expect(stages(element)).toEqual(['Checking the backup:current', 'Creating a safety backup:pending']);
  });

  it('places the safety backup as the second step of a restore', async () => {
    const element = await render(status({ trigger: 'BeforeRestore', percentComplete: 20 }));

    expect(stages(element)).toEqual(['Checking the backup:done', 'Creating a safety backup:current']);
  });

  it('lists no steps once a restore has failed, whichever step it failed in', async () => {
    const element = await render(status({ kind: 'Restore', stage: 'Failed', trigger: 'BeforeRestore', errorMessage: 'Disk full' }));

    expect(element.querySelector('.bpc-stage')).toBeNull();
    expect(element.querySelector('.bpc-error')?.textContent).toContain('Disk full');
  });

  it('offers cancel while the backup runs and reports the click', async () => {
    const element = await render(status({ percentComplete: 20 }));
    fixture.componentRef.setInput('cancellable', true);
    fixture.detectChanges();
    let cancelled = 0;
    fixture.componentInstance.cancel.subscribe(() => cancelled++);

    (element.querySelector('.bpc-actions button') as HTMLButtonElement).click();

    expect(cancelled).toBe(1);
  });

  it('offers no cancel once the backup is over', async () => {
    const element = await render(status({ stage: 'Completed', percentComplete: 100 }));
    fixture.componentRef.setInput('cancellable', true);
    fixture.detectChanges();

    expect(element.querySelector('.bpc-actions')).toBeNull();
  });
});
