import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { WidgetType } from '@macro-deck/runtime';
import {
  UiSessionHandle,
  UiSessionOpenRequest, UiSessionOpenRequestSource,
  UiSessionService,
  UiTreeWidgetComponent,
  WidgetRegistryService,
  WidgetTypeCatalogService,
  WidgetTypeFavoritesService,
  WidgetTypeInfo,
} from '@shared';
import { WIDGET_TYPE_SELECTOR_VIEW_KEY, WidgetTypeSelectorComponent } from './widget-type-selector.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

const GAUGE_TYPE = 'com.example.gauges::gauge';

describe('WidgetTypeSelectorComponent', () => {
  let opens: UiSessionOpenRequest[];
  let fixture: ComponentFixture<WidgetTypeSelectorComponent>;
  let rejectedTypes: Set<string>;
  let favoriteIds: WritableSignal<ReadonlySet<string>>;
  let toggled: string[];
  let rollBackAfterMs: number | null;

  function catalogEntry(overrides: Partial<WidgetTypeInfo> & { id: string }): WidgetTypeInfo {
    return {
      providerId: '',
      isBuiltIn: true,
      defaultData: {},
      supportsConfigUi: false,
      configUiModelVersion: 0,
      ...overrides,
    };
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function visibleCards(): HTMLElement[] {
    return Array.from(root().querySelectorAll<HTMLElement>('.widget-types .type-card'))
      .filter(card => !card.hidden);
  }

  function cardNames(): string[] {
    return visibleCards().map(card => card.querySelector('.type-name')?.textContent?.trim() ?? '');
  }

  function rowNames(): string[] {
    return Array.from(root().querySelectorAll('.widget-types.list .type-card:not([hidden]) .type-name'))
      .map(name => name.textContent?.trim() ?? '');
  }

  function cardNamed(name: string): HTMLElement {
    const card = visibleCards().find(candidate => candidate.querySelector('.type-name')?.textContent?.trim() === name);
    if (!card) throw new Error(`no visible card named ${name}`);
    return card;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function search(text: string): Promise<void> {
    const input = root().querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function switchView(label: 'Tiles' | 'List'): Promise<void> {
    root().querySelector<HTMLButtonElement>(`.view-toggle button[aria-label="${label}"]`)!.click();
    await settle();
  }

  async function select(card: HTMLElement): Promise<string[]> {
    const selected: string[] = [];
    fixture.componentInstance.typeSelected.subscribe(type => selected.push(type));
    card.querySelector<HTMLButtonElement>('.type-select')!.click();
    await fixture.whenStable();
    // The modal dismisses with an animation before it emits.
    await new Promise(resolve => setTimeout(resolve, 400));
    return selected;
  }

  async function createFixture(catalog: WidgetTypeInfo[], options: { declined?: string[]; favorites?: string[] } = {}): Promise<void> {
    opens = [];
    toggled = [];
    rollBackAfterMs = null;
    rejectedTypes = new Set(options.declined ?? []);
    favoriteIds = signal(new Set(options.favorites ?? []));

    const uiSessions = {
      open: (source: UiSessionOpenRequestSource): UiSessionHandle => {
        const request = typeof source === 'function' ? source() : source;
        opens.push(request);
        const declined = rejectedTypes.has((request as { widgetType?: string }).widgetType ?? '');
        return {
          root: () => null,
          revision: () => 0,
          rejection: () => declined ? { code: 'declined', message: 'no preview' } : null,
          send: () => undefined,
          close: () => undefined,
        } as unknown as UiSessionHandle;
      },
    };

    const typesSignal: WritableSignal<WidgetTypeInfo[]> = signal(catalog);
    const catalogStub = { types: typesSignal, load: () => Promise.resolve() };
    const favoritesStub = {
      ids: favoriteIds.asReadonly(),
      isFavorite: (id: string) => favoriteIds().has(id),
      toggle: (id: string) => {
        toggled.push(id);
        const before = favoriteIds();
        const next = new Set(before);
        if (next.has(id)) next.delete(id); else next.add(id);
        favoriteIds.set(next);
        const delay = rollBackAfterMs;
        if (delay === null) return Promise.resolve();
        return new Promise<void>(resolve => setTimeout(() => {
          favoriteIds.set(before);
          resolve();
        }, delay));
      },
    };

    TestBed.configureTestingModule({
      imports: [WidgetTypeSelectorComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: UiSessionService, useValue: uiSessions },
        { provide: WidgetTypeCatalogService, useValue: catalogStub },
        { provide: WidgetTypeFavoritesService, useValue: favoritesStub },
      ],
    });

    // Only the two built-in types carry a local Angular registration; the picker draws its cards from
    // the host catalogue, not from this, so a plugin type with no local registration still gets a card.
    const registry = TestBed.inject(WidgetRegistryService);
    registry.register({ type: WidgetType.ActionButton, component: UiTreeWidgetComponent });
    registry.register({ type: WidgetType.Weather, component: UiTreeWidgetComponent });

    fixture = TestBed.createComponent(WidgetTypeSelectorComponent);
    await settle();
  }

  const builtInCatalog = [
    catalogEntry({ id: WidgetType.Weather, name: 'Weather', description: 'Shows the forecast' }),
    catalogEntry({ id: WidgetType.ActionButton, name: 'Action Button', description: 'Fires an action' }),
  ];
  const pluginCatalog = [
    ...builtInCatalog,
    catalogEntry({
      id: GAUGE_TYPE,
      providerId: 'com.example.gauges',
      isBuiltIn: false,
      isPluginProvided: true,
      providerName: 'Gauge Pack',
      name: 'Gauge',
      description: 'A dial',
    }),
  ];
  const integrationCatalog = [
    ...builtInCatalog,
    catalogEntry({
      id: 'app.macro-deck.twitch::chat',
      providerId: 'app.macro-deck.twitch',
      isBuiltIn: false,
      isPluginProvided: false,
      providerName: 'Twitch',
      name: 'Chat',
      description: 'Shows the chat',
    }),
  ];

  beforeEach(() => localStorage.removeItem(WIDGET_TYPE_SELECTOR_VIEW_KEY));
  afterEach(() => localStorage.removeItem(WIDGET_TYPE_SELECTOR_VIEW_KEY));

  describe('with only built-in types on offer', () => {
    beforeEach(() => createFixture(builtInCatalog));

    it('offers one card per catalogue entry, in alphabetical order rather than catalogue order', () => {
      expect(cardNames()).toEqual(['Action Button', 'Weather']);
    });

    it('shows each widget type drawn from its own sample rather than from a generic icon', () => {
      const previews = fixture.debugElement.queryAll(By.directive(UiTreeWidgetComponent))
        .map(element => element.componentInstance as UiTreeWidgetComponent);

      expect(previews.map(preview => preview.widgetType).sort())
        .toEqual([WidgetType.ActionButton, WidgetType.Weather].sort());
      expect(previews.every(preview => preview.sample)).toBeTrue();
      expect(opens.map(request => (request as { sample?: boolean }).sample)).toEqual([true, true]);
    });

    it('selects the type when the card itself is clicked, with nothing else to aim for', async () => {
      expect(await select(cardNamed('Weather'))).toEqual([WidgetType.Weather]);
      expect(root().querySelector('.icon-chevron-right')).toBeNull();
    });

    it('shows no integration badge on a built-in type', () => {
      expect(root().querySelector('.type-provider')).toBeNull();
    });
  });

  describe('with a plugin-provided type on the catalogue', () => {
    beforeEach(() => createFixture(pluginCatalog));

    it('renders a card for the plugin type, named and described from the catalogue - not the raw id', () => {
      const gauge = cardNamed('Gauge');
      expect(gauge.querySelector('.type-description')?.textContent?.trim()).toBe('A dial');
      expect(gauge.querySelector('.type-select')?.getAttribute('aria-label')).toBe('Gauge');
    });

    it('labels the plugin type with the name of the integration that provides it', () => {
      expect(cardNamed('Gauge').querySelector('.type-provider')?.textContent?.trim()).toBe('Gauge Pack');
      expect(cardNamed('Weather').querySelector('.type-provider')).toBeNull();
    });

    it('emits typeSelected with the qualified plugin id verbatim when its card is clicked', async () => {
      expect(await select(cardNamed('Gauge'))).toEqual([GAUGE_TYPE]);
    });

    it('narrows the cards to what the search matches, by name, description or integration', async () => {
      await search('weath');
      expect(cardNames()).toEqual(['Weather']);

      await search('FIRES');
      expect(cardNames()).toEqual(['Action Button']);

      await search('gauge pack');
      expect(cardNames()).toEqual(['Gauge']);
    });

    it('takes hidden cards out of reach of the keyboard', async () => {
      await search('weath');

      const hidden = Array.from(root().querySelectorAll<HTMLElement>('.widget-types .type-card'))
        .filter(card => card.hidden);
      expect(hidden.length).toBe(2);
      expect(hidden.every(card => getComputedStyle(card).display === 'none')).toBeTrue();
    });

    it('says so when nothing matches the search', async () => {
      expect(root().querySelector('.no-matches')).toBeNull();

      await search('zzz');

      expect(cardNames()).toEqual([]);
      expect(root().querySelector('.no-matches')?.textContent?.trim()).toBe('No widgets match your search');
    });

    it('keeps the live previews open while searching and switching views', async () => {
      const opened = opens.length;

      await search('weath');
      await search('');
      await switchView('List');
      await switchView('Tiles');

      expect(opens.length).toBe(opened);
    });

    it('marks a type as favorite from its star without selecting it', async () => {
      const selected: string[] = [];
      fixture.componentInstance.typeSelected.subscribe(type => selected.push(type));

      const star = cardNamed('Weather').querySelector<HTMLButtonElement>('.type-favorite')!;
      expect(star.getAttribute('aria-label')).toBe('Add Weather to favorites');
      star.click();
      await settle();
      await new Promise(resolve => setTimeout(resolve, 400));

      expect(toggled).toEqual([WidgetType.Weather]);
      expect(selected).toEqual([]);
      expect(cardNamed('Weather').querySelector('.type-favorite')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('keeps keyboard focus on the star when starring moves the card to the front', async () => {
      const star = cardNamed('Weather').querySelector<HTMLButtonElement>('.type-favorite')!;
      star.focus();
      star.click();
      await settle();

      expect(cardNames()[0]).toBe('Weather');
      expect(document.activeElement).toBe(cardNamed('Weather').querySelector('.type-favorite'));
    });

    it('keeps keyboard focus on the star when a refused favorite moves the card back', async () => {
      rollBackAfterMs = 50;
      const star = cardNamed('Weather').querySelector<HTMLButtonElement>('.type-favorite')!;
      star.focus();
      star.click();
      await settle();
      await new Promise(resolve => setTimeout(resolve, 100));
      await settle();

      expect(cardNames()).toEqual(['Action Button', 'Gauge', 'Weather']);
      expect(document.activeElement).toBe(cardNamed('Weather').querySelector('.type-favorite'));
    });

    it('does nothing once the dialog closed before the host answered', async () => {
      rollBackAfterMs = 50;
      const errors: unknown[] = [];
      const onRejection = (event: PromiseRejectionEvent) => errors.push(event.reason);
      window.addEventListener('unhandledrejection', onRejection);

      const star = cardNamed('Weather').querySelector<HTMLButtonElement>('.type-favorite')!;
      star.focus();
      star.click();
      await settle();
      fixture.destroy();
      (document.activeElement as HTMLElement | null)?.blur();
      await new Promise(resolve => setTimeout(resolve, 100));
      window.removeEventListener('unhandledrejection', onRejection);

      expect(errors).toEqual([]);
    });

    it('reaches the star with Tab right after the card it belongs to', () => {
      const card = cardNamed('Gauge');
      const buttons = Array.from(card.querySelectorAll('button'));
      expect(buttons.map(button => button.className.split(' ')[0])).toEqual(['type-select', 'type-favorite']);
    });

    it('offers a list view with the same order, badges, previews and selection', async () => {
      await switchView('List');

      expect(rowNames()).toEqual(['Action Button', 'Gauge', 'Weather']);
      expect(cardNamed('Gauge').querySelector('.type-provider')?.textContent?.trim()).toBe('Gauge Pack');
      expect(cardNamed('Weather').querySelector('.type-preview shared-ui-tree-widget')).not.toBeNull();
      expect(await select(cardNamed('Gauge'))).toEqual([GAUGE_TYPE]);
    });

    it('remembers the list view for the next time the selector opens', async () => {
      await switchView('List');
      fixture.destroy();

      fixture = TestBed.createComponent(WidgetTypeSelectorComponent);
      await settle();

      expect(rowNames()).toEqual(['Action Button', 'Gauge', 'Weather']);
    });
  });

  describe('with a type one of the host\'s own integrations provides', () => {
    beforeEach(() => createFixture(integrationCatalog));

    it('shows no integration badge, because only plugin-provided types carry one', () => {
      expect(cardNamed('Chat').querySelector('.type-provider')).toBeNull();
    });

    it('still finds the type by the name of the integration that provides it', async () => {
      await search('twitch');
      expect(cardNames()).toEqual(['Chat']);
    });
  });

  describe('with favorites', () => {
    beforeEach(() => createFixture(pluginCatalog, { favorites: [WidgetType.Weather, GAUGE_TYPE] }));

    it('puts favorites first, each group in alphabetical order', () => {
      expect(cardNames()).toEqual(['Gauge', 'Weather', 'Action Button']);
    });

    it('moves a type to the front when it is starred', async () => {
      cardNamed('Action Button').querySelector<HTMLButtonElement>('.type-favorite')!.click();
      await settle();

      expect(cardNames()).toEqual(['Action Button', 'Gauge', 'Weather']);
    });
  });

  describe('when the plugin type declines its sample session', () => {
    beforeEach(() => createFixture(pluginCatalog, { declined: [GAUGE_TYPE] }));

    it('still shows the card, named from the catalogue, with a placeholder instead of a blank sample', () => {
      expect(cardNamed('Gauge').querySelector('.type-preview-placeholder')).not.toBeNull();
    });

    it('still emits typeSelected for the declined type when its card is clicked', async () => {
      expect(await select(cardNamed('Gauge'))).toEqual([GAUGE_TYPE]);
    });
  });
});
