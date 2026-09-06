import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CopyTextModalComponent } from './copy-text-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('CopyTextModalComponent', () => {
  let fixture: ComponentFixture<CopyTextModalComponent>;
  let component: CopyTextModalComponent;

  beforeEach(() => {
    jasmine.clock().install();
    // The modal's ghost-click grace window compares real Date.now(), not the fake timer queue -
    // mockDate() is required for jasmine.clock().tick() to advance it too.
    jasmine.clock().mockDate();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(CopyTextModalComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  function field(): HTMLInputElement {
    return fixture.nativeElement.querySelector('.copy-text-field') as HTMLInputElement;
  }

  function overlay(): HTMLElement {
    return fixture.nativeElement.querySelector('.modal-overlay') as HTMLElement;
  }

  it('defaults zIndex to null and does not set an inline z-index', () => {
    fixture.detectChanges();

    expect(component.zIndex).toBeNull();
    expect(overlay().style.zIndex).toBe('');
  });

  it('forwards zIndex to the inner shared-modal', () => {
    fixture.componentRef.setInput('zIndex', 1100);
    fixture.detectChanges();

    expect(overlay().style.zIndex).toBe('1100');
  });

  it('renders the value verbatim in a readonly field', () => {
    fixture.componentRef.setInput('value', 'icon-abc-123');
    fixture.detectChanges();

    expect(field().value).toBe('icon-abc-123');
    expect(field().hasAttribute('readonly')).toBe(true);
  });

  it('focuses and fully selects the field after the view initializes', () => {
    fixture.componentRef.setInput('value', 'icon-abc-123');
    fixture.detectChanges();

    expect(document.activeElement).toBe(field());
    expect(field().selectionStart).toBe(0);
    expect(field().selectionEnd).toBe('icon-abc-123'.length);
  });

  it('re-selects the field on click', () => {
    fixture.componentRef.setInput('value', 'icon-abc-123');
    fixture.detectChanges();

    field().setSelectionRange(2, 2);
    field().dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(field().selectionStart).toBe(0);
    expect(field().selectionEnd).toBe('icon-abc-123'.length);
  });

  it('plays the exit animation before emitting closed', () => {
    fixture.detectChanges();
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    component.onClose();
    fixture.detectChanges();

    expect(overlay().classList).toContain('closing');
    expect(closedSpy).not.toHaveBeenCalled();

    jasmine.clock().tick(150);
    expect(closedSpy).toHaveBeenCalledTimes(1);
  });

  it('emits closed on Escape', () => {
    fixture.detectChanges();
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(closedSpy).toHaveBeenCalledTimes(1);
  });

  it('emits closed on a backdrop click', () => {
    fixture.detectChanges();
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    // Skip the ghost-click grace window the modal applies right after opening.
    jasmine.clock().tick(300);
    const backdrop = overlay();
    backdrop.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    backdrop.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(closedSpy).toHaveBeenCalledTimes(1);
  });

  it('has no "try again" affordance, only the confirm button', () => {
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('[modal-footer] shared-button');
    expect(buttons.length).toBe(1);
    expect((buttons[0] as HTMLElement).textContent?.trim()).toBe('Done');
  });
});
