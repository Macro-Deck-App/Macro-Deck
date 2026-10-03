import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { StoreRatingPromptCandidateBody } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { StoreRatingPromptService, StoreRatingSubmitResult } from '../../../services/store-rating-prompt.service';
import { StoreRatingPromptModalComponent } from './store-rating-prompt-modal.component';

describe('StoreRatingPromptModalComponent', () => {
  const candidate: StoreRatingPromptCandidateBody = {
    kind: 'Plugin',
    id: 'com.acme.hue',
    name: 'Hue Bridge',
    hasIcon: true,
    iconSha256: 'abc',
  };

  let pending: ReturnType<typeof signal<StoreRatingPromptCandidateBody | null>>;
  let markShown: jasmine.Spy;
  let dismiss: jasmine.Spy;
  let submit: jasmine.Spy<(rating: number, body: string) => Promise<StoreRatingSubmitResult>>;
  let toasts: { show: jasmine.Spy };

  beforeEach(() => {
    pending = signal<StoreRatingPromptCandidateBody | null>(candidate);
    markShown = jasmine.createSpy('markShown');
    dismiss = jasmine.createSpy('dismiss').and.callFake(() => pending.set(null));
    submit = jasmine.createSpy('submit').and.resolveTo({ ok: true });
    toasts = { show: jasmine.createSpy('show') };
    TestBed.configureTestingModule({
      imports: [StoreRatingPromptModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: StoreRatingPromptService, useValue: { pending, markShown, dismiss, submit } },
        { provide: ApiService, useValue: { onNotification: () => EMPTY, getStoreExtensionIconUrl: () => 'http://host/icon.png' } },
        { provide: ToastService, useValue: toasts },
      ],
    });
  });

  function create() {
    const fixture = TestBed.createComponent(StoreRatingPromptModalComponent);
    fixture.detectChanges();
    return fixture;
  }

  const element = (fixture: ReturnType<typeof create>) => fixture.nativeElement as HTMLElement;
  const stars = (fixture: ReturnType<typeof create>) =>
    Array.from(element(fixture).querySelectorAll<HTMLButtonElement>('[role="radio"]'));
  const button = (fixture: ReturnType<typeof create>, text: string) =>
    Array.from(element(fixture).querySelectorAll<HTMLElement>('shared-button'))
      .find(candidate => candidate.textContent?.includes(text))!;

  it('asks how the user likes the package, with its icon and five stars', () => {
    const fixture = create();

    expect(element(fixture).textContent).toContain('How do you like Hue Bridge?');
    expect(element(fixture).querySelector('img')?.getAttribute('src')).toBe('http://host/icon.png');
    expect(stars(fixture).length).toBe(5);
  });

  it('records that the prompt was shown as soon as it is on screen', () => {
    create();

    expect(markShown).toHaveBeenCalledTimes(1);
  });

  it('cannot be submitted before a star is chosen, then sends the rating directly', async () => {
    const fixture = create();
    const submitButton = button(fixture, 'Submit').querySelector('button')!;
    expect(submitButton.disabled).toBeTrue();

    stars(fixture)[3].click();
    fixture.detectChanges();
    expect(submitButton.disabled).toBeFalse();
    submitButton.click();
    await fixture.whenStable();

    expect(submit).toHaveBeenCalledOnceWith(4, '');
    expect(toasts.show).toHaveBeenCalled();
  });

  it('keeps the written review optional and sends it when the user adds one', async () => {
    const fixture = create();
    stars(fixture)[4].click();
    fixture.detectChanges();

    button(fixture, 'Write a review').querySelector('button')!.click();
    fixture.detectChanges();
    const input = element(fixture).querySelector('textarea')!;
    input.value = 'Great plugin';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    button(fixture, 'Submit').querySelector('button')!.click();
    await fixture.whenStable();

    expect(submit).toHaveBeenCalledOnceWith(5, 'Great plugin');
  });

  it('shows the reason and stays open when the rating could not be sent', async () => {
    submit.and.resolveTo({ ok: false, message: 'Ratings could not be reached.' });
    const fixture = create();
    stars(fixture)[0].click();
    fixture.detectChanges();

    button(fixture, 'Submit').querySelector('button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element(fixture).querySelector('[role="alert"]')?.textContent).toContain('Ratings could not be reached.');
    expect(dismiss).not.toHaveBeenCalled();
    expect(toasts.show).not.toHaveBeenCalled();
  });

  it('dismisses with Not now', async () => {
    const fixture = create();

    button(fixture, 'Not now').querySelector('button')!.click();
    await new Promise(resolve => setTimeout(resolve, 400));

    expect(dismiss).toHaveBeenCalledTimes(1);
  });

  it('counts closing it with Escape as not now', async () => {
    const fixture = create();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 400));

    expect(dismiss).toHaveBeenCalledTimes(1);
  });
});
