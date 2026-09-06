import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AppStrings } from '@macro-deck/runtime';
import { LIBRARY_CONTENT_TYPES, LibraryContentType } from '../../domain/library-content-type';
import { bundledTranslator, provideLocalizationTesting } from '../../../testing/localization-test-support';
import { LIBRARY_OVERVIEW_ID, LibraryContentTypeSwitcherComponent } from './library-content-type-switcher.component';

const FAKE_TYPES: readonly LibraryContentType[] = [
  {
    id: 'alpha',
    route: '/library/alpha',
    labelKey: AppStrings.Nav.Scripts,
    descriptionKey: AppStrings.IconPacks.NoPacksYetMessage,
    icon: 'list-play',
  },
  {
    id: 'beta',
    route: '/library/beta',
    labelKey: AppStrings.Nav.Variables,
    descriptionKey: AppStrings.IconPacks.EmptyReadOnlyMessage,
    icon: 'braces-x',
  },
];

describe('LibraryContentTypeSwitcherComponent', () => {
  let fixture: ComponentFixture<LibraryContentTypeSwitcherComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LibraryContentTypeSwitcherComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        provideRouter([
          { path: 'library', children: [] },
          { path: 'library/alpha', children: [] },
          { path: 'library/beta', children: [] },
        ]),
        { provide: LIBRARY_CONTENT_TYPES, useValue: FAKE_TYPES },
      ],
    });

    fixture = TestBed.createComponent(LibraryContentTypeSwitcherComponent);
  });

  function show(activeId: string): void {
    fixture.componentRef.setInput('activeId', activeId);
    fixture.detectChanges();
  }

  function optionLabels(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.seg-option'))
      .map(element => (element as HTMLElement).textContent!.trim());
  }

  function pick(label: string): void {
    const option = Array.from(fixture.nativeElement.querySelectorAll('.seg-option'))
      .find(element => (element as HTMLElement).textContent!.trim() === label);
    expect(option).withContext(`no option labelled "${label}"`).toBeTruthy();
    (option as HTMLElement).click();
  }

  it('offers the overview and every registered content type', () => {
    show(LIBRARY_OVERVIEW_ID);

    expect(optionLabels()).toEqual([
      bundledTranslator(AppStrings.Library.Page.OverviewOption),
      bundledTranslator(AppStrings.Nav.Scripts),
      bundledTranslator(AppStrings.Nav.Variables),
    ]);
  });

  it('opens the content type that was picked', async () => {
    show('alpha');

    pick(bundledTranslator(AppStrings.Nav.Variables));
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/library/beta');
  });

  it('returns to the overview', async () => {
    show('alpha');

    pick(bundledTranslator(AppStrings.Library.Page.OverviewOption));
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/library');
  });
});
