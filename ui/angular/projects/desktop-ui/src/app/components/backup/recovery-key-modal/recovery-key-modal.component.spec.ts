import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastService } from '@shared';
import { FileSaveService } from '../../../services/file-save.service';
import { RecoveryKeyModalComponent } from './recovery-key-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('RecoveryKeyModalComponent', () => {
  let fixture: ComponentFixture<RecoveryKeyModalComponent>;
  let fileSave: jasmine.SpyObj<FileSaveService>;
  let toasts: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    jasmine.clock().install();
    fileSave = jasmine.createSpyObj<FileSaveService>('FileSaveService', ['save']);
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
  });
  afterEach(() => jasmine.clock().uninstall());

  async function create(
    mode: 'created' | 'revealed',
    requireAcknowledgement = false,
  ): Promise<ComponentFixture<RecoveryKeyModalComponent>> {
    TestBed.configureTestingModule({
      imports: [RecoveryKeyModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: FileSaveService, useValue: fileSave },
        { provide: ToastService, useValue: toasts },
      ],
    });
    const f = TestBed.createComponent(RecoveryKeyModalComponent);
    f.componentRef.setInput('mode', mode);
    f.componentRef.setInput('key', 'MDBK-TEST-KEY-0000');
    f.componentRef.setInput('requireAcknowledgement', requireAcknowledgement);
    f.detectChanges();
    return f;
  }

  function confirmButton(): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-button button'))
      .find(el => ['Done', 'Close'].includes((el as HTMLElement).textContent?.trim() ?? '')) as HTMLButtonElement;
  }

  function checkbox(): HTMLInputElement | null {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('disables the confirm button and never fires acknowledge until the checkbox is ticked', async () => {
    fixture = await create('created');
    const acknowledged = jasmine.createSpy('acknowledged');
    fixture.componentInstance.acknowledged.subscribe(acknowledged);

    expect(confirmButton().disabled).toBeTrue();
    fixture.componentInstance.onConfirm();
    jasmine.clock().tick(150);
    expect(acknowledged).not.toHaveBeenCalled();

    checkbox()!.click();
    fixture.detectChanges();
    expect(confirmButton().disabled).toBeFalse();

    fixture.componentInstance.onConfirm();
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(acknowledged).toHaveBeenCalledTimes(1);
  });

  it('shows the key and has no gating checkbox in revealed mode, and closes without acknowledging', async () => {
    fixture = await create('revealed');
    const acknowledged = jasmine.createSpy('acknowledged');
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.acknowledged.subscribe(acknowledged);
    fixture.componentInstance.closed.subscribe(closed);

    expect(checkbox()).toBeNull();
    expect(fixture.nativeElement.textContent as string).toContain('MDBK-TEST-KEY-0000');
    expect(confirmButton().disabled).toBeFalse();

    fixture.componentInstance.onConfirm();
    jasmine.clock().tick(150);

    expect(acknowledged).not.toHaveBeenCalled();
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('gates revealed mode on the checkbox and emits acknowledged when requireAcknowledgement is set', async () => {
    fixture = await create('revealed', true);
    const acknowledged = jasmine.createSpy('acknowledged');
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.acknowledged.subscribe(acknowledged);
    fixture.componentInstance.closed.subscribe(closed);

    expect(checkbox()).not.toBeNull();
    expect(confirmButton().disabled).toBeTrue();

    fixture.componentInstance.onConfirm();
    jasmine.clock().tick(150);
    expect(acknowledged).not.toHaveBeenCalled();

    checkbox()!.click();
    fixture.detectChanges();
    expect(confirmButton().disabled).toBeFalse();

    fixture.componentInstance.onConfirm();
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(acknowledged).toHaveBeenCalledTimes(1);
    expect(closed).not.toHaveBeenCalled();
  });
  function downloadButton(): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-button button'))
      .find(el => (el as HTMLElement).textContent?.includes('Download as file')) as HTMLButtonElement;
  }

  it('saves the key file where the user chooses, like any other export', async () => {
    fileSave.save.and.resolveTo({ status: 'saved', path: '/Users/me/key.txt', viaDialog: true });
    fixture = await create('revealed');

    downloadButton().click();
    await fixture.whenStable();

    const [blob, name] = fileSave.save.calls.mostRecent().args;
    expect(name).toBe('macro-deck-recovery-key.txt');
    expect(await blob.text()).toBe('MDBK-TEST-KEY-0000');
    expect(toasts.show).not.toHaveBeenCalled();
  });

  it('says so when the key file could not be written', async () => {
    fileSave.save.and.resolveTo({ status: 'error', message: 'EACCES' });
    fixture = await create('revealed');

    downloadButton().click();
    await fixture.whenStable();
    await Promise.resolve();

    expect(toasts.show).toHaveBeenCalledWith('The file could not be written', jasmine.objectContaining({ variant: 'error' }));
  });
});
