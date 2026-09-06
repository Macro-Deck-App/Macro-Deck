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
});
