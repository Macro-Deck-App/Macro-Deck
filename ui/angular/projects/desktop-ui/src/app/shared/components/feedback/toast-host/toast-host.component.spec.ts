import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastService } from '../../../services/toast.service';
import { ToastHostComponent } from './toast-host.component';
import { provideLocalizationTesting } from '../../../localization/localization-test-support';

describe('ToastHostComponent', () => {
  let fixture: ComponentFixture<ToastHostComponent>;
  let toasts: ToastService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ToastHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting(), ToastService],
    }).compileComponents();

    fixture = TestBed.createComponent(ToastHostComponent);
    toasts = TestBed.inject(ToastService);
    fixture.detectChanges();
  });

  it('renders nothing while the queue is empty', () => {
    expect(fixture.nativeElement.querySelectorAll('.th-toast').length).toBe(0);
  });

  it('renders the message and detail of a queued toast', async () => {
    toasts.show('Profile exported', { detail: 'Saved to /tmp/deck.macroDeckProfile', durationMs: 0 });
    await fixture.whenStable();

    const toast = fixture.nativeElement.querySelector('.th-toast') as HTMLElement;
    expect(toast.querySelector('.th-message')?.textContent).toContain('Profile exported');
    expect(toast.querySelector('.th-detail')?.textContent).toContain('Saved to /tmp/deck.macroDeckProfile');
    expect(toast.classList).not.toContain('th-error');
  });

  it('marks an error toast', async () => {
    toasts.show('Export failed', { variant: 'error', durationMs: 0 });
    await fixture.whenStable();

    expect((fixture.nativeElement.querySelector('.th-toast') as HTMLElement).classList).toContain('th-error');
  });

  it('dismisses a toast from its close button', async () => {
    toasts.show('Profile exported', { durationMs: 0 });
    await fixture.whenStable();

    (fixture.nativeElement.querySelector('.th-dismiss') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('.th-toast').length).toBe(0);
    expect(toasts.toasts()).toEqual([]);
  });
});
