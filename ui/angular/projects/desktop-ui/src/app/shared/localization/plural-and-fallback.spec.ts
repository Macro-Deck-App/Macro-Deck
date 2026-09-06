import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Subject } from 'rxjs';
import { ApiService } from '../transport';
import { LocalizationService } from './localization.service';
import { AppStringsDefaults, pluralForm, Strings, StringsDefaults } from '@macro-deck/runtime';

describe('LocalizationService plural and fallback behaviour', () => {
  let localization: LocalizationService;
  let getLocalization: jasmine.Spy;

  beforeEach(() => {
    localStorage.clear();

    getLocalization = jasmine.createSpy('getLocalization');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            onNotification: () => new Subject(),
            getLocalization,
            updateLocalizationSettings: () => Promise.resolve({ success: true }),
          },
        },
      ],
    });

    localization = TestBed.inject(LocalizationService);
  });

  // Every catalog applied here lands in localStorage, and a real LocalizationService restores from
  // it in its constructor - so a non-English one left behind resolves a later spec's English
  // expectation to that language, depending only on the order the suites happened to run in.
  afterEach(() => localStorage.clear());

  async function applyCatalog(translations: Record<string, string>, culture = 'en'): Promise<void> {
    getLocalization.and.returnValue(
      Promise.resolve({ culture, fallbackCulture: 'en', translations, availableCultures: [culture] }),
    );
    await localization.loadFromHost();
  }

  describe('plural families', () => {
    const catalog = {
      'macrodeck.app:Icons.One': '{count} icon',
      'macrodeck.app:Icons.Other': '{count} icons',
    };

    it('renders the singular for one and the plural for everything else', async () => {
      await applyCatalog(catalog);

      expect(localization.translate('macrodeck.app', 'Icons', { count: 1 })).toBe('1 icon');
      expect(localization.translate('macrodeck.app', 'Icons', { count: 0 })).toBe('0 icons');
      expect(localization.translate('macrodeck.app', 'Icons', { count: 7 })).toBe('7 icons');
    });

    it('falls back to the other form when the selected one is missing', async () => {
      await applyCatalog({ 'macrodeck.app:Icons.Other': '{count} icons' });

      expect(localization.translate('macrodeck.app', 'Icons', { count: 1 })).toBe('1 icons');
    });

    it('does not resolve a family when no count was passed', async () => {
      await applyCatalog(catalog);

      expect(localization.translate('macrodeck.app', 'Icons')).toBe('[[macrodeck.app:Icons]]');
    });

    it('lets a form leave the count out of its own text', async () => {
      await applyCatalog({
        'macrodeck.app:Icons.One': 'one icon',
        'macrodeck.app:Icons.Other': '{count} icons',
      });

      expect(localization.translate('macrodeck.app', 'Icons', { count: 1 })).toBe('one icon');
      expect(localization.translate('macrodeck.app', 'Icons', { count: 4 })).toBe('4 icons');
    });

    it('picks the same form the host and the bootstrapper do', () => {
      expect(pluralForm(1)).toBe('One');
      expect(pluralForm(0)).toBe('Other');
      expect(pluralForm(2)).toBe('Other');
    });
  });

  describe('the bundled English fallback', () => {
    it('renders a key before the host has answered anything', () => {
      expect(localization.translateKey(Strings.Common.Save)).toBe('Save');
    });

    it('keeps rendering English for a key the host catalog does not carry', async () => {
      await applyCatalog({ 'macrodeck:Common.Cancel': 'Abbrechen' }, 'de');

      expect(localization.translateKey(Strings.Common.Cancel)).toBe('Abbrechen');
      expect(localization.translateKey(Strings.Common.Save)).toBe('Save');
    });

    it('resolves a localized reference passed as an argument', async () => {
      await applyCatalog({
        'macrodeck:Validation.Required': '{field} is required',
        'macrodeck.app:Network.Port': 'Port',
      });

      const rendered = localization.translate('macrodeck', 'Validation.Required', {
        field: { $localized: { scope: 'macrodeck.app', key: 'Network.Port' } },
      });

      expect(rendered).toBe('Port is required');
    });

    it('still names a key that exists in no catalog at all', () => {
      expect(localization.translate('macrodeck', 'Nothing.Here')).toBe('[[macrodeck:Nothing.Here]]');
    });

    it('ships a default for every generated key', () => {
      for (const defaults of [StringsDefaults, AppStringsDefaults]) {
        for (const [key, text] of Object.entries(defaults)) {
          expect(text).withContext(key).toBeTruthy();
        }
      }
    });
  });
});
