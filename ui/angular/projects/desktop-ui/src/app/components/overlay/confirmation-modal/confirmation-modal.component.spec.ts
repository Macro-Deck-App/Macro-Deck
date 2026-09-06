import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmationModalComponent } from './confirmation-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('ConfirmationModalComponent', () => {
  let fixture: ComponentFixture<ConfirmationModalComponent>;
  let component: ConfirmationModalComponent;

  beforeEach(() => {
    jasmine.clock().install();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(ConfirmationModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  function overlay(): HTMLElement {
    return fixture.nativeElement.querySelector('.modal-overlay') as HTMLElement;
  }

  it('plays the exit animation before emitting confirm', () => {
    const confirmSpy = jasmine.createSpy('confirm');
    component.confirm.subscribe(confirmSpy);

    component.onConfirm();
    fixture.detectChanges();

    expect(overlay().classList).toContain('closing');
    expect(confirmSpy).not.toHaveBeenCalled();

    jasmine.clock().tick(150);
    expect(confirmSpy).toHaveBeenCalledTimes(1);
  });

  it('plays the exit animation before emitting cancel', () => {
    const cancelSpy = jasmine.createSpy('cancel');
    component.cancel.subscribe(cancelSpy);

    component.onCancel();
    fixture.detectChanges();

    expect(overlay().classList).toContain('closing');
    expect(cancelSpy).not.toHaveBeenCalled();

    jasmine.clock().tick(150);
    expect(cancelSpy).toHaveBeenCalledTimes(1);
  });

  it('offers no third button by default', () => {
    expect(fixture.nativeElement.querySelectorAll('[modal-footer] shared-button').length).toBe(2);
  });

  it('renders the third choice last and emits alt for it', () => {
    const altSpy = jasmine.createSpy('alt');
    component.alt.subscribe(altSpy);
    fixture.componentRef.setInput('altText', 'Save & Close');
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('[modal-footer] shared-button');
    expect(buttons.length).toBe(3);
    expect((buttons[2] as HTMLElement).textContent?.trim()).toBe('Save & Close');

    (buttons[2] as HTMLElement).click();
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(altSpy).toHaveBeenCalledTimes(1);
  });

  it('emits cancel when the inner modal closes (Escape/backdrop path)', () => {
    const cancelSpy = jasmine.createSpy('cancel');
    component.cancel.subscribe(cancelSpy);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(cancelSpy).toHaveBeenCalledTimes(1);
  });
});

@Component({
  standalone: true,
  imports: [ConfirmationModalComponent],
  template: `<shared-confirmation-modal heading="Delete Folder" message="Sure?" />`,
})
class StaticHeadingHostComponent {}

describe('ConfirmationModalComponent heading input (regression: no native title tooltip)', () => {
  it('does not leak the heading onto the host as a native title tooltip', async () => {
    // A static `title` attribute on <shared-confirmation-modal> would both bind the input and
    // stay on the host element, showing a browser tooltip over the whole modal; the input is
    // named `heading` precisely to avoid that (issue #233).
    await TestBed.configureTestingModule({
      imports: [StaticHeadingHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    const fixture = TestBed.createComponent(StaticHeadingHostComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement.querySelector('shared-confirmation-modal') as HTMLElement;
    expect(host.hasAttribute('title')).toBe(false);
    expect(host.querySelector('.modal-title')?.textContent?.trim()).toBe('Delete Folder');
  });
});
