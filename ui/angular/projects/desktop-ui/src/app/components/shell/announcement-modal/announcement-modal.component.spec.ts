import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Announcement } from '@macro-deck/runtime';
import { AnnouncementService } from '../../../services/announcement.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { AnnouncementModalComponent } from './announcement-modal.component';

describe('AnnouncementModalComponent', () => {
  const announcement: Announcement = {
    number: 4,
    title: 'Macro Deck 3 is here',
    content: '## What\'s new\n\nRead the [release notes](https://macro-deck.app/notes).',
    publishedAt: '2026-09-28T15:58:49.672+00:00',
    updatedAt: '2026-09-28T16:08:13.377+00:00',
  };

  let pending: ReturnType<typeof signal<Announcement | null>>;
  let dismiss: jasmine.Spy;

  beforeEach(() => {
    pending = signal<Announcement | null>(announcement);
    dismiss = jasmine.createSpy('dismiss').and.callFake(() => pending.set(null));
    TestBed.configureTestingModule({
      imports: [AnnouncementModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: AnnouncementService, useValue: { pending, dismiss } },
      ],
    });
  });

  function create() {
    const fixture = TestBed.createComponent(AnnouncementModalComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('is titled with the announcement and shows its date and body', () => {
    const element = create().nativeElement as HTMLElement;

    expect(element.textContent).toContain('Macro Deck 3 is here');
    expect(element.textContent).toContain(`Published ${new Date(announcement.publishedAt).toLocaleDateString()}`);
    expect(element.querySelector('a')?.textContent).toBe('release notes');
  });

  it('marks the announcement seen with Got it', async () => {
    const fixture = create();

    const gotIt = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes('Got it'));
    gotIt!.click();
    await new Promise(resolve => setTimeout(resolve, 400));

    expect(dismiss).toHaveBeenCalled();
  });

  it('counts closing it any other way as seen', async () => {
    const fixture = create();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 400));

    expect(dismiss).toHaveBeenCalled();
  });

  it('shows an edited text in place', () => {
    const fixture = create();

    pending.set({ ...announcement, content: 'Edited body' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Edited body');
  });
});
