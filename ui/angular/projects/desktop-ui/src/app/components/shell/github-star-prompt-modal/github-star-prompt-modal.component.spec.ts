import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { GitHubStarPromptService } from '../../../services/github-star-prompt.service';
import { ExternalLinkService } from '../../../services/external-link.service';
import { GitHubStarPromptModalComponent } from './github-star-prompt-modal.component';

describe('GitHubStarPromptModalComponent', () => {
  let pending: ReturnType<typeof signal<boolean>>;
  let markShown: jasmine.Spy;
  let dismiss: jasmine.Spy;
  let open: jasmine.Spy;

  beforeEach(() => {
    pending = signal(true);
    markShown = jasmine.createSpy('markShown');
    dismiss = jasmine.createSpy('dismiss').and.callFake(() => pending.set(false));
    open = jasmine.createSpy('open');
    TestBed.configureTestingModule({
      imports: [GitHubStarPromptModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: GitHubStarPromptService, useValue: { pending, markShown, dismiss } },
        { provide: ExternalLinkService, useValue: { open } },
        { provide: ApiService, useValue: { onNotification: () => EMPTY } },
      ],
    });
  });

  function create() {
    const fixture = TestBed.createComponent(GitHubStarPromptModalComponent);
    fixture.detectChanges();
    return fixture;
  }

  const element = (fixture: ReturnType<typeof create>) => fixture.nativeElement as HTMLElement;
  const click = (fixture: ReturnType<typeof create>, text: string) => {
    Array.from(element(fixture).querySelectorAll<HTMLElement>('shared-button'))
      .find(candidate => candidate.textContent?.includes(text))!
      .querySelector('button')!
      .click();
    fixture.detectChanges();
  };
  const closed = () => new Promise(resolve => setTimeout(resolve, 400));

  it('asks whether the user likes Macro Deck and records it as shown right away', () => {
    const fixture = create();

    expect(element(fixture).textContent).toContain('Do you like Macro Deck?');
    expect(markShown).toHaveBeenCalledTimes(1);
  });

  it('asks a user who likes it for a star and opens the repository', async () => {
    const fixture = create();

    click(fixture, 'Yes!');
    expect(element(fixture).textContent).toContain('A star on GitHub');
    click(fixture, 'Star on GitHub');
    await closed();

    expect(open).toHaveBeenCalledOnceWith('https://github.com/Macro-Deck-App/Macro-Deck');
    expect(dismiss).toHaveBeenCalledTimes(1);
  });

  it('lets a user who likes it decline the star without opening anything', async () => {
    const fixture = create();

    click(fixture, 'Yes!');
    click(fixture, 'No thanks');
    await closed();

    expect(open).not.toHaveBeenCalled();
    expect(dismiss).toHaveBeenCalledTimes(1);
  });

  it('asks a user who does not like it for feedback and opens the issues', async () => {
    const fixture = create();

    click(fixture, 'Not really');
    expect(element(fixture).textContent).not.toContain('A star on GitHub');
    click(fixture, 'Give feedback');
    await closed();

    expect(open).toHaveBeenCalledOnceWith('https://github.com/Macro-Deck-App/Macro-Deck/issues');
    expect(dismiss).toHaveBeenCalledTimes(1);
  });

  it('counts closing it with Escape as an answer', async () => {
    const fixture = create();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await closed();

    expect(open).not.toHaveBeenCalled();
    expect(dismiss).toHaveBeenCalledTimes(1);
  });
});
