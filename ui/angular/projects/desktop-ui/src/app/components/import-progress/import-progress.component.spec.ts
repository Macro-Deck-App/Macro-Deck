import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { IconImportBatchModel, IconPackService } from '../../services/icon-pack.service';
import { ImportProgressComponent } from './import-progress.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('ImportProgressComponent', () => {
  let fixture: ComponentFixture<ImportProgressComponent>;
  let activeBatches: WritableSignal<ReadonlyMap<string, IconImportBatchModel>>;
  let dismissBatchSpy: jasmine.Spy;
  let cancelBatchSpy: jasmine.Spy;

  function batch(overrides: Partial<IconImportBatchModel> = {}): IconImportBatchModel {
    return {
      id: 'batch-1',
      packId: 'pack-1',
      state: 'Processing',
      processed: 3,
      failed: 0,
      total: 10,
      ...overrides,
    };
  }

  function setBatch(model: IconImportBatchModel): void {
    activeBatches.set(new Map([[model.id, model]]));
  }

  beforeEach(async () => {
    activeBatches = signal<ReadonlyMap<string, IconImportBatchModel>>(new Map());
    dismissBatchSpy = jasmine.createSpy('dismissBatch');
    cancelBatchSpy = jasmine.createSpy('cancelBatch').and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [ImportProgressComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: IconPackService,
          useValue: {
            activeBatches,
            dismissBatch: dismissBatchSpy,
            cancelBatch: cancelBatchSpy,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ImportProgressComponent);
  });

  it('renders a cancel button while the batch is still running', () => {
    setBatch(batch({ state: 'Processing' }));
    fixture.detectChanges();

    const cancelButton = fixture.nativeElement.querySelector('.cancel') as HTMLButtonElement | null;
    expect(cancelButton).not.toBeNull();
  });

  it('calls cancelBatch with the batch id when the cancel button is clicked', () => {
    setBatch(batch({ id: 'batch-42', state: 'Discovering' }));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.cancel') as HTMLButtonElement).click();

    expect(cancelBatchSpy).toHaveBeenCalledWith('batch-42');
  });

  it('does not render a cancel button once the batch is terminal', () => {
    setBatch(batch({ state: 'Completed', failed: 0 }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.cancel')).toBeNull();
  });

  it('shows the dismiss control instead of cancel once cancelled', () => {
    setBatch(batch({ state: 'Cancelled' }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.cancel')).toBeNull();
    expect(fixture.nativeElement.querySelector('.dismiss')).not.toBeNull();
  });

  it('shows a status line naming how many icons were kept after a cancel', () => {
    setBatch(batch({ state: 'Cancelled', processed: 7 }));
    fixture.detectChanges();

    const status = fixture.nativeElement.querySelector('.status') as HTMLElement;
    expect(status.textContent).toContain('7');
  });
});
