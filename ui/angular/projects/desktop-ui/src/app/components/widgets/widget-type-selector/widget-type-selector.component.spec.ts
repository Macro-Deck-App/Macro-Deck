import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { WidgetType } from '@macro-deck/runtime';
import {
  UiSessionHandle,
  UiSessionOpenRequest,
  UiSessionService,
  UiTreeWidgetComponent,
  WidgetRegistryService,
  WidgetTypeCatalogService,
  WidgetTypeInfo,
} from '@shared';
import { WidgetTypeSelectorComponent } from './widget-type-selector.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

const GAUGE_TYPE = 'com.example.gauges::gauge';

describe('WidgetTypeSelectorComponent', () => {
  let opens: UiSessionOpenRequest[];
  let fixture: ComponentFixture<WidgetTypeSelectorComponent>;
  let rejectedTypes: Set<string>;

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

  function cards(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.type-card'));
  }

  async function createFixture(catalog: WidgetTypeInfo[], declined: string[] = []): Promise<void> {
    opens = [];
    rejectedTypes = new Set(declined);

    const uiSessions = {
      open: (request: UiSessionOpenRequest): UiSessionHandle => {
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

    TestBed.configureTestingModule({
      imports: [WidgetTypeSelectorComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: UiSessionService, useValue: uiSessions },
        { provide: WidgetTypeCatalogService, useValue: catalogStub },
      ],
    });

    // Only the two built-in types carry a local Angular registration; the picker draws its cards from
    // the host catalogue, not from this, so a plugin type with no local registration still gets a card.
    const registry = TestBed.inject(WidgetRegistryService);
    registry.register({ type: WidgetType.ActionButton, component: UiTreeWidgetComponent });
    registry.register({ type: WidgetType.Weather, component: UiTreeWidgetComponent });

    fixture = TestBed.createComponent(WidgetTypeSelectorComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const builtInCatalog = [
    catalogEntry({ id: WidgetType.ActionButton, name: 'Action Button', description: 'Fires an action' }),
    catalogEntry({ id: WidgetType.Weather, name: 'Weather', description: 'Shows the forecast' }),
  ];
  const pluginCatalog = [
    ...builtInCatalog,
    catalogEntry({ id: GAUGE_TYPE, providerId: 'com.example.gauges', name: 'Gauge', description: 'A dial' }),
  ];

  describe('with only built-in types on offer', () => {
    beforeEach(() => createFixture(builtInCatalog));

    it('offers one card per catalogue entry', () => {
      expect(cards().length).toBe(2);
    });

    it('shows each widget type drawn from its own sample rather than from a generic icon', () => {
      const previews = fixture.debugElement.queryAll(By.directive(UiTreeWidgetComponent))
        .map(element => element.componentInstance as UiTreeWidgetComponent);

      expect(previews.map(preview => preview.widgetType))
        .toEqual([WidgetType.ActionButton, WidgetType.Weather]);
      expect(previews.every(preview => preview.sample)).toBeTrue();
      expect(opens.map(request => (request as { sample?: boolean }).sample)).toEqual([true, true]);
    });

    it('selects the type when the card itself is clicked, with nothing else to aim for', async () => {
      const selected: string[] = [];
      fixture.componentInstance.typeSelected.subscribe(type => selected.push(type));

      cards()[1].click();
      await fixture.whenStable();
      // The modal dismisses with an animation before it emits.
      await new Promise(resolve => setTimeout(resolve, 400));

      expect(selected).toEqual([WidgetType.Weather]);
      expect((fixture.nativeElement as HTMLElement).querySelector('.icon-chevron-right')).toBeNull();
    });
  });

  describe('with a plugin-provided type on the catalogue', () => {
    beforeEach(() => createFixture(pluginCatalog));

    it('renders a third card for the plugin type, named and described from the catalogue - not the raw id', () => {
      const rendered = cards();
      expect(rendered.length).toBe(3);

      const last = rendered[2];
      expect(last.querySelector('.type-name')?.textContent?.trim()).toBe('Gauge');
      expect(last.querySelector('.type-description')?.textContent?.trim()).toBe('A dial');
      expect(last.getAttribute('aria-label')).toBe('Gauge');
    });

    it('emits typeSelected with the qualified plugin id verbatim when its card is clicked', async () => {
      const selected: string[] = [];
      fixture.componentInstance.typeSelected.subscribe(type => selected.push(type));

      cards()[2].click();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, 400));

      expect(selected).toEqual([GAUGE_TYPE]);
    });
  });

  describe('when the plugin type declines its sample session', () => {
    beforeEach(() => createFixture(pluginCatalog, [GAUGE_TYPE]));

    it('still shows the card, named from the catalogue, with a placeholder instead of a blank sample', () => {
      const rendered = cards();
      expect(rendered.length).toBe(3);

      const last = rendered[2];
      expect(last.querySelector('.type-name')?.textContent?.trim()).toBe('Gauge');
      expect(last.querySelector('.type-preview-placeholder')).not.toBeNull();
    });

    it('still emits typeSelected for the declined type when its card is clicked', async () => {
      const selected: string[] = [];
      fixture.componentInstance.typeSelected.subscribe(type => selected.push(type));

      cards()[2].click();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, 400));

      expect(selected).toEqual([GAUGE_TYPE]);
    });
  });
});
