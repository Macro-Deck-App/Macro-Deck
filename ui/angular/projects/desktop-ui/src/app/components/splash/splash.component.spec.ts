import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SplashComponent } from './splash.component';

describe('SplashComponent', () => {
  let fixture: ComponentFixture<SplashComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SplashComponent],
      providers: [
        provideZonelessChangeDetection(),
      ],
    });
    fixture = TestBed.createComponent(SplashComponent);
  });

  function statusMessage(): HTMLElement | null {
    return fixture.nativeElement.querySelector('.status .status-message');
  }

  it('renders the given status string exactly', async () => {
    fixture.componentRef.setInput('status', 'Preparing update…');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(statusMessage()?.textContent).toBe('Preparing update…');
  });

  it('re-renders with a new status and drops the old text', async () => {
    fixture.componentRef.setInput('status', 'Preparing update…');
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.componentRef.setInput('status', 'Connecting to Macro Deck…');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(statusMessage()?.textContent).toBe('Connecting to Macro Deck…');
    expect(fixture.nativeElement.textContent).not.toContain('Preparing update…');
  });

  it('shows a filled bar instead of the spinner once the progress is known', async () => {
    fixture.componentRef.setInput('status', 'Creating a backup… 30%');
    fixture.componentRef.setInput('progress', 30);
    fixture.detectChanges();
    await fixture.whenStable();

    const bar = fixture.nativeElement.querySelector('[role="progressbar"]');
    expect(bar?.getAttribute('aria-valuenow')).toBe('30');
    expect(fixture.nativeElement.querySelector('.spinner')).toBeNull();
  });

  it('keeps the spinner while the progress is unknown', async () => {
    fixture.componentRef.setInput('status', 'Preparing update…');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.spinner')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[role="progressbar"]')).toBeNull();
  });
});
