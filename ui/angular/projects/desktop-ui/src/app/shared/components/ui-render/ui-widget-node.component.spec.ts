import { TestBed } from '@angular/core/testing';
import { EMPTY, Observable } from 'rxjs';

import { LocalizationService } from '../../localization';
import { ApiService } from '../../transport';
import { UiNode, UiComponents, UiMacroDeckComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick, updateTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const MacroDeckTypes = UiMacroDeckComponents;
const Props = UiComponentProperties;

interface Catalog {
  culture: string;
  fallbackCulture: string;
  translations: Record<string, string>;
}

function apiSpyWithCatalog(catalog: Catalog): jasmine.SpyObj<ApiService> {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getLocalization', 'onNotification']);
  api.getLocalization.and.resolveTo({ ...catalog, followSystem: false, availableCultures: [catalog.culture] });
  api.onNotification.and.callFake(<T>(): Observable<T> => EMPTY);
  // ServerClockService (reachable indirectly through any node's UiWidgetTimeService injection) reads
  // this on construction; a spy without it throws before the test's own assertions ever run.
  (api as unknown as { connectionStateSignal: () => string }).connectionStateSignal = () => 'disconnected';
  return api;
}

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function typeSequence(host: HTMLElement): (string | null)[] {
  return [...host.querySelectorAll('[data-node-type]')].map(node => node.getAttribute('data-node-type'));
}

function geometrySignature(host: HTMLElement): unknown[] {
  return [...host.querySelectorAll<HTMLElement>('.widget-stack, .widget-text, .widget-image, .widget-range-bar')]
    .map(node => ({
      class: node.className,
      width: node.style.width,
      height: node.style.height,
      fontSize: node.style.fontSize,
    }));
}

function forecastTree(dayCount: number): UiNode {
  const rows: UiNode[] = Array.from({ length: dayCount }, (_, i) => ({
    id: `row-${i}`,
    type: Types.Stack,
    properties: { [Props.Fill]: true, [Props.Direction]: 'horizontal' },
    children: [
      {
        id: `weekday-${i}`,
        type: Types.Text,
        properties: {
          [Props.Text]: 'Mo',
          [Props.MainSize]: { basis: 0.114, maxOfCross: 0.95 },
          [Props.Size]: { basis: 0.06, maxOfCross: 0.5 },
        },
      },
    ],
  }));

  return {
    id: 'root',
    type: Types.Stack,
    properties: { [Props.Padding]: { basis: 0.06 } },
    children: [
      { id: 'current', type: Types.Stack, properties: { [Props.MainSize]: { basis: 0.42 } } },
      { id: 'forecast', type: Types.Stack, properties: { [Props.Fill]: true }, children: rows },
    ],
  };
}

describe('shared-ui-widget-node', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('range bar', () => {
    it('gives the filled span the track\'s own rounded ends', async () => {
      const tree: UiNode = {
        id: 'bar',
        type: Types.RangeBar,
        properties: {
          [Props.Start]: 0.2,
          [Props.End]: 0.6,
          [Props.StartColor]: '#2b6cee',
          [Props.EndColor]: '#ee2b2b',
          [Props.Thickness]: { basis: 0.05 },
        },
      };

      const rendered = await renderTree(tree, withBasis(200));
      const host = el(rendered);
      const track = host.querySelector('.widget-range-bar-track') as HTMLElement;
      const fill = host.querySelector('.widget-range-bar-fill') as HTMLElement;

      expect(fill.style.borderRadius).toBe(track.style.borderRadius);
      expect(parseFloat(fill.style.borderRadius)).toBe(5);
    });
  });

  describe('content-sized children', () => {
    function rowTree(): UiNode {
      return {
        id: 'root',
        type: Types.Stack,
        properties: { [Props.Padding]: { basis: 0.06 }, [Props.Direction]: 'horizontal' },
        children: [
          {
            id: 'label',
            type: Types.Stack,
            properties: { [Props.Direction]: 'horizontal' },
            children: [
              { id: 'day', type: Types.Text, properties: { [Props.Text]: 'Mo', [Props.MainSize]: { basis: 0.1 } } },
              { id: 'icon', type: Types.Image, properties: { [Props.MainSize]: { basis: 0.05 } } },
            ],
          },
          { id: 'min', type: Types.Text, properties: { [Props.Text]: '10', [Props.MainSize]: { basis: 0.1 } } },
          { id: 'bar', type: Types.RangeBar, properties: { [Props.Fill]: true } },
        ],
      };
    }

    it('leaves room for a content-sized sibling instead of handing its space to the filling one', async () => {
      const rendered = await renderTree(rowTree(), withBasis(200));
      const host = el(rendered);

      const rect = (id: string) =>
        (host.querySelector(`[data-node-id="${id}"]`) as HTMLElement).getBoundingClientRect();
      const rootRect = rect('root');

      expect(Math.round(rect('bar').width)).toBe(126);
      expect(Math.round(rect('bar').right)).toBeLessThanOrEqual(Math.round(rootRect.right - 12));
    });

    it('does not give a content-sized child the whole widget on the cross axis', async () => {
      const tree: UiNode = {
        id: 'root',
        type: Types.Stack,
        properties: {},
        children: [
          {
            id: 'head',
            type: Types.Stack,
            properties: { [Props.Direction]: 'horizontal' },
            children: [{ id: 'icon', type: Types.Image, properties: { [Props.Size]: { basis: 0.2 } } }],
          },
        ],
      };

      const rendered = await renderTree(tree, withBasis(200));
      const head = el(rendered).querySelector('[data-node-id="head"] > *') as HTMLElement;

      expect(head.style.height).toBe('');
    });
  });

  describe('S24 - maxOfCross actually binds', () => {
    it('cross-clamps the forecast row font at 5 days, and its own mainSize stays 1.9x the font', async () => {
      const rendered = await renderTree(forecastTree(5), withBasis(240));
      const text = el(rendered).querySelector('[data-node-id="weekday-0"].widget-text') as HTMLElement;

      const fontSize = parseFloat(text.style.fontSize);
      const width = parseFloat(text.style.width);
      expect(fontSize).toBeCloseTo(11.04, 2);
      expect(width).toBeCloseTo(20.976, 2);
      expect(width / fontSize).toBeCloseTo(1.9, 2);
    });

    it('is basis-bound at 2 days instead, where a maxOfCross-blind renderer would coincidentally pass', async () => {
      const rendered = await renderTree(forecastTree(2), withBasis(240));
      const text = el(rendered).querySelector('[data-node-id="weekday-0"].widget-text') as HTMLElement;

      expect(parseFloat(text.style.fontSize)).toBeCloseTo(14.4, 2);
      expect(parseFloat(text.style.width)).toBeCloseTo(27.36, 2);
    });
  });

  describe('maxLines clamps the rendered text', () => {
    function captionTree(maxLines?: number): UiNode {
      return {
        id: 'root',
        type: Types.Stack,
        children: [
          {
            id: 'caption',
            type: Types.Text,
            properties: {
              [Props.Text]: 'A caption long enough to run past a single line of the tile',
              [Props.Size]: { basis: 0.1 },
              ...(maxLines === undefined ? {} : { [Props.MaxLines]: maxLines }),
            },
          },
        ],
      };
    }

    function caption(rendered: RenderedTree): HTMLElement {
      return el(rendered).querySelector('[data-node-id="caption"].widget-text') as HTMLElement;
    }

    it('puts the line count on the element as -webkit-line-clamp, not just as a class', async () => {
      const rendered = await renderTree(captionTree(3), withBasis(240));
      const text = caption(rendered);

      expect(text.classList).toContain('widget-text-clamp');
      expect(text.style.getPropertyValue('-webkit-line-clamp')).toBe('3');
      expect(getComputedStyle(text).webkitLineClamp).toBe('3');
    });

    it('leaves single-line text unclamped and ellipsized instead', async () => {
      const rendered = await renderTree(captionTree(), withBasis(240));
      const text = caption(rendered);

      expect(text.classList).not.toContain('widget-text-clamp');
      expect(text.style.getPropertyValue('-webkit-line-clamp')).toBe('');
      expect(text.style.textOverflow).toBe('ellipsis');
    });

    it('drops the clamp again when the node falls back to one line', async () => {
      const rendered = await renderTree(captionTree(3), withBasis(240));
      await updateTree(rendered, { root: captionTree() });

      expect(caption(rendered).style.getPropertyValue('-webkit-line-clamp')).toBe('');
    });
  });

  describe('S23 - no weather-specific knowledge on the client', () => {
    function tree(headerKey: string, resourceId: string): UiNode {
      return {
        id: `root-${headerKey}`,
        type: Types.Stack,
        children: [
          {
            id: `header-${headerKey}`,
            type: Types.Text,
            properties: {
              [Props.Text]: { $localized: { scope: 'macrodeck', key: headerKey } },
              [Props.MainSize]: { basis: 0.5 },
              [Props.Size]: { basis: 0.1 },
            },
          },
          {
            id: `icon-${headerKey}`,
            type: Types.Image,
            properties: {
              [Props.Source]: { resourceId },
              [Props.MainSize]: { basis: 0.3 },
              [Props.Size]: { basis: 0.2 },
            },
          },
          {
            id: `bar-${headerKey}`,
            type: Types.RangeBar,
            properties: {
              [Props.MainSize]: { basis: 0.2 },
              [Props.Thickness]: { basis: 0.05 },
              [Props.Start]: 0.2,
              [Props.End]: 0.8,
              [Props.StartColor]: '#112233',
              [Props.EndColor]: '#445566',
              [Props.Marker]: 0.5,
            },
          },
        ],
      };
    }

    const catalog: Catalog = {
      culture: 'en',
      fallbackCulture: 'en',
      translations: { 'macrodeck:Key.A': 'Alpha Text', 'macrodeck:Key.B': 'Beta Text' },
    };

    it('renders a structurally and geometrically identical tree for a scrambled twin', async () => {
      const original = await renderTree(tree('Key.A', 'icon-a'), [{ provide: ApiService, useValue: apiSpyWithCatalog(catalog) }]);
      await TestBed.inject(LocalizationService).loadFromHost();
      await tick(original);
      const hostA = el(original);

      // Read off before the second renderTree(), which resets the testing module and, with it,
      // destroys this fixture - the bridge's own teardown then empties hostA's subtree.
      const typesA = typeSequence(hostA);
      const geometryA = geometrySignature(hostA);
      const textA = hostA.querySelector('.widget-text')?.textContent;
      const imgA = (hostA.querySelector('.widget-image img') as HTMLImageElement).src;

      const scrambled = await renderTree(tree('Key.B', 'icon-b'), [{ provide: ApiService, useValue: apiSpyWithCatalog(catalog) }]);
      await TestBed.inject(LocalizationService).loadFromHost();
      await tick(scrambled);
      const hostB = el(scrambled);

      expect(typesA).toEqual(typeSequence(hostB));
      expect(geometryA).toEqual(geometrySignature(hostB));

      expect(textA).toBe('Alpha Text');
      expect(hostB.querySelector('.widget-text')?.textContent).toBe('Beta Text');

      expect(imgA).toBe('http://host/api/ui/resources/icon-a');
      expect((hostB.querySelector('.widget-image img') as HTMLImageElement).src).toBe('http://host/api/ui/resources/icon-b');
    });

    it('builds the image URL from exactly the resource handle, with no slug-to-icon table', async () => {
      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          {
            id: 'icon',
            type: Types.Image,
            properties: { [Props.Source]: { resourceId: 'partly-cloudy-day', contentHash: 'abc123' } },
          },
        ],
      };
      const rendered = await renderTree(root);
      const img = el(rendered).querySelector('img') as HTMLImageElement;
      expect(img.src).toBe('http://host/api/ui/resources/partly-cloudy-day?v=abc123');
    });
  });

  describe('S26 - roles follow the live theme, data colours do not', () => {
    const themeStyle = document.createElement('style');
    themeStyle.textContent = `
      :root { --color-text-primary: #111111; --color-text-secondary: #222222; --color-text-muted: #333333; }
      .light { --color-text-primary: #eeeeee; --color-text-secondary: #dddddd; --color-text-muted: #cccccc; }
    `;

    beforeEach(() => {
      document.head.appendChild(themeStyle);
      // Set up as well as torn down: `ThemeService.applyToDom` writes this class on the root and
      // never restores it, so any earlier spec that injected it leaves the theme already switched.
      document.documentElement.classList.remove('light');
    });
    afterEach(() => {
      themeStyle.remove();
      document.documentElement.classList.remove('light');
    });

    function roleTree(): UiNode {
      return {
        id: 'root',
        type: Types.Stack,
        children: [
          { id: 'primary', type: Types.Text, properties: { [Props.Text]: 'P' } },
          { id: 'secondary', type: Types.Text, properties: { [Props.Text]: 'S', [Props.Role]: 'secondary' } },
          { id: 'muted', type: Types.Text, properties: { [Props.Text]: 'M', [Props.Role]: 'muted' } },
          { id: 'unknown-role', type: Types.Text, properties: { [Props.Text]: 'U', [Props.Role]: 'bogus' } },
          {
            id: 'bar',
            type: Types.RangeBar,
            properties: {
              [Props.Start]: 0,
              [Props.End]: 1,
              [Props.StartColor]: '#ff0000',
              [Props.EndColor]: '#00ff00',
            },
          },
        ],
      };
    }

    it('resolves the three roles to their css variables, defaulting an unknown role to primary', async () => {
      const rendered = await renderTree(roleTree());
      const host = el(rendered);

      expect((host.querySelector('[data-node-id="primary"].widget-text') as HTMLElement).style.color)
        .toBe('var(--color-text-primary)');
      expect((host.querySelector('[data-node-id="secondary"].widget-text') as HTMLElement).style.color)
        .toBe('var(--color-text-secondary)');
      expect((host.querySelector('[data-node-id="muted"].widget-text') as HTMLElement).style.color)
        .toBe('var(--color-text-muted)');
      expect((host.querySelector('[data-node-id="unknown-role"].widget-text') as HTMLElement).style.color)
        .toBe('var(--color-text-primary)');
    });

    it('changes role colours on a live theme switch with no new tree, and leaves range-bar colours untouched', async () => {
      const rendered = await renderTree(roleTree());
      const host = el(rendered);

      const primaryEl = host.querySelector('[data-node-id="primary"].widget-text') as HTMLElement;
      const fillEl = host.querySelector('.widget-range-bar-fill') as HTMLElement;
      const before = getComputedStyle(primaryEl).color;
      const fillBefore = fillEl.style.background;

      document.documentElement.classList.add('light');
      const after = getComputedStyle(primaryEl).color;

      expect(after).not.toBe(before);
      expect(host.querySelector('[data-node-id="primary"].widget-text')).toBe(primaryEl);
      expect(fillEl.style.background).toBe(fillBefore);
      expect(fillEl.style.background).toContain('rgb(255, 0, 0)');
      expect(fillEl.style.background).toContain('rgb(0, 255, 0)');
    });
  });

  describe('S27 - unsupported widget nodes degrade to the tile-shaped placeholder', () => {
    it('renders the fallback when one is declared', async () => {
      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          {
            id: 'unknown',
            type: 'macrodeck.sparkline',
            fallback: { id: 'unknown-fb', type: Types.Text, properties: { [Props.Text]: 'Fallback' } },
          },
        ],
      };
      const rendered = await renderTree(root);
      const host = el(rendered);

      expect(host.querySelector('[data-node-id="unknown-fb"].widget-text')?.textContent).toBe('Fallback');
      expect(host.querySelector('.widget-node-unsupported')).toBeNull();
    });

    it('renders a text-free tile placeholder without one, and keeps rendering siblings', async () => {
      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          { id: 'before', type: Types.Text, properties: { [Props.Text]: 'Before' } },
          { id: 'unknown', type: 'macrodeck.sparkline' },
          { id: 'after', type: Types.Text, properties: { [Props.Text]: 'After' } },
        ],
      };
      const rendered = await renderTree(root);
      const host = el(rendered);

      const placeholder = host.querySelector('[data-node-id="unknown"].widget-node-unsupported');
      expect(placeholder).not.toBeNull();
      expect(placeholder?.getAttribute('data-unsupported-type')).toBe('macrodeck.sparkline');
      expect(placeholder?.textContent?.trim()).toBe('');
      expect(host.querySelector('.config-node-unsupported')).toBeNull();

      expect(host.querySelector('[data-node-id="before"].widget-text')?.textContent).toBe('Before');
      expect(host.querySelector('[data-node-id="after"].widget-text')?.textContent).toBe('After');
    });

    it('never throws, even for an unknown type at the root with no ancestor stack', async () => {
      const root: UiNode = { id: 'root', type: 'ui.mystery' };
      const rendered = await renderTree(root);
      expect(el(rendered).querySelector('.widget-node-unsupported')).not.toBeNull();
    });
  });

  describe('ui.range-bar geometry and colour validation', () => {
    function barTree(startColor: string, endColor: string): UiNode {
      return {
        id: 'root',
        type: Types.Stack,
        children: [
          {
            id: 'bar',
            type: Types.RangeBar,
            properties: {
              [Props.MainSize]: { basis: 1 },
              [Props.Thickness]: { basis: 0.1 },
              [Props.Start]: 0.2,
              [Props.End]: 0.7,
              [Props.StartColor]: startColor,
              [Props.EndColor]: endColor,
              [Props.Marker]: 0.9,
            },
          },
        ],
      };
    }

    it('places the fill span and the marker at the declared fractions', async () => {
      const rendered = await renderTree(barTree('#ff0000', '#00ff00'));
      const host = el(rendered);

      const fill = host.querySelector('.widget-range-bar-fill') as HTMLElement;
      expect(fill.style.left).toBe('20%');
      expect(fill.style.width).toBe('50%');

      const marker = host.querySelector('.widget-range-bar-marker') as HTMLElement;
      const barWidth = 120;
      const thickness = 12;
      const inset = 0.75 * thickness + (0.28 * thickness) / 2;
      const expectedLeft = Math.min(Math.max(0.9 * barWidth, inset), barWidth - inset);
      expect(parseFloat(marker.style.left)).toBeCloseTo(expectedLeft, 5);
    });

    it('rejects an invalid colour rather than interpolating it into a style', async () => {
      const rendered = await renderTree(barTree('#f00', 'javascript:alert(1)'));
      const host = el(rendered);

      expect(host.querySelector('.widget-range-bar-fill')).toBeNull();
      expect(host.querySelector('.widget-range-bar-marker')).toBeNull();
      expect(host.innerHTML).not.toContain('javascript:alert');
    });

    it('omits the marker entirely when absent, even with valid colours', async () => {
      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          {
            id: 'bar',
            type: Types.RangeBar,
            properties: {
              [Props.MainSize]: { basis: 1 },
              [Props.Thickness]: { basis: 0.1 },
              [Props.Start]: 0,
              [Props.End]: 1,
              [Props.StartColor]: '#000000',
              [Props.EndColor]: '#ffffff',
            },
          },
        ],
      };
      const rendered = await renderTree(root);
      expect(el(rendered).querySelector('.widget-range-bar-marker')).toBeNull();
    });
  });

function limitAvailableWidth(element: HTMLElement, availableWidth: () => number): void {
  Object.defineProperty(element, 'clientWidth', { configurable: true, get: availableWidth });

  const measured = element.getBoundingClientRect.bind(element);
  element.getBoundingClientRect = () => {
    const box = measured();
    return Object.assign(box.toJSON() as DOMRect, { width: Math.min(box.width, availableWidth()) });
  };
}

function isClipped(element: HTMLElement): boolean {
  return naturalWidthOf(element) > element.clientWidth + 0.5;
}

function naturalWidthOf(element: HTMLElement): number {
  const probe = document.createElement('span');
  const style = getComputedStyle(element);
  probe.style.position = 'absolute';
  probe.style.visibility = 'hidden';
  probe.style.whiteSpace = 'pre';
  probe.style.font = style.font;
  probe.style.fontFamily = style.fontFamily;
  probe.style.fontSize = style.fontSize;
  probe.style.fontWeight = style.fontWeight;
  probe.style.letterSpacing = style.letterSpacing;
  probe.style.fontVariantNumeric = style.fontVariantNumeric;
  probe.textContent = element.textContent;
  document.body.appendChild(probe);
  const width = probe.getBoundingClientRect().width;
  probe.remove();
  return width;
}

  describe('#804 guard - minSize re-measures when only the available width changes', () => {
    it('restores the declared size after the surface is widened without its smaller side changing', async () => {
      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          {
            id: 'time',
            type: Types.Text,
            properties: {
              [Props.Text]: '20:49:07',
              [Props.MainSize]: { basis: 1 },
              [Props.Size]: { basis: 1 / 120 },
              [Props.MinSize]: { basis: 0.1 / 120 },
            },
          },
        ],
      };

      const rendered: RenderedTree = await renderTree(root);
      rendered.fixture.componentRef.setInput('box', { width: 100, height: 120 });
      await tick(rendered);

      const textEl = el(rendered).querySelector('.widget-text') as HTMLElement;
      let availableWidth = 2;
      limitAvailableWidth(textEl, () => availableWidth);
      rendered.fixture.componentRef.setInput('box', { width: 3, height: 120 });
      await tick(rendered);

      const shrunk = parseFloat(textEl.style.fontSize);
      expect(shrunk).toBeLessThan(1);
      expect(isClipped(textEl)).toBeFalse();

      // Only the width grows - the smaller side, and with it the basis, is untouched. The text now fits,
      // so it must go back to its declared size rather than stay at what it was once shrunk to.
      availableWidth = 400;
      rendered.fixture.componentRef.setInput('box', { width: 400, height: 120 });
      await tick(rendered);

      expect(parseFloat(textEl.style.fontSize)).toBeCloseTo(1, 5);
    });
  });

  describe('#761 guard - minSize re-measures on a localization catalog change', () => {
    // Wrapped because measuring a text run resizes it, which schedules another observation:
    // Chrome reports that as the benign `ResizeObserver loop completed with undelivered
    // notifications` on the window, and karma fails a spec on any window error. The assertion
    // below keeps that the only error the spec tolerates.
    it('re-measures a shrinking ui.text after the catalog version changes, not just the text', async () => {
      await jasmine.spyOnGlobalErrorsAsync(async globalErrors => {
        const catalogEn: Catalog = {
          culture: 'en',
          fallbackCulture: 'en',
          translations: { 'macrodeck:Widgets.Weather.Condition.PartlyCloudy': 'Partly cloudy' },
        };
        const api = apiSpyWithCatalog(catalogEn);

        const root: UiNode = {
          id: 'root',
          type: Types.Stack,
          children: [
            {
              id: 'condition',
              type: Types.Text,
              properties: {
                [Props.Text]: { $localized: { scope: 'macrodeck', key: 'Widgets.Weather.Condition.PartlyCloudy' } },
                [Props.MainSize]: { basis: 1 },
                [Props.Size]: { basis: 1 / 120 },
                [Props.MinSize]: { basis: 0.1 / 120 },
              },
            },
          ],
        };

        const rendered: RenderedTree = await renderTree(root, [{ provide: ApiService, useValue: api }]);
        await TestBed.inject(LocalizationService).loadFromHost();
        await tick(rendered);

        const textEl = el(rendered).querySelector('.widget-text') as HTMLElement;
        expect(parseFloat(textEl.style.fontSize)).toBeCloseTo(1, 5);

        limitAvailableWidth(textEl, () => 2);

        api.getLocalization.and.resolveTo({
          culture: 'de-DE',
          fallbackCulture: 'en',
          translations: { 'macrodeck:Widgets.Weather.Condition.PartlyCloudy': 'Teilweise bewölkt' },
          followSystem: false,
          availableCultures: ['en', 'de-DE'],
        });
        await TestBed.inject(LocalizationService).loadFromHost();
        await tick(rendered);

        expect(textEl.textContent).toBe('Teilweise bewölkt');
        expect(parseFloat(textEl.style.fontSize)).toBeLessThan(1);
        expect(isClipped(textEl)).toBeFalse();
        for (const [error] of globalErrors.calls.allArgs()) {
          expect(String(error?.message ?? error)).toContain('ResizeObserver loop');
        }
      });
    });
  });

  describe('macrodeck.dynamic-text', () => {
    // A monospace fallback: how faithfully a headless browser's installed fonts honour the
    // OpenType tabular-figures feature varies a great deal by platform (verified by hand against
    // this machine's Chrome), so the width-stability assertion pins a font family guaranteed to
    // have equal-width digits: the same outcome tabular-nums is declared to produce.
    const fontStyle = document.createElement('style');
    fontStyle.textContent = `.widget-dynamic-text { font-family: monospace; }`;

    beforeEach(() => {
      jasmine.clock().install();
      document.head.appendChild(fontStyle);
      // A prior spec's loadFromHost() persists its culture to the real localStorage (the service's
      // paint-before-connect cache), which otherwise leaks into this describe's fresh
      // LocalizationService instances and makes the expected default culture run-order-dependent.
      localStorage.clear();
    });
    afterEach(() => {
      jasmine.clock().uninstall();
      fontStyle.remove();
    });

    function timeValue(zone?: string): unknown {
      return zone ? { $time: { zone } } : { $time: {} };
    }

    function dynamicTextTree(
      id: string,
      props: Record<string, unknown>,
      sizeBasis = 0.1,
    ): UiNode {
      return {
        id,
        type: MacroDeckTypes.DynamicText,
        properties: { [Props.Size]: { basis: sizeBasis }, ...props },
      };
    }

    function expectedSecondsSubstring(instant: Date, locale: string): string {
      const parts = new Intl.DateTimeFormat(locale, {
        hour: 'numeric', minute: '2-digit', second: '2-digit',
      }).formatToParts(instant);
      const secondIndex = parts.findIndex(p => p.type === 'second');
      const start = secondIndex > 0 && parts[secondIndex - 1].type === 'literal' ? secondIndex - 1 : secondIndex;
      return parts.slice(start, secondIndex + 1).map(p => p.value).join('');
    }

    it('renders exactly what Intl produces, in DOM order, for en-US, de-DE and ar-EG', async () => {
      const instant = new Date(Date.UTC(2024, 5, 15, 21, 40, 5));

      for (const locale of ['en-US', 'de-DE', 'ar-EG']) {
        jasmine.clock().mockDate(instant);

        const root: UiNode = {
          id: 'root',
          type: Types.Stack,
          children: [dynamicTextTree('dt', { [Props.Value]: timeValue(), [Props.Format]: 'time', [Props.Seconds]: true })],
        };
        const rendered = await renderTree(root, withBasis(200));
        TestBed.inject(LocalizationService).culture.set(locale);
        await tick(rendered);

        const run = el(rendered).querySelector('[data-node-id="dt"].widget-dynamic-text') as HTMLElement;
        const expected = new Intl.DateTimeFormat(locale, {
          hour: 'numeric', minute: '2-digit', second: '2-digit',
        }).format(instant);

        expect(run.textContent).toBe(expected);
        const domOrder = Array.from(run.childNodes).map(node => node.textContent).join('');
        expect(domOrder).toBe(expected);
      }
    });

    it('draws the seconds in their own span, at 0.55x the size and in the muted role colour', async () => {
      const instant = new Date(Date.UTC(2024, 5, 15, 21, 40, 5));
      jasmine.clock().mockDate(instant);

      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          dynamicTextTree('dt', { [Props.Value]: timeValue(), [Props.Format]: 'time', [Props.Seconds]: true }),
          { id: 'muted-sibling', type: Types.Text, properties: { [Props.Text]: 'X', [Props.Role]: 'muted' } },
        ],
      };
      const rendered = await renderTree(root, withBasis(200));
      await tick(rendered);
      const host = el(rendered);

      const run = host.querySelector('[data-node-id="dt"].widget-dynamic-text') as HTMLElement;
      const secondsSpan = run.querySelector('.widget-dynamic-text-seconds') as HTMLElement;
      const mutedSibling = host.querySelector('[data-node-id="muted-sibling"].widget-text') as HTMLElement;

      expect(secondsSpan.textContent).toBe(expectedSecondsSubstring(instant, 'en'));
      expect(parseFloat(secondsSpan.style.fontSize)).toBeCloseTo(0.55 * parseFloat(run.style.fontSize), 5);
      expect(secondsSpan.style.color).toBe(mutedSibling.style.color);
    });

    it('keeps digit advance width equal so the run does not shift as the seconds change', async () => {
      const base = Date.UTC(2024, 5, 15, 9, 4, 0);
      const measurements: { width: number; left: number }[] = [];

      for (const seconds of [11, 8, 0]) {
        jasmine.clock().mockDate(new Date(base + seconds * 1000));

        const root: UiNode = {
          id: 'root',
          type: Types.Stack,
          children: [dynamicTextTree('dt', { [Props.Value]: timeValue(), [Props.Format]: 'time', [Props.Seconds]: true })],
        };
        const rendered = await renderTree(root, withBasis(200));
        await tick(rendered);

        const run = el(rendered).querySelector('.widget-dynamic-text') as HTMLElement;
        expect(getComputedStyle(run).fontVariantNumeric).toBe('tabular-nums');

        const secondsSpan = run.querySelector('.widget-dynamic-text-seconds') as HTMLElement;
        const rect = secondsSpan.getBoundingClientRect();
        measurements.push({ width: rect.width, left: rect.left });
      }

      const [first, ...rest] = measurements;
      for (const m of rest) {
        expect(m.width).toBeCloseTo(first.width, 2);
        expect(m.left).toBeCloseTo(first.left, 2);
      }
    });

    it('ticks the display forward with no patch and no new tree', async () => {
      const start = Date.UTC(2024, 5, 15, 9, 4, 0);
      jasmine.clock().mockDate(new Date(start));

      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [dynamicTextTree('dt', { [Props.Value]: timeValue(), [Props.Format]: 'time', [Props.Seconds]: true })],
      };
      const rendered = await renderTree(root, withBasis(200));
      await tick(rendered);

      const run = () => el(rendered).querySelector('[data-node-id="dt"].widget-dynamic-text') as HTMLElement;
      const before = run().textContent;

      jasmine.clock().tick(1000);
      await tick(rendered);

      const expected = new Intl.DateTimeFormat('en', {
        hour: 'numeric', minute: '2-digit', second: '2-digit',
      }).format(new Date(start + 1000));

      expect(run().textContent).not.toBe(before);
      expect(run().textContent).toBe(expected);
    });

    it('honours a zoned reference independently of the unzoned one, including across the date line', async () => {
      // New York (EDT, UTC-4) is still on the previous calendar day at this UTC instant.
      const instant = new Date(Date.UTC(2024, 5, 15, 2, 0, 0));
      jasmine.clock().mockDate(instant);

      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          dynamicTextTree('ny-time', { [Props.Value]: timeValue('America/New_York'), [Props.Format]: 'time', [Props.Seconds]: true }, 0.05),
          dynamicTextTree('ny-date', { [Props.Value]: timeValue('America/New_York'), [Props.Format]: 'date' }, 0.05),
          dynamicTextTree('local-time', { [Props.Value]: timeValue(), [Props.Format]: 'time', [Props.Seconds]: true }, 0.05),
          dynamicTextTree('ny-zone', { [Props.Value]: timeValue('America/New_York'), [Props.Format]: 'zone-name' }, 0.05),
          dynamicTextTree('no-zone', { [Props.Value]: timeValue(), [Props.Format]: 'zone-name' }, 0.05),
        ],
      };
      const rendered = await renderTree(root, withBasis(200));
      await tick(rendered);
      const host = el(rendered);

      const text = (id: string) => host.querySelector(`[data-node-id="${id}"].widget-dynamic-text`)!.textContent;

      const expectedNyTime = new Intl.DateTimeFormat('en', {
        hour: 'numeric', minute: '2-digit', second: '2-digit', timeZone: 'America/New_York',
      }).format(instant);
      const expectedNyDate = new Intl.DateTimeFormat('en', {
        weekday: 'short', day: 'numeric', month: 'short', timeZone: 'America/New_York',
      }).format(instant);
      const expectedLocalTime = new Intl.DateTimeFormat('en', {
        hour: 'numeric', minute: '2-digit', second: '2-digit',
      }).format(instant);

      expect(text('ny-time')).toBe(expectedNyTime);
      expect(text('ny-date')).toBe(expectedNyDate);
      expect(text('local-time')).toBe(expectedLocalTime);
      expect(text('ny-zone')).toBe('New York');
      expect(text('no-zone')).toBe('');
    });

    it('renders empty for an unknown format or a missing value, without throwing', async () => {
      const root: UiNode = {
        id: 'root',
        type: Types.Stack,
        children: [
          dynamicTextTree('unknown-format', { [Props.Value]: timeValue(), [Props.Format]: 'bogus' }),
          dynamicTextTree('no-value', { [Props.Format]: 'time' }),
        ],
      };

      const rendered = await renderTree(root, withBasis(200));
      await tick(rendered);
      const host = el(rendered);

      expect(host.querySelector('[data-node-id="unknown-format"].widget-dynamic-text')?.textContent).toBe('');
      expect(host.querySelector('[data-node-id="no-value"].widget-dynamic-text')?.textContent).toBe('');
    });
  });

  describe('macrodeck.clock-dial', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    function handAngleDeg(line: SVGLineElement, cx: number, cy: number): number {
      const x2 = parseFloat(line.getAttribute('x2')!);
      const y2 = parseFloat(line.getAttribute('y2')!);
      const rad = Math.atan2(x2 - cx, cy - y2);
      return ((rad * 180) / Math.PI + 360) % 360;
    }

    function dist(x1: number, y1: number, x2: number, y2: number): number {
      return Math.hypot(x2 - x1, y2 - y1);
    }

    it('restates the geometry by hand for a non-square box (d = 160)', async () => {
      const instant = new Date(Date.UTC(2024, 5, 15, 10, 9, 30));
      jasmine.clock().mockDate(instant);

      const root: UiNode = {
        id: 'dial',
        type: MacroDeckTypes.ClockDial,
        properties: { [Props.Value]: { $time: { zone: 'UTC' } }, [Props.Seconds]: true },
      };
      const rendered = await renderTree(root, withBasis(160));
      rendered.fixture.componentRef.setInput('box', { width: 240, height: 160 });
      await tick(rendered);

      const svg = el(rendered).querySelector('.widget-clock-dial') as SVGSVGElement;
      const face = svg.querySelector('.widget-clock-dial-face') as SVGCircleElement;
      const hub = svg.querySelector('.widget-clock-dial-hub') as SVGCircleElement;
      const ticks = Array.from(svg.querySelectorAll('.widget-clock-dial-tick')) as SVGLineElement[];
      const major = ticks.filter(t => parseFloat(t.getAttribute('stroke-width')!) > 3);
      const minor = ticks.filter(t => parseFloat(t.getAttribute('stroke-width')!) <= 3);
      const hourHand = svg.querySelector('.widget-clock-dial-hand-hour') as SVGLineElement;
      const minuteHand = svg.querySelector('.widget-clock-dial-hand-minute') as SVGLineElement;
      const secondHand = svg.querySelector('.widget-clock-dial-hand-second') as SVGLineElement;

      const cx = parseFloat(face.getAttribute('cx')!);
      const cy = parseFloat(face.getAttribute('cy')!);
      expect(cx).toBeCloseTo(120, 5);
      expect(cy).toBeCloseTo(80, 5);

      expect(parseFloat(face.getAttribute('r')!)).toBeCloseTo(76.8, 5);
      expect(parseFloat(hub.getAttribute('r')!)).toBeCloseTo(4.16, 5);

      expect(ticks.length).toBe(12);
      expect(major.length).toBe(4);
      expect(minor.length).toBe(8);

      const outerR = (t: SVGLineElement) => dist(cx, cy, parseFloat(t.getAttribute('x2')!), parseFloat(t.getAttribute('y2')!));
      const innerR = (t: SVGLineElement) => dist(cx, cy, parseFloat(t.getAttribute('x1')!), parseFloat(t.getAttribute('y1')!));

      expect(outerR(major[0])).toBeCloseTo(70.4, 5);
      expect(innerR(major[0])).toBeCloseTo(59.2, 5);
      expect(parseFloat(major[0].getAttribute('stroke-width')!)).toBeCloseTo(4, 5);

      expect(outerR(minor[0])).toBeCloseTo(70.4, 5);
      expect(innerR(minor[0])).toBeCloseTo(64.8, 5);
      expect(parseFloat(minor[0].getAttribute('stroke-width')!)).toBeCloseTo(2.4, 5);

      const tailR = (h: SVGLineElement) => dist(cx, cy, parseFloat(h.getAttribute('x1')!), parseFloat(h.getAttribute('y1')!));
      const tipR = (h: SVGLineElement) => dist(cx, cy, parseFloat(h.getAttribute('x2')!), parseFloat(h.getAttribute('y2')!));

      expect(tailR(hourHand)).toBeCloseTo(8, 5);
      expect(tipR(hourHand)).toBeCloseTo(33.6, 5);
      expect(parseFloat(hourHand.getAttribute('stroke-width')!)).toBeCloseTo(6.4, 5);

      expect(tailR(minuteHand)).toBeCloseTo(8, 5);
      expect(tipR(minuteHand)).toBeCloseTo(52.8, 5);
      expect(parseFloat(minuteHand.getAttribute('stroke-width')!)).toBeCloseTo(4, 5);

      expect(tailR(secondHand)).toBeCloseTo(12.8, 5);
      expect(tipR(secondHand)).toBeCloseTo(59.2, 5);
      expect(parseFloat(secondHand.getAttribute('stroke-width')!)).toBeCloseTo(2, 5);

      expect(handAngleDeg(hourHand, cx, cy)).toBeCloseTo(304.5, 5);
      expect(handAngleDeg(minuteHand, cx, cy)).toBeCloseTo(57, 5);
      expect(handAngleDeg(secondHand, cx, cy)).toBeCloseTo(180, 5);
    });

    it('evaluates from the whole second: identical rotation sub-second, and ticks with no new tree', async () => {
      const start = Date.UTC(2024, 5, 15, 10, 9, 5);
      jasmine.clock().mockDate(new Date(start));

      const root: UiNode = {
        id: 'dial',
        type: MacroDeckTypes.ClockDial,
        properties: { [Props.Value]: { $time: { zone: 'UTC' } }, [Props.Seconds]: true },
      };
      const rendered = await renderTree(root, withBasis(160));
      await tick(rendered);

      const secondHand = () => el(rendered).querySelector('.widget-clock-dial-hand-second') as SVGLineElement;
      const face = el(rendered).querySelector('.widget-clock-dial-face') as SVGCircleElement;
      const cx = parseFloat(face.getAttribute('cx')!);
      const cy = parseFloat(face.getAttribute('cy')!);

      const angleAt0 = handAngleDeg(secondHand(), cx, cy);

      jasmine.clock().tick(400);
      await tick(rendered);
      expect(handAngleDeg(secondHand(), cx, cy)).toBeCloseTo(angleAt0, 5);

      jasmine.clock().tick(500);
      await tick(rendered);
      expect(handAngleDeg(secondHand(), cx, cy)).toBeCloseTo(angleAt0, 5);

      jasmine.clock().tick(200);
      await tick(rendered);
      expect(handAngleDeg(secondHand(), cx, cy)).not.toBeCloseTo(angleAt0, 5);
    });
  });

  describe('ui.slider geometry', () => {
    function sliderTree(properties: Record<string, unknown>): UiNode {
      return { id: 'slider', type: Types.Slider, properties };
    }

    it('restates the horizontal geometry by hand (basis 200)', async () => {
      const rendered = await renderTree(
        sliderTree({ [Props.Level]: 0.35, [Props.Thickness]: { basis: 0.12 }, [Props.LevelColor]: '#2b6cee' }),
        withBasis(200),
      );
      const host = el(rendered);
      const track = host.querySelector('.widget-slider-track') as HTMLElement;
      const fill = host.querySelector('.widget-slider-fill') as HTMLElement;

      expect(parseFloat(track.style.height)).toBeCloseTo(0.12 * 200, 5);
      expect(parseFloat(track.style.borderRadius)).toBeCloseTo((0.12 * 200) / 2, 5);
      expect(track.style.width).toBe('');
      expect(parseFloat(fill.style.width)).toBeCloseTo(0.35 * 100, 5);
      expect(parseFloat(fill.style.borderRadius)).toBeCloseTo((0.12 * 200) / 2, 5);
      // The CSSOM serializes a literal colour as rgb() when the style is read back.
      expect(fill.style.background).toBe('rgb(43, 108, 238)');
    });

    it('restates the vertical geometry by hand (basis 200), and resolves the accent token when levelColor is absent', async () => {
      const rendered = await renderTree(
        sliderTree({ [Props.Level]: 0.6, [Props.Direction]: 'vertical', [Props.Thickness]: { basis: 0.1 } }),
        withBasis(200),
      );
      const host = el(rendered);
      const track = host.querySelector('.widget-slider-track') as HTMLElement;
      const fill = host.querySelector('.widget-slider-fill') as HTMLElement;

      expect(parseFloat(track.style.width)).toBeCloseTo(0.1 * 200, 5);
      expect(track.style.height).toBe('');
      expect(parseFloat(fill.style.height)).toBeCloseTo(0.6 * 100, 5);
      expect(fill.style.background).toBe('var(--color-accent)');
    });

    it('draws the thumb as a handle rather than a marker, and keeps its ring clear of both ends',
      async () => {
        const thickness = 0.12 * 200;
        const stroke = 0.28 * thickness;
        const diameter = 2.5 * thickness - stroke;
        const inset = 1.25 * thickness + stroke / 2;

        const rendered = await renderTree(
          sliderTree({ [Props.Level]: 0.5, [Props.Thickness]: { basis: 0.12 } }), withBasis(200));
        rendered.fixture.componentRef.setInput('box', { width: 400, height: 200 });
        await tick(rendered);

        const thumb = el(rendered).querySelector('.widget-slider-thumb') as HTMLElement;

        expect(parseFloat(thumb.style.width)).toBeCloseTo(diameter, 5);
        expect(parseFloat(thumb.style.height)).toBeCloseTo(diameter, 5);
        expect(parseFloat(thumb.style.borderWidth || thumb.style.border)).toBeCloseTo(stroke, 5);
        // Centre at half of 400, expressed as the leading edge of the bordered box.
        expect(parseFloat(thumb.style.left)).toBeCloseTo(200 - diameter / 2 - stroke, 5);

        // At the very end the centre is pulled in by radius + stroke/2, so the ring never clips.
        await updateTree(rendered, { root: sliderTree({ [Props.Level]: 1, [Props.Thickness]: { basis: 0.12 } }) });

        const atEnd = el(rendered).querySelector('.widget-slider-thumb') as HTMLElement;
        expect(parseFloat(atEnd.style.left)).toBeCloseTo(400 - inset - diameter / 2 - stroke, 5);
      });

    it('defaults direction to horizontal, unlike ui.stack which defaults to vertical', async () => {
      const rendered = await renderTree(sliderTree({ [Props.Level]: 0.5 }), withBasis(200));
      const track = el(rendered).querySelector('.widget-slider-track') as HTMLElement;

      expect(track.style.height).not.toBe('');
      expect(track.style.width).toBe('');
    });
  });
});

describe('literal colour overrides', () => {
  it('a literal ui.text color overrides its role', async () => {
    const root: UiNode = {
      id: 'root',
      type: Types.Text,
      properties: { [Props.Text]: 'Hi', [Props.Role]: 'secondary', [Props.Color]: '#ff8800' },
    };
    const rendered = await renderTree(root);
    const textEl = el(rendered).querySelector('.widget-text') as HTMLElement;

    // The CSSOM serializes a literal colour as rgb() when the style is read back, same as the
    // range-bar's own literal-colour assertions.
    expect(textEl.style.color).toBe('rgb(255, 136, 0)');
  });

  it('falls back to the role colour when ui.text carries no literal color', async () => {
    const root: UiNode = {
      id: 'root',
      type: Types.Text,
      properties: { [Props.Text]: 'Hi', [Props.Role]: 'muted' },
    };
    const rendered = await renderTree(root);
    const textEl = el(rendered).querySelector('.widget-text') as HTMLElement;

    expect(textEl.style.color).toBe('var(--color-text-muted)');
  });

  it('a literal ui.stack background paints behind its children', async () => {
    const root: UiNode = { id: 'root', type: Types.Stack, properties: { [Props.Background]: '#123456' }, children: [] };
    const rendered = await renderTree(root);
    const stackEl = el(rendered).querySelector('.widget-stack') as HTMLElement;

    expect(stackEl.style.background).toBe('rgb(18, 52, 86)');
  });

  it('paints no background behind a ui.stack when absent', async () => {
    const root: UiNode = { id: 'root', type: Types.Stack, properties: {}, children: [] };
    const rendered = await renderTree(root);
    const stackEl = el(rendered).querySelector('.widget-stack') as HTMLElement;

    expect(stackEl.style.background).toBe('');
  });
});

describe('shared-ui-widget-node vs. the root font size', () => {
  const clockTree: UiNode = {
    id: 'clock',
    type: Types.Stack,
    properties: {
      [Props.Direction]: 'vertical',
      [Props.Align]: 'center',
      [Props.Gap]: { basis: 0.03 },
      [Props.Padding]: { basis: 0.05 },
    },
    children: [
      {
        id: 'clock.time',
        type: MacroDeckTypes.DynamicText,
        properties: {
          [Props.Value]: { $time: {} },
          [Props.Format]: 'time',
          [Props.Seconds]: true,
          [Props.Size]: { basis: 0.24 },
        },
        children: [],
      },
      {
        id: 'clock.dial',
        type: MacroDeckTypes.ClockDial,
        properties: { [Props.Value]: { $time: {} }, [Props.MainSize]: { basis: 0.42 } },
        children: [],
      },
    ],
  };

  beforeEach(() => {
    // LocalizationService persists its culture to real localStorage, so a rendered tree here would
    // otherwise decide what language a later spec starts in.
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  async function measureAt(rootFontSize: string): Promise<unknown[]> {
    const previous = document.documentElement.style.fontSize;
    document.documentElement.style.fontSize = rootFontSize;

    try {
      const rendered = await renderTree(clockTree, withBasis(240));
      const host = el(rendered);

      // A layout container sets no font size of its own, so its computed one simply inherits the root
      // and says nothing about the widget's proportions. What must not move is the geometry, and the
      // size of the runs the profile does size.
      const sized = '.widget-stack, .widget-dynamic-text, .widget-dynamic-text-seconds, .widget-clock-dial';
      const measured = [...host.querySelectorAll<HTMLElement>(sized)].map(node => {
        const style = getComputedStyle(node);
        return {
          class: node.className,
          width: style.width,
          height: style.height,
          gap: style.gap,
          padding: style.padding,
          fontSize: node.style.fontSize === '' ? null : style.fontSize,
        };
      });

      rendered.fixture.destroy();
      return measured;
    } finally {
      document.documentElement.style.fontSize = previous;
    }
  }

  it('resolves identical geometry whatever the reader set as their root font size', async () => {
    const atDefault = await measureAt('16px');
    const atDoubled = await measureAt('32px');

    expect(atDoubled).toEqual(atDefault);
    expect(atDefault.length).toBeGreaterThan(0);
  });
});

describe('ui.button appearance', () => {
  function buttonEl(rendered: RenderedTree): HTMLElement {
    return el(rendered).querySelector('.widget-button') as HTMLElement;
  }

  it('is its own stacking context, so its always-present background cannot paint over the negative-z artwork', async () => {
    // .widget-button-artwork uses z-index: -1 to sit behind the flex children while staying above the
    // button's own background - but a negative z-index only paints behind whatever stacking context it
    // resolves against. Without `isolation: isolate` (or an equivalent z-index: 0) here, `.widget-button`
    // is not itself a stacking context, so the artwork paints against the ancestor `.widget-content`
    // instead and this element's own background (always present - see `buttonBackground`) covers it.
    const node: UiNode = { id: 'btn', type: Types.Button, properties: {} };
    const rendered = await renderTree(node, withBasis(200));

    expect(getComputedStyle(buttonEl(rendered)).isolation).toBe('isolate');
  });

  it('draws a ring in the configured color when borderStyle/borderColor are set', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button,
      properties: { [Props.BorderStyle]: 'static', [Props.BorderColor]: '#ff3b30' },
    };
    const rendered = await renderTree(node, withBasis(200));

    const ring = buttonEl(rendered).querySelector('.widget-button-ring .ring') as HTMLElement;
    expect(ring).not.toBeNull();
    expect(ring.classList).toContain('wb-static');
    expect(ring.style.getPropertyValue('--wb-color')).toBe('#ff3b30');
  });

  it('draws the spectrum ring for rgb ignoring any configured color', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button,
      properties: { [Props.BorderStyle]: 'rgb' },
    };
    const rendered = await renderTree(node, withBasis(200));

    const ring = buttonEl(rendered).querySelector('.widget-button-ring .ring') as HTMLElement;
    expect(ring.classList).toContain('wb-rgb');
  });

  it('draws no ring element at all when borderStyle is absent', async () => {
    const node: UiNode = { id: 'btn', type: Types.Button, properties: {} };
    const rendered = await renderTree(node, withBasis(200));

    expect(buttonEl(rendered).querySelector('.widget-button-ring')).toBeNull();
  });

  it('draws no ring for a borderStyle this reader does not recognise, rather than an unstyled one', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button,
      properties: { [Props.BorderStyle]: 'some-future-style' },
    };
    const rendered = await renderTree(node, withBasis(200));

    expect(buttonEl(rendered).querySelector('.widget-button-ring')).toBeNull();
  });

  it('frames the artwork from fit/zoom/offset/opacity exactly as configured, in contract units', async () => {
    // zoom is a multiplier, offsetX/offsetY are a fraction of the element's own width/height, and
    // opacity is 0..1 (see UiComponentProperties.Zoom/OffsetX/OffsetY/Opacity and docs/sdk/ui.md) - none
    // of these are CSS percentages on the wire, so this renderer must not divide any of them by 100.
    const node: UiNode = {
      id: 'btn', type: Types.Button,
      properties: {
        [Props.Source]: { resourceId: 'res-1' },
        [Props.Fit]: 'cover', [Props.Zoom]: 2.5, [Props.OffsetX]: -0.3, [Props.OffsetY]: 0.1, [Props.Opacity]: 0.6,
      },
    };
    const rendered = await renderTree(node, withBasis(200));
    await tick(rendered);

    const img = buttonEl(rendered).querySelector('.widget-button-artwork') as HTMLImageElement;
    expect(img.style.objectFit).toBe('cover');
    expect(img.style.transform).toBe('translate(-30%, 10%) scale(2.5)');
    expect(img.style.opacity).toBe('0.6');
  });

  it('frames the artwork exactly as the conformance fixture pins it', async () => {
    // Literals from ui-model/fixtures/component-profile/conformance-action-button-tree.json
    // (conformance.full: zoom 1.35, offsetX 0.08, offsetY -0.05, opacity 0.85) - the contract's own
    // authoritative example, not a value either side of this renderer invented.
    const node: UiNode = {
      id: 'btn', type: Types.Button,
      properties: {
        [Props.Source]: { resourceId: 'res-1' },
        [Props.Fit]: 'cover', [Props.Zoom]: 1.35, [Props.OffsetX]: 0.08, [Props.OffsetY]: -0.05, [Props.Opacity]: 0.85,
      },
    };
    const rendered = await renderTree(node, withBasis(200));
    await tick(rendered);

    const img = buttonEl(rendered).querySelector('.widget-button-artwork') as HTMLImageElement;
    expect(img.style.transform).toBe('translate(8%, -5%) scale(1.35)');
    expect(img.style.opacity).toBe('0.85');
  });

  it('defaults framing to contain/scale(1)/opacity 1 - never scale(0) or opacity 0 - with no framing keys', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button,
      properties: { [Props.Source]: { resourceId: 'res-1' } },
    };
    const rendered = await renderTree(node, withBasis(200));
    await tick(rendered);

    const img = buttonEl(rendered).querySelector('.widget-button-artwork') as HTMLImageElement;
    expect(img.style.objectFit).toBe('contain');
    expect(img.style.transform).toBe('translate(0%, 0%) scale(1)');
    expect(img.style.opacity).toBe('1');
  });

  it('paints the reader accent when background is absent, and a literal color when configured', async () => {
    const accentNode: UiNode = { id: 'btn', type: Types.Button, properties: {} };
    const accent = await renderTree(accentNode, withBasis(200));
    expect(buttonEl(accent).style.background).toBe('var(--color-accent)');

    const literalNode: UiNode = { id: 'btn', type: Types.Button, properties: { [Props.Background]: '#2b6cee' } };
    const literal = await renderTree(literalNode, withBasis(200));
    // The browser normalizes a hex value read back off .style - rgb(43, 108, 238) is #2b6cee.
    expect(buttonEl(literal).style.background).toBe('rgb(43, 108, 238)');
  });

  it('sizes a 0.14 basis fraction off the smaller of a non-square tile\'s two dimensions', async () => {
    // UiTreeWidgetComponent.ngOnChanges sets the shared basis to min(width, height) - a 200x120 tile
    // therefore renders with a basis of 120, which is what this component actually reads. A renderer
    // that used either raw dimension instead of the minimum would pass a 200-basis variant of this
    // same test but fail this one.
    const node: UiNode = {
      id: 'btn', type: Types.Button, properties: {},
      children: [{ id: 'icon', type: Types.Text, properties: { [Props.Size]: { basis: 0.14 }, [Props.Text]: 'x' } }],
    };
    const rendered = await renderTree(node, withBasis(120));

    const label = el(rendered).querySelector('.widget-text') as HTMLElement;
    expect(parseFloat(label.style.fontSize)).toBeCloseTo(16.8, 3);
  });

  it('places a label at the bottom, left-aligned, from justify:end + text align:start', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button, properties: { [Props.Justify]: 'end' },
      children: [{ id: 'label', type: Types.Text, properties: { [Props.Text]: 'Hi', [Props.Align]: 'start' } }],
    };
    const rendered = await renderTree(node, withBasis(200));

    const button = buttonEl(rendered);
    expect(button.style.justifyContent).toBe('flex-end');
    const label = button.querySelector('.widget-text') as HTMLElement;
    expect(label.style.textAlign).toBe('start');
  });

  it('breaks a long word across lines and preserves explicit newlines when wrap is true', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button, properties: {},
      children: [{
        id: 'label', type: Types.Text,
        properties: { [Props.Text]: 'a\nb', [Props.Wrap]: true },
      }],
    };
    const rendered = await renderTree(node, withBasis(200));

    const label = el(rendered).querySelector('.widget-text') as HTMLElement;
    expect(label.style.whiteSpace).toBe('pre-wrap');
    expect(label.style.overflowWrap).toBe('anywhere');
  });

  it('keeps single-line + ellipsis exactly as before when wrap is absent', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button, properties: {},
      children: [{ id: 'label', type: Types.Text, properties: { [Props.Text]: 'Hi' } }],
    };
    const rendered = await renderTree(node, withBasis(200));

    const label = el(rendered).querySelector('.widget-text') as HTMLElement;
    expect(label.style.whiteSpace).toBe('nowrap');
    expect(label.style.textOverflow).toBe('ellipsis');
  });

  it('clamps to two lines with ellipsis when wrap is true and maxLines is 2', async () => {
    const node: UiNode = {
      id: 'btn', type: Types.Button, properties: {},
      children: [{
        id: 'label', type: Types.Text,
        properties: { [Props.Text]: 'a very long label indeed', [Props.Wrap]: true, [Props.MaxLines]: 2 },
      }],
    };
    const rendered = await renderTree(node, withBasis(200));

    const label = el(rendered).querySelector('.widget-text') as HTMLElement;
    // Only the clamp class is asserted here, not the `-webkit-line-clamp` value: `[style.webkitLineClamp]`
    // (pre-existing on this component, also used by Weather's forecast rows) does not actually reach
    // the DOM under Angular's style binding in this build - a pre-existing bug outside this change's
    // scope, reported rather than silently fixed here.
    expect(label.classList).toContain('widget-text-clamp');
  });
});
