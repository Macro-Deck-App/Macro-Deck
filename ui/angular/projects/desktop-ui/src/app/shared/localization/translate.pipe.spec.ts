import { Component, provideZonelessChangeDetection, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '../transport';
import { LocalizationCultureChangedEvent } from '@macro-deck/runtime';
import { LocalizationService } from './localization.service';
import { LocalizedTextPipe } from './localized-text.pipe';
import { TranslatePipe } from './translate.pipe';

@Component({
  standalone: true,
  imports: [TranslatePipe, LocalizedTextPipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <span id="key">{{ 'macrodeck:Common.Save' | translate }}</span>
    <span id="plural">{{ 'macrodeck.app:Icons' | translate: { count: count() } }}</span>
    <span id="host">{{ hostText() | localizedText }}</span>
  `,
})
class HostComponent {
  // Signals, not plain fields: this fixture is zoneless, so only a signal write marks the view dirty
  // and lets the impure pipes re-run against the new value.
  readonly count = signal(1);
  readonly hostText = signal<unknown>({ $localized: { scope: 'macrodeck', key: 'Common.Cancel' } });
}

describe('translate pipes', () => {
  let fixture: ComponentFixture<HostComponent>;
  let notifications: Subject<LocalizationCultureChangedEvent>;
  let getLocalization: jasmine.Spy;

  const english = {
    'macrodeck:Common.Save': 'Save',
    'macrodeck:Common.Cancel': 'Cancel',
    'macrodeck.app:Icons.One': '{count} icon',
    'macrodeck.app:Icons.Other': '{count} icons',
  };

  const german = {
    'macrodeck:Common.Save': 'Speichern',
    'macrodeck:Common.Cancel': 'Abbrechen',
    'macrodeck.app:Icons.One': '{count} Symbol',
    'macrodeck.app:Icons.Other': '{count} Symbole',
  };

  beforeEach(async () => {
    localStorage.clear();
    notifications = new Subject<LocalizationCultureChangedEvent>();
    getLocalization = jasmine.createSpy('getLocalization').and.returnValue(
      Promise.resolve({
        culture: 'en',
        fallbackCulture: 'en',
        translations: english,
        availableCultures: ['en', 'de'],
      }),
    );

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            onNotification: (name: string) =>
              name === 'LocalizationCultureChangedEvent' ? notifications : new Subject(),
            getLocalization,
          },
        },
      ],
    }).compileComponents();

    await TestBed.inject(LocalizationService).loadFromHost();

    fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
  });

  // The German catalog this suite writes is read back by every later spec that builds a real
  // LocalizationService - it restores from this cache in its constructor - so leaving it behind
  // resolves someone else's English expectation to German, depending only on spec order.
  afterEach(() => localStorage.clear());

  function text(id: string): string {
    return (fixture.nativeElement as HTMLElement).querySelector(`#${id}`)!.textContent!.trim();
  }

  it('resolves a key', () => {
    expect(text('key')).toBe('Save');
  });

  it('selects a plural form from the argument', async () => {
    expect(text('plural')).toBe('1 icon');

    fixture.componentInstance.count.set(3);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(text('plural')).toBe('3 icons');
  });

  it('resolves a host-supplied localized reference', () => {
    expect(text('host')).toBe('Cancel');
  });

  it('renders host-supplied literal text as-is', async () => {
    fixture.componentInstance.hostText.set('Already final');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(text('host')).toBe('Already final');
  });

  it('re-renders every binding when the language changes', async () => {
    getLocalization.and.returnValue(
      Promise.resolve({
        culture: 'de',
        fallbackCulture: 'en',
        translations: german,
        availableCultures: ['en', 'de'],
      }),
    );

    notifications.next({} as LocalizationCultureChangedEvent);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(text('key')).toBe('Speichern');
    expect(text('plural')).toBe('1 Symbol');
    expect(text('host')).toBe('Abbrechen');
  });
});
