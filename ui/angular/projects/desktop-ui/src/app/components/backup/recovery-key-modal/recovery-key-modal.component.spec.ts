import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RecoveryKeyModalComponent } from './recovery-key-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('RecoveryKeyModalComponent', () => {
  let fixture: ComponentFixture<RecoveryKeyModalComponent>;

  beforeEach(() => jasmine.clock().install());
  afterEach(() => jasmine.clock().uninstall());

  async function create(
    mode: 'created' | 'revealed',
    requireAcknowledgement = false,
  ): Promise<ComponentFixture<RecoveryKeyModalComponent>> {
    TestBed.configureTestingModule({
      imports: [RecoveryKeyModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
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
});
