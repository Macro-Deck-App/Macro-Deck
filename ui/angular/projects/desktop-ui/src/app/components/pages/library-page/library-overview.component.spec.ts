import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AppStrings } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import {
  ICON_PACKS_CONTENT_TYPE,
  LIBRARY_CONTENT_TYPES,
  LibraryContentType,
} from '../../../domain/library-content-type';
import { bundledTranslator, provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { LibraryOverviewComponent } from './library-overview.component';

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

describe('LibraryOverviewComponent', () => {
  let fixture: ComponentFixture<LibraryOverviewComponent>;

  function pack(id: string): unknown {
    return {
      id,
      name: id,
      isDefault: false,
      isReadOnly: false,
      sourceType: 'User',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      iconCount: 0,
      ownerKind: 'User',
      canDelete: true,
    };
  }

  async function render(
    contentTypes: readonly LibraryContentType[],
    packs: unknown[] = [],
  ): Promise<void> {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconPacks', 'onNotification']);
    apiSpy.getIconPacks.and.resolveTo({ packs } as never);
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [LibraryOverviewComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        provideRouter([
          { path: 'library', children: [] },
          { path: 'library/alpha', children: [] },
          { path: 'library/beta', children: [] },
          { path: 'store', children: [] },
        ]),
        { provide: ApiService, useValue: apiSpy },
        { provide: LIBRARY_CONTENT_TYPES, useValue: contentTypes },
      ],
    });

    fixture = TestBed.createComponent(LibraryOverviewComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function cardNames(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.library-card-name'))
      .map(element => (element as HTMLElement).textContent!.trim());
  }

  function summaries(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.library-card-summary'))
      .map(element => (element as HTMLElement).textContent!.trim());
  }

  function clickableWith(text: string): HTMLElement {
    const match = Array.from(fixture.nativeElement.querySelectorAll('a, button'))
      .find(element => (element as HTMLElement).textContent!.includes(text));
    expect(match).withContext(`no clickable element containing "${text}"`).toBeTruthy();
    return match as HTMLElement;
  }

  // The point of issue #845: a second content type must be a registry entry, not a UI restructure.
  it('renders exactly the registered content types, in order', async () => {
    await render(FAKE_TYPES);

    expect(cardNames()).toEqual([
      bundledTranslator(AppStrings.Nav.Scripts),
      bundledTranslator(AppStrings.Nav.Variables),
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain(bundledTranslator(AppStrings.IconPacks.NoPacksYetMessage));
    expect(text).toContain(bundledTranslator(AppStrings.IconPacks.EmptyReadOnlyMessage));
    expect(text).not.toContain(bundledTranslator(AppStrings.Nav.IconPacks));
  });

  // The card's summary is the overview's only live data; it has to load the packs itself, because
  // arriving straight at /library means nothing else has.
  it('reports how many icon packs there are', async () => {
    await render(
      [{ ...FAKE_TYPES[0], id: ICON_PACKS_CONTENT_TYPE, route: '/library/icon-packs' }],
      [pack('a'), pack('b'), pack('c')],
    );

    expect(summaries()).toEqual([
      bundledTranslator(`${AppStrings.Library.Page.PackCount}.Other`, { count: 3 }),
    ]);
  });

  it('says one pack, not one packs, for a single pack', async () => {
    await render(
      [{ ...FAKE_TYPES[0], id: ICON_PACKS_CONTENT_TYPE, route: '/library/icon-packs' }],
      [pack('a')],
    );

    const [single] = summaries();
    expect(single).toBe(bundledTranslator(`${AppStrings.Library.Page.PackCount}.One`, { count: 1 }));
    expect(single).not.toBe(bundledTranslator(`${AppStrings.Library.Page.PackCount}.Other`, { count: 1 }));
  });

  it('shows no summary for a content type that has none', async () => {
    await render(FAKE_TYPES);

    expect(summaries()).toEqual([]);
  });

  it('opens the content type a card names', async () => {
    await render(FAKE_TYPES);

    clickableWith(bundledTranslator(AppStrings.Nav.Variables)).click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/library/beta');
  });

  it('offers a way into the store', async () => {
    await render(FAKE_TYPES);

    clickableWith(bundledTranslator(AppStrings.Integrations.Page.BrowseStoreAction)).click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url.startsWith('/store')).toBeTrue();
  });
});
