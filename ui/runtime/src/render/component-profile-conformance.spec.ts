import { UiNode } from '../ui-framework/ui-node.interface';
import { renderUiNode } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';
import { PRESS_FEEDBACK_MIN_VISIBLE_MS } from './press-feedback';
import { DEFAULT_WIDGET_BORDER_COLOR, WIDGET_BORDER_WIDTH } from '../domain/widget.interface';

interface UiTreeFixture {
  root: UiNode;
}

interface LayoutCase {
  basis: number;
  tile: { width: number; height: number };
  nodes: Record<string, Record<string, unknown>>;
}

interface LayoutFixture {
  cases: LayoutCase[];
}

declare const __loadWidgetProfileFixture: (name: string) => unknown;

function loadJson<T>(name: string): T {
  return __loadWidgetProfileFixture(name) as T;
}

function loadTree(name: string): UiNode {
  return loadJson<UiTreeFixture>(name).root;
}

function testHost(overrides: Partial<UiRenderHost> = {}): UiRenderHost {
  return {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: resource => (resource ? `/api/ui/resources/${resource.resourceId}` : null),
    now: () => Date.parse('2026-01-02T03:04:05.000Z'),
    culture: () => 'en-US',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
    ...overrides,
  };
}

const UNCHECKED_KEYS = new Set([
  'minFontSize', 'offsetY', 'pressTintAlpha', 'pressFadeInMs', 'pressFadeOutMs',
  'pressSurfaceWidth', 'pressSurfaceHeight', 'lineOpacity', 'areaOpacity',
  'note', 'alignNote', 'bandNote', 'binds', 'paddingBinds', 'gapBinds', 'strokeWidthBinds', 'minWidth',
  // A button's own main-axis extent (height, under the vertical root every fixture nests it in) is
  // resolved to `null` by layoutStackChildren() whenever the node declares neither `mainSize` nor
  // `fill` - "not determined by the parent" in that function's own doc comment, left to the browser's
  // content-based flex sizing. None of the four action-button fixture's top-level buttons declares
  // either, so their own `height`/`contentHeight` (and anything derived from them - a backdrop's
  // height and vertical offset) are real in a browser but unset in jsdom, which lays nothing out.
  // width/contentWidth/backdropWidth/backdropOffsetX (the cross axis) are resolved numbers regardless
  // and are asserted normally.
  'backdropHeight', 'backdropOffsetY',
]);

const ASSERTED_KEYS = new Set([
  'width', 'height', 'contentWidth', 'contentHeight', 'padding', 'gap', 'fontSize', 'role', 'color',
  'size', 'mainSize', 'mainSizeOverFontSize', 'align', 'digits', 'tabular',
  'direction', 'thickness', 'trackLength', 'trackThickness', 'cornerRadius', 'fillLength', 'fillColor',
  'interactive', 'interactiveWidth', 'interactiveHeight', 'thumbRadius', 'thumbRingWidth', 'thumbCentre',
  'fillPainted', 'fillFrom', 'fillLengthAtAnchor', 'fillLengthAt30s',
  'fillStart', 'fillEnd', 'marker',
  'background', 'backdropFit', 'backdropWidth', 'backdropOffsetX',
  'backdropOpacity', 'ringStyle', 'ringColor', 'ringWidth', 'pressMinVisibleMs',
  'fontSize', 'lines', 'textAlign', 'blockAlign',
  'geometry', 'centre', 'secondHand',
  'strokeWidth', 'line', 'area', 'draws', 'paintOrder', 'childBox', 'band',
  'imageEdge', 'transition', 'artworkFit', 'artworkOpacity',
  'secondsFontSize', 'secondsRole',
  'handColor', 'tickColor',
  'renders',
  'transform', 'transformOrigin',
]);

const ROLE_COLOR: Record<string, string> = {
  primary: 'var(--color-text-primary)',
  secondary: 'var(--color-text-secondary)',
  muted: 'var(--color-text-muted)',
};

const ALIGN_TO_ITEMS: Record<string, string> = {
  start: 'flex-start',
  center: 'center',
  end: 'flex-end',
  baseline: 'baseline',
  stretch: 'stretch',
};

function num(style: string): number {
  return parseFloat(style);
}

function rgbNotation(hex: string): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgb(${r}, ${g}, ${b})`;
}

function sameColor(actual: string, expectedHex: string): boolean {
  return actual === expectedHex || actual === rgbNotation(expectedHex);
}

function containsColor(actual: string, expectedHex: string): boolean {
  return actual.includes(expectedHex) || actual.includes(rgbNotation(expectedHex));
}

let container: HTMLElement;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
});

afterEach(() => container.remove());

function mount(root: UiNode, tile: { width: number; height: number }, basis: number, host = testHost()) {
  return renderUiNode(container, root, tile, null, basis, host);
}

function byId(id: string): HTMLElement {
  const element = container.querySelector(`[data-node-id="${id}"]`);
  if (element === null) throw new Error(`no element with data-node-id="${id}"`);
  return element as HTMLElement;
}

function has(id: string): boolean {
  return container.querySelector(`[data-node-id="${id}"]`) !== null;
}

function assertCommon(id: string, spec: Record<string, unknown>): void {
  if ('renders' in spec) {
    // The unsupported node itself never gets an element - resolution jumps straight to whichever
    // fallback negotiated, which is checked separately by asserting that id's own fixture entry.
    expect(has(id)).withContext(`${id} (an unsupported node) should not itself be mounted`).toBeFalse();
    return;
  }

  const el = byId(id);

  if ('width' in spec) {
    expect(num(el.style.width)).withContext(`${id}.width`).toBeCloseTo(spec.width as number, 2);
  }
  if ('height' in spec) {
    expect(num(el.style.height)).withContext(`${id}.height`).toBeCloseTo(spec.height as number, 2);
  }
  if ('padding' in spec) {
    expect(num(el.style.padding)).withContext(`${id}.padding`).toBeCloseTo(spec.padding as number, 2);
  }
  if ('gap' in spec) {
    expect(num(el.style.gap)).withContext(`${id}.gap`).toBeCloseTo(spec.gap as number, 2);
  }
  if ('contentWidth' in spec) {
    const padding = num(el.style.padding) || 0;
    expect(num(el.style.width) - 2 * padding)
      .withContext(`${id}.contentWidth`).toBeCloseTo(spec.contentWidth as number, 2);
  }
  if ('contentHeight' in spec) {
    const padding = num(el.style.padding) || 0;
    expect(num(el.style.height) - 2 * padding)
      .withContext(`${id}.contentHeight`).toBeCloseTo(spec.contentHeight as number, 2);
  }
  if ('size' in spec || 'imageEdge' in spec) {
    // A ui.image's own root div is sized to the box its parent's layout gave it (its "width"/
    // "height" above, constrained on the cross axis by the row/column it sits in) - "size"/"imageEdge"
    // is what the drawn <img> inside is sized to instead, set directly on the <img> by repaintArtwork.
    const value = (spec.size ?? spec.imageEdge) as number;
    const image = el.querySelector('img') as HTMLElement;
    expect(num(image.style.width)).withContext(`${id}.size/imageEdge(width)`).toBeCloseTo(value, 2);
    expect(num(image.style.height)).withContext(`${id}.size/imageEdge(height)`).toBeCloseTo(value, 2);
  }
  if ('fontSize' in spec) {
    expect(num(el.style.fontSize)).withContext(`${id}.fontSize`).toBeCloseTo(spec.fontSize as number, 2);
  }
  if ('mainSize' in spec) {
    // Every fixture using this key places the node in a horizontal row, where main axis == width.
    expect(num(el.style.width)).withContext(`${id}.mainSize`).toBeCloseTo(spec.mainSize as number, 2);
  }
  if ('mainSizeOverFontSize' in spec) {
    expect(num(el.style.width) / num(el.style.fontSize))
      .withContext(`${id}.mainSizeOverFontSize`).toBeCloseTo(spec.mainSizeOverFontSize as number, 2);
  }
  if ('role' in spec && !('color' in spec)) {
    // textFillColor() lets a literal `color` override the role, so a node carrying both (the slider
    // header's label) is checked by the `color` branch below instead - `role` there is informational.
    expect(el.style.color).withContext(`${id}.role`).toBe(ROLE_COLOR[spec.role as string]);
  }
  if ('color' in spec) {
    expect(sameColor(el.style.color, spec.color as string)).withContext(`${id}.color`).toBeTrue();
  }
  if ('align' in spec) {
    expect(el.style.alignItems).withContext(`${id}.align`)
      .toBe(ALIGN_TO_ITEMS[spec.align as string]);
  }
  if ('digits' in spec) {
    expect(el.style.minWidth).withContext(`${id}.digits`).toBe(`${spec.digits}ch`);
  }
  if ('tabular' in spec) {
    expect(el.classList.contains('widget-text-tabular')).withContext(`${id}.tabular`).toBe(spec.tabular as boolean);
  }
  if ('transition' in spec) {
    const eased = el.classList.contains('widget-artwork-eased');
    expect(eased).withContext(`${id}.transition`).toBe(spec.transition === 'crossfade');
  }
  if ('renders' in spec) {
    // The unsupported node itself never gets an element - resolution jumps straight to whichever
    // fallback negotiated, which is checked separately by asserting that id's own fixture entry.
    expect(has(id)).withContext(`${id} (an unsupported node) should not itself be mounted`).toBeFalse();
  }
}

describe('component-profile conformance fixtures: weather tree', () => {
  const tree = loadTree('conformance-tree.json');
  const layout = loadJson<LayoutFixture>('conformance-layout.json');
  const BAR_IDS = new Set(['conformance.rows.2026-07-20.bar', 'conformance.rows.2026-07-21.bar']);

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      for (const id of Object.keys(testCase.nodes)) {
        it(`resolves ${id}`, () => {
          const spec = testCase.nodes[id];
          if (BAR_IDS.has(id)) {
            const el = byId(id);
            const track = el.querySelector('.widget-range-bar-track') as HTMLElement;
            const fill = el.querySelector('.widget-range-bar-fill') as HTMLElement;
            expect(num(el.style.width)).withContext(`${id}.width`).toBeCloseTo(spec.width as number, 2);
            expect(num(track.style.height)).withContext(`${id}.thickness`).toBeCloseTo(spec.thickness as number, 2);
            expect(num(fill.style.left)).withContext(`${id}.fillStart`)
              .toBeCloseTo((spec.fillStart as number) * 100, 2);
            expect(num(fill.style.width)).withContext(`${id}.fillEnd`)
              .toBeCloseTo(((spec.fillEnd as number) - (spec.fillStart as number)) * 100, 2);
            const marker = el.querySelector('.widget-range-bar-marker');
            if (spec.marker === null) {
              expect(marker).withContext(`${id}.marker (absent)`).toBeNull();
            } else {
              expect(marker).withContext(`${id}.marker (present)`).not.toBeNull();
            }
            return;
          }
          assertCommon(id, spec);
        });
      }
    });
  }
});

describe('component-profile conformance fixtures: clock tree', () => {
  const tree = loadTree('conformance-clock-tree.json');
  const layout = loadJson<LayoutFixture & {
    referenceInstant: { hourAngle: number; minuteAngle: number; secondAngle: number };
  }>('conformance-clock-layout.json');

  function dialMetrics(el: SVGElement) {
    return {
      width: Number(el.getAttribute('width')),
      height: Number(el.getAttribute('height')),
    };
  }

  function radiusOf(cx: number, cy: number, x: number, y: number): number {
    return Math.hypot(x - cx, y - cy);
  }

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      for (const id of Object.keys(testCase.nodes)) {
        const spec = testCase.nodes[id];

        if (id === 'conformance.dial' || id === 'conformance.dialPlain') {
          it(`resolves the dial geometry of ${id}`, () => {
            const el = byId(id) as unknown as SVGElement;
            const { width, height } = dialMetrics(el);
            expect(width).withContext(`${id}.width`).toBeCloseTo(spec.width as number, 2);
            expect(height).withContext(`${id}.height`).toBeCloseTo(spec.height as number, 2);

            const face = el.querySelector('.widget-clock-dial-face') as SVGCircleElement;
            const cx = Number(face.getAttribute('cx'));
            const cy = Number(face.getAttribute('cy'));
            const geometry = spec.geometry as Record<string, number>;
            expect(Math.min(width, height)).withContext(`${id}.geometry.edge`).toBeCloseTo(geometry.edge, 2);
            expect(Number(face.getAttribute('r'))).withContext(`${id}.geometry.faceRadius`)
              .toBeCloseTo(geometry.faceRadius, 2);

            const ticks = el.querySelectorAll('.widget-clock-dial-tick');
            expect(ticks.length).withContext(`${id} tick count`).toBe(12);
            const major = ticks[0] as SVGLineElement; // index 0: major (index % 3 === 0)
            const minor = ticks[1] as SVGLineElement; // index 1: minor
            expect(radiusOf(cx, cy, Number(major.getAttribute('x1')), Number(major.getAttribute('y1'))))
              .withContext(`${id}.geometry.majorTickInnerRadius`).toBeCloseTo(geometry.majorTickInnerRadius, 2);
            expect(radiusOf(cx, cy, Number(major.getAttribute('x2')), Number(major.getAttribute('y2'))))
              .withContext(`${id}.geometry.tickOuterRadius`).toBeCloseTo(geometry.tickOuterRadius, 2);
            expect(Number(major.getAttribute('stroke-width')))
              .withContext(`${id}.geometry.majorTickStroke`).toBeCloseTo(geometry.majorTickStroke, 2);
            expect(radiusOf(cx, cy, Number(minor.getAttribute('x1')), Number(minor.getAttribute('y1'))))
              .withContext(`${id}.geometry.minorTickInnerRadius`).toBeCloseTo(geometry.minorTickInnerRadius, 2);
            expect(Number(minor.getAttribute('stroke-width')))
              .withContext(`${id}.geometry.minorTickStroke`).toBeCloseTo(geometry.minorTickStroke, 2);

            const hour = el.querySelector('.widget-clock-dial-hand-hour') as SVGLineElement;
            const minute = el.querySelector('.widget-clock-dial-hand-minute') as SVGLineElement;
            expect(radiusOf(cx, cy, Number(hour.getAttribute('x1')), Number(hour.getAttribute('y1'))))
              .withContext(`${id}.geometry.hourTail`).toBeCloseTo(geometry.hourTail, 2);
            expect(radiusOf(cx, cy, Number(hour.getAttribute('x2')), Number(hour.getAttribute('y2'))))
              .withContext(`${id}.geometry.hourTip`).toBeCloseTo(geometry.hourTip, 2);
            expect(Number(hour.getAttribute('stroke-width')))
              .withContext(`${id}.geometry.hourStroke`).toBeCloseTo(geometry.hourStroke, 2);
            expect(radiusOf(cx, cy, Number(minute.getAttribute('x2')), Number(minute.getAttribute('y2'))))
              .withContext(`${id}.geometry.minuteTip`).toBeCloseTo(geometry.minuteTip, 2);
            expect(Number(minute.getAttribute('stroke-width')))
              .withContext(`${id}.geometry.minuteStroke`).toBeCloseTo(geometry.minuteStroke, 2);

            const hub = el.querySelector('.widget-clock-dial-hub') as SVGCircleElement;
            expect(Number(hub.getAttribute('r'))).withContext(`${id}.geometry.hubRadius`)
              .toBeCloseTo(geometry.hubRadius, 2);

            expect(el.querySelector('.widget-clock-dial-hand-second') !== null)
              .withContext(`${id}.secondHand`).toBe(spec.secondHand as boolean);

            if ('centre' in spec) {
              const centre = spec.centre as { x: number; y: number };
              expect(cx).withContext(`${id}.centre.x`).toBeCloseTo(centre.x, 2);
              expect(cy).withContext(`${id}.centre.y`).toBeCloseTo(centre.y, 2);
            }
          });
          continue;
        }

        it(`resolves ${id}`, () => {
          assertCommon(id, spec);
          if ('secondsFontSize' in spec || 'secondsRole' in spec) {
            const seconds = byId(id).querySelector('.widget-dynamic-text-seconds') as HTMLElement;
            if ('secondsFontSize' in spec) {
              expect(num(seconds.style.fontSize)).withContext(`${id}.secondsFontSize`)
                .toBeCloseTo(spec.secondsFontSize as number, 2);
            }
            if ('secondsRole' in spec) {
              expect(seconds.style.color).withContext(`${id}.secondsRole`)
                .toBe(ROLE_COLOR[spec.secondsRole as string]);
            }
          }
        });
      }
    });
  }

  it('points the hands at the fixture\'s named instant (10:09:30 America/New_York)', () => {
    // 2026-01-15 is outside US daylight saving, so America/New_York is a fixed UTC-5 and this instant
    // is unambiguous regardless of the machine running the suite.
    const host = testHost({ now: () => Date.parse('2026-01-15T15:09:30.000Z') });
    mount(tree, { width: 240, height: 480 }, 240, host);

    const el = byId('conformance.dial') as unknown as SVGElement;
    const face = el.querySelector('.widget-clock-dial-face') as SVGCircleElement;
    const cx = Number(face.getAttribute('cx'));
    const cy = Number(face.getAttribute('cy'));

    const angleOf = (selector: string, tail: number): number => {
      const hand = el.querySelector(selector) as SVGLineElement;
      const x2 = Number(hand.getAttribute('x2'));
      const y2 = Number(hand.getAttribute('y2'));
      // Degrees clockwise from twelve, matching dialAngles()'s own convention.
      const degrees = (Math.atan2(x2 - cx, cy - y2) * 180) / Math.PI;
      return ((degrees % 360) + 360) % 360;
    };

    expect(angleOf('.widget-clock-dial-hand-hour', 0)).toBeCloseTo(layout.referenceInstant.hourAngle, 1);
    expect(angleOf('.widget-clock-dial-hand-minute', 0)).toBeCloseTo(layout.referenceInstant.minuteAngle, 1);
    expect(angleOf('.widget-clock-dial-hand-second', 0)).toBeCloseTo(layout.referenceInstant.secondAngle, 1);
  });
});

describe('component-profile conformance fixtures: slider tree', () => {
  const tree = loadTree('conformance-slider-tree.json');
  const layout = loadJson<LayoutFixture>('conformance-slider-layout.json');
  const SLIDER_IDS = new Set([
    'conformance.track', 'conformance.column', 'conformance.plain', 'conformance.empty',
  ]);
  // These two declare neither `mainSize` nor `fill`, so - exactly like the action-button fixture's
  // buttons - their own height (the root's main axis) is left to content-based flex sizing that jsdom
  // never computes; `conformance.track` (fill) and `conformance.column` (mainSize) both resolve to a
  // real number and are checked in full.
  const INTRINSIC_HEIGHT_SLIDER_IDS = new Set(['conformance.plain', 'conformance.empty']);

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      for (const id of Object.keys(testCase.nodes)) {
        const spec = testCase.nodes[id];

        if (!SLIDER_IDS.has(id)) {
          // conformance.header declares neither mainSize nor fill either, so its own height is the
          // same kind of jsdom-unobservable content-based extent - see INTRINSIC_HEIGHT_SLIDER_IDS.
          const { height: _height, ...rest } = spec;
          it(`resolves ${id}`, () => assertCommon(id, id === 'conformance.header' ? rest : spec));
          continue;
        }

        it(`resolves the geometry of ${id}`, () => {
          const el = byId(id);
          const vertical = el.classList.contains('widget-slider-vertical');
          const heightIsIntrinsic = INTRINSIC_HEIGHT_SLIDER_IDS.has(id);
          expect(vertical ? 'vertical' : 'horizontal').withContext(`${id}.direction`).toBe(spec.direction as string);
          expect(num(el.style.width)).withContext(`${id}.width/interactiveWidth`)
            .toBeCloseTo(spec.width as number, 2);
          expect(num(el.style.width)).withContext(`${id}.interactiveWidth`)
            .toBeCloseTo(spec.interactiveWidth as number, 2);
          if (!heightIsIntrinsic) {
            expect(num(el.style.height)).withContext(`${id}.height/interactiveHeight`)
              .toBeCloseTo(spec.height as number, 2);
            expect(num(el.style.height)).withContext(`${id}.interactiveHeight`)
              .toBeCloseTo(spec.interactiveHeight as number, 2);
          }

          const trackLength = vertical ? num(el.style.height) : num(el.style.width);
          expect(trackLength).withContext(`${id}.trackLength`).toBeCloseTo(spec.trackLength as number, 2);

          const track = el.querySelector('.widget-slider-track') as HTMLElement;
          const trackThickness = num(vertical ? track.style.width : track.style.height);
          expect(trackThickness).withContext(`${id}.trackThickness`).toBeCloseTo(spec.trackThickness as number, 2);
          expect(num(track.style.borderRadius)).withContext(`${id}.cornerRadius`)
            .toBeCloseTo(spec.cornerRadius as number, 2);

          const fill = el.querySelector('.widget-slider-fill') as HTMLElement;
          const fillFraction = num(vertical ? fill.style.height : fill.style.width) / 100;
          expect(fillFraction * trackLength).withContext(`${id}.fillLength`)
            .toBeCloseTo(spec.fillLength as number, 2);

          if ('fillColor' in spec) {
            if (spec.fillColor === 'reader-accent') {
              expect(fill.style.background).withContext(`${id}.fillColor`).toContain('--color-accent');
            } else {
              expect(sameColor(fill.style.background, spec.fillColor as string))
                .withContext(`${id}.fillColor`).toBeTrue();
            }
          }

          expect(el.classList.contains('widget-pressable')).withContext(`${id}.interactive`)
            .toBe(spec.interactive as boolean);

          const thumb = el.querySelector('.widget-slider-thumb') as HTMLElement;
          // jsdom's CSSOM does not expand the `border` shorthand into `border-width` when the colour
          // is a `var(...)` reference (as it always is here), so `.borderWidth` reads back empty -
          // parsed off the shorthand string itself instead.
          const strokeMatch = /^([\d.]+)px/.exec(thumb.style.border);
          if (strokeMatch === null) throw new Error(`unparseable thumb border on ${id}: ${thumb.style.border}`);
          const stroke = Number(strokeMatch[1]);
          expect(stroke).withContext(`${id}.thumbRingWidth`).toBeCloseTo(spec.thumbRingWidth as number, 2);

          // The DOM carries the thumb's leading edge, not its centre: sliderThumbOffsetPx() pulls the
          // centre back by the element's own radius and stroke so the border box lands where the
          // centre should be. Adding both back is what recovers the centre the fixture pins.
          const offsetStyle = vertical ? thumb.style.bottom : thumb.style.left;
          const offset = num(offsetStyle);
          const diameter = num(thumb.style.width);
          const centre = offset + diameter / 2 + stroke;
          expect(centre).withContext(`${id}.thumbCentre`).toBeCloseTo(spec.thumbCentre as number, 2);
        });

        it(`draws the thumb at the pinned radius on ${id}`, () => {
          // The ring is a CSS border on a content-box element, so three radii exist and only one of
          // them is `thumbRadius`: the fixture pins the ring's centre line, which is what its own
          // inset rule refers to when it says the centre sits `radius + ringWidth / 2` from each end.
          // Measuring the content box instead would read a disc a stroke narrower and call it wrong.
          const el = byId(id);
          const thumb = el.querySelector('.widget-slider-thumb') as HTMLElement;
          // Same shorthand-parsing reason as above: jsdom will not give back `border-width` here.
          const ringMatch = /^([\d.]+)px/.exec(thumb.style.border);
          if (ringMatch === null) throw new Error(`unparseable thumb border on ${id}: ${thumb.style.border}`);
          const centreLineRadius = num(thumb.style.width) / 2 + Number(ringMatch[1]) / 2;

          expect(centreLineRadius).toBeCloseTo(spec.thumbRadius as number, 2);
        });
      }
    });
  }
});

describe('component-profile conformance fixtures: action-button tree', () => {
  const tree = loadTree('conformance-action-button-tree.json');
  const layout = loadJson<LayoutFixture>('conformance-action-button-layout.json');
  const LABEL_ID: Record<string, string> = {
    'conformance.full': 'conformance.full.title',
    'conformance.press': 'conformance.press.label',
    'conformance.boundary': 'conformance.boundary.label',
    'conformance.silent': 'conformance.silent.label',
  };

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      for (const id of Object.keys(testCase.nodes)) {
        const spec = testCase.nodes[id];
        if (id === 'conformance') {
          it('resolves the root stack', () => assertCommon(id, spec));
          continue;
        }

        it(`resolves the box, background and label of ${id}`, () => {
          // fontSize on a button's own fixture entry describes its label's font, not the button's own
          // (buttons carry no font-size of their own) - assertCommon() below is for the button's box.
          //
          // height/contentHeight: none of the four buttons here declares `mainSize` or `fill`, so each
          // one's own main-axis extent (height, since the root they sit in is a vertical stack) is left
          // to the browser's own content-based flex sizing rather than resolved to a number by this
          // renderer - layoutStackChildren() hands such a child `box.height: null` on purpose (see its
          // own doc comment: "not determined by the parent"). jsdom never lays anything out, so that
          // stays unset here; width/contentWidth (the cross axis) come from the root's own content
          // width instead, which *is* a resolved number, and are asserted normally.
          const { fontSize: _labelFontSize, height: _height, contentHeight: _contentHeight, ...boxSpec } = spec;
          assertCommon(id, boxSpec);
          const el = byId(id);

          if ('background' in spec) {
            if (spec.background === 'reader-accent') {
              expect(el.style.background).withContext(`${id}.background`).toContain('--color-accent');
            } else {
              expect(sameColor(el.style.background, spec.background as string))
                .withContext(`${id}.background`).toBeTrue();
            }
          }

          const labelId = LABEL_ID[id];
          const label = byId(labelId);
          if ('fontSize' in spec) {
            expect(num(label.style.fontSize)).withContext(`${id}.fontSize (label)`)
              .toBeCloseTo(spec.fontSize as number, 2);
          }
          if ('lines' in spec) {
            const clamped = label.classList.contains('widget-text-clamp');
            expect(clamped).withContext(`${id}.lines (clamp class)`).toBe((spec.lines as number) > 1);
          }
          if ('textAlign' in spec) {
            expect(label.style.textAlign).withContext(`${id}.textAlign`).toBe(spec.textAlign as string);
          }
          if ('blockAlign' in spec) {
            expect(el.style.alignItems).withContext(`${id}.blockAlign`)
              .toBe(ALIGN_TO_ITEMS[spec.blockAlign as string]);
          }
        });

        if ('backdropFit' in spec) {
          it(`resolves the backdrop rect of ${id}`, () => {
            const el = byId(id);
            const image = el.querySelector('.widget-button-artwork') as HTMLImageElement;
            expect(image.style.objectFit).withContext(`${id}.backdropFit`).toBe(spec.backdropFit as string);
            expect(num(image.style.opacity)).withContext(`${id}.backdropOpacity`)
              .toBeCloseTo(spec.backdropOpacity as number, 2);

            const match = /translate\(([-\d.]+)%,\s*([-\d.]+)%\)\s*scale\(([\d.]+)\)/.exec(image.style.transform);
            if (match === null) throw new Error(`unparseable transform on ${id}: ${image.style.transform}`);
            const [, offsetXPct, offsetYPct, zoom] = match.map(Number as unknown as (s: string) => number);

            const ownWidth = num(el.style.width);
            expect(ownWidth * zoom).withContext(`${id}.backdropWidth`).toBeCloseTo(spec.backdropWidth as number, 2);
            expect((offsetXPct / 100) * ownWidth).withContext(`${id}.backdropOffsetX`)
              .toBeCloseTo(spec.backdropOffsetX as number, 2);
            // backdropHeight/backdropOffsetY need the button's own resolved height, which - like
            // height/contentHeight above - is left to content-based flex sizing this renderer never
            // computes as a number, so jsdom has nothing to read it back from. Not asserted here.
          });
        }

        const ringWidthAtDeckScale = (overlay: HTMLElement, deckScale: number): number => {
          const declared = overlay.style.getPropertyValue('--wb-width');
          const divided = /^calc\(([\d.]+)px \/ var\(--deck-scale, 1\)\)$/.exec(declared);
          if (divided !== null) return Number(divided[1]) / deckScale;
          const plain = /^([\d.]+)px$/.exec(declared);
          expect(plain).withContext(`unreadable --wb-width: ${declared}`).not.toBeNull();
          return Number(plain?.[1]);
        };

        it(`resolves the ring and press feedback of ${id}`, () => {
          const el = byId(id);
          const overlay = el.querySelector('.widget-button-ring') as HTMLElement | null;

          if (spec.ringStyle === null) {
            expect(overlay).withContext(`${id}.ringStyle (none)`).toBeNull();
          } else {
            expect(overlay).withContext(`${id}.ringStyle (present)`).not.toBeNull();
            const ring = overlay!.querySelector('.ring') as HTMLElement;
            expect(ring.classList.contains(`wb-${spec.ringStyle}`)).withContext(`${id}.ringStyle`).toBeTrue();
            // The ring lives inside the tile content the deck scale is applied to, so it declares a
            // width that divides that scale back out. These fixtures are measured in reference
            // pixels - deck scale 1 - which is what the declaration has to come out as here.
            expect(ringWidthAtDeckScale(overlay!, 1)).withContext(`${id}.ringWidth`)
              .toBeCloseTo(WIDGET_BORDER_WIDTH, 6);
            expect(spec.ringWidth as number).withContext(`${id}.ringWidth (fixture constant)`)
              .toBe(WIDGET_BORDER_WIDTH);

            const expectedColor = spec.ringColor === 'reader-cycled'
              // "reader-cycled" is the hue-shift ring with no borderColor: resolveWidgetBorder() falls
              // back to this static base colour, which a stylesheet animation then hue-rotates - the
              // JS side of the contract is the base colour, not the animation.
              ? DEFAULT_WIDGET_BORDER_COLOR
              : spec.ringColor as string;
            expect(ring.style.getPropertyValue('--wb-color')).withContext(`${id}.ringColor`).toBe(expectedColor);
          }

          expect(PRESS_FEEDBACK_MIN_VISIBLE_MS).withContext(`${id}.pressMinVisibleMs`)
            .toBe(spec.pressMinVisibleMs as number);
        });
      }
    });
  }
});

describe('component-profile conformance fixtures: music-player tree', () => {
  const tree = loadTree('conformance-music-player-tree.json');
  const layout = loadJson<LayoutFixture & {
    runs: {
      atAnchor: Record<string, string>;
      at30sAfterAnchor: Record<string, string>;
    };
  }>('conformance-music-player-layout.json');
  const ANCHOR_MS = Date.parse('2026-08-25T12:00:00.000Z');

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      let nowMs: number;
      let handle: ReturnType<typeof mount>;

      beforeEach(() => {
        nowMs = ANCHOR_MS;
        handle = mount(tree, testCase.tile, testCase.basis, testHost({ now: () => nowMs }));
      });

      function retick(): void {
        nowMs = ANCHOR_MS + 30_000;
        handle.update(tree, testCase.tile, null, testCase.basis);
      }

      for (const id of Object.keys(testCase.nodes)) {
        const spec = testCase.nodes[id];

        if (id in layout.runs.atAnchor) {
          it(`formats the run for ${id} at the anchor and 30s after it`, () => {
            expect(byId(id).textContent).withContext(`${id}@anchor`).toBe(layout.runs.atAnchor[id]);
            if ('fontSize' in spec) {
              expect(num(byId(id).style.fontSize)).withContext(`${id}.fontSize`)
                .toBeCloseTo(spec.fontSize as number, 2);
            }
            retick();
            expect(byId(id).textContent).withContext(`${id}@+30s`).toBe(layout.runs.at30sAfterAnchor[id]);
          });
          continue;
        }

        if (id.endsWith('.progress')) {
          it(`resolves the progress geometry of ${id}`, () => {
            const el = byId(id);
            const track = el.querySelector('.widget-range-bar-track') as HTMLElement;
            if ('width' in spec) expect(num(el.style.width)).withContext(`${id}.width`).toBeCloseTo(spec.width as number, 2);
            expect(num(track.style.height)).withContext(`${id}.trackThickness`)
              .toBeCloseTo(spec.trackThickness as number, 2);
            expect(num(track.style.borderRadius)).withContext(`${id}.cornerRadius`)
              .toBeCloseTo(spec.cornerRadius as number, 2);

            const readFillLength = () => {
              const fill = el.querySelector('.widget-progress-fill') as HTMLElement | null;
              if (fill === null) return 0;
              expect(fill.style.left).withContext(`${id}.fillFrom`).toBe('0%');
              return (num(fill.style.width) / 100) * num(el.style.width);
            };
            expect(readFillLength()).withContext(`${id}.fillLengthAtAnchor`)
              .toBeCloseTo(spec.fillLengthAtAnchor as number, 2);

            const fillEl = () => el.querySelector('.widget-progress-fill') as HTMLElement | null;
            if ('fillColor' in spec) {
              const fill = fillEl()!;
              // progressFillStyle() always builds a two-stop gradient, even where start === end colour.
              const matched = spec.fillColor === 'reader-accent'
                ? fill.style.background.includes('--color-accent')
                : containsColor(fill.style.background, spec.fillColor as string);
              expect(matched).withContext(`${id}.fillColor ${fill.style.background}`).toBeTrue();
            }

            retick();
            expect(readFillLength()).withContext(`${id}.fillLengthAt30s`)
              .toBeCloseTo(spec.fillLengthAt30s as number, 2);
          });
          continue;
        }

        it(`resolves ${id}`, () => {
          assertCommon(id, spec);
          const el = byId(id);

          if ('artworkFit' in spec || 'artworkOpacity' in spec) {
            const image = el.querySelector('.widget-button-artwork') as HTMLImageElement;
            if ('artworkFit' in spec) {
              expect(image.style.objectFit).withContext(`${id}.artworkFit`).toBe(spec.artworkFit as string);
            }
            if ('artworkOpacity' in spec) {
              expect(num(image.style.opacity)).withContext(`${id}.artworkOpacity`)
                .toBeCloseTo(spec.artworkOpacity as number, 2);
            }
          }
        });
      }

      it('dims and desaturates the paused cover the way the fixture states', () => {
        const image = byId('conformance.paused.cover').querySelector('img') as HTMLImageElement;
        expect(image.style.filter).toBe('brightness(0.6) saturate(0.55)');
      });
    });
  }
});

describe('component-profile conformance fixtures: history-graph tree', () => {
  const tree = loadTree('conformance-history-graph-tree.json');
  const layout = loadJson<LayoutFixture>('conformance-history-graph-layout.json');

  function parsePathPoints(d: string): Array<{ x: number; y: number }> {
    return d
      .slice(1) // drop the leading "M"
      .split('L')
      .map(segment => {
        const [x, y] = segment.trim().split(/\s+/).map(Number);
        return { x, y };
      });
  }

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      it('paints the layer\'s children in declaration order, furthest back first', () => {
        const root = byId('conformance');
        const order = Array.from(root.children)
          .filter(child => child.hasAttribute('data-node-id'))
          .map(child => child.getAttribute('data-node-id'));
        expect(order).toEqual((testCase.nodes['conformance'].paintOrder as string[]));
      });

      it('sizes the layer to the whole content box', () => {
        const root = byId('conformance');
        const box = testCase.nodes['conformance'].childBox as { width: number; height: number };
        expect(num(root.style.width)).toBeCloseTo(box.width, 2);
        expect(num(root.style.height)).toBeCloseTo(box.height, 2);
      });

      for (const id of Object.keys(testCase.nodes)) {
        if (id === 'conformance') continue;
        const spec = testCase.nodes[id];

        if ('line' in spec) {
          it(`plots the line and area of ${id}`, () => {
            const el = byId(id) as unknown as SVGElement;
            expect(Number(el.getAttribute('width'))).withContext(`${id}.width`).toBeCloseTo(spec.width as number, 2);
            expect(Number(el.getAttribute('height'))).withContext(`${id}.height`)
              .toBeCloseTo(spec.height as number, 2);

            const line = el.querySelector('.widget-chart-line') as SVGPathElement;
            expect(Number(line.getAttribute('stroke-width'))).withContext(`${id}.strokeWidth`)
              .toBeCloseTo(spec.strokeWidth as number, 2);

            const points = parsePathPoints(line.getAttribute('d')!);
            const expected = spec.line as Array<{ x: number; y: number }>;
            expect(points.length).withContext(`${id}.line.length`).toBe(expected.length);
            for (let i = 0; i < expected.length; i++) {
              expect(points[i].x).withContext(`${id}.line[${i}].x`).toBeCloseTo(expected[i].x, 1);
              expect(points[i].y).withContext(`${id}.line[${i}].y`).toBeCloseTo(expected[i].y, 1);
            }

            const area = el.querySelector('.widget-chart-area') as SVGPathElement;
            const areaD = area.getAttribute('d')!;
            const bottom = spec.height as number;
            const right = spec.width as number;
            expect(areaD.startsWith(`M0 ${bottom}`)).withContext(`${id}.area (starts at the foot)`).toBeTrue();
            expect(areaD.endsWith(`${right} ${bottom} Z`)).withContext(`${id}.area (closes at the foot)`).toBeTrue();
          });
          continue;
        }

        if ('draws' in spec) {
          it(`draws nothing at all for the empty series on ${id}`, () => {
            const el = byId(id);
            expect(el.querySelector('.widget-chart-line')).withContext(`${id} line`).toBeNull();
            expect(el.querySelector('.widget-chart-area')).withContext(`${id} area`).toBeNull();
          });
          continue;
        }

        it(`resolves ${id}`, () => assertCommon(id, spec));
      }
    });
  }
});

describe('component-profile conformance fixtures: clock formats tree', () => {
  const tree = loadTree('conformance-clock-formats-tree.json');
  const layout = loadJson<LayoutFixture>('conformance-clock-formats-layout.json');

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      for (const id of Object.keys(testCase.nodes)) {
        const spec = testCase.nodes[id];

        if (id === 'conformance.dialTinted') {
          it(`tints ${id}`, () => {
            const el = byId(id) as unknown as SVGElement;
            expect(Number(el.getAttribute('width'))).withContext(`${id}.width`)
              .toBeCloseTo(spec.width as number, 2);
            expect(Number(el.getAttribute('height'))).withContext(`${id}.height`)
              .toBeCloseTo(spec.height as number, 2);

            // The tint reaches every mark the dial would otherwise draw in a text colour, and stops
            // there: a second hand that followed it would stop reading against the face.
            for (const className of ['widget-clock-dial-hand-hour', 'widget-clock-dial-hand-minute']) {
              const hand = el.querySelector(`.${className}`) as SVGElement;
              expect(sameColor(hand.style.stroke, spec.handColor as string))
                .withContext(`${id}.handColor (${className})`).toBeTrue();
            }
            for (const tick of Array.from(el.querySelectorAll('.widget-clock-dial-tick'))) {
              expect(sameColor((tick as SVGElement).style.stroke, spec.tickColor as string))
                .withContext(`${id}.tickColor`).toBeTrue();
            }

            const second = el.querySelector('.widget-clock-dial-hand-second') as SVGElement;
            expect(second !== null).withContext(`${id}.secondHand`).toBe(spec.secondHand as boolean);
            expect(second.style.stroke).withContext(`${id} second hand keeps the accent`)
              .toBe('var(--color-accent)');
          });
          continue;
        }

        it(`resolves ${id}`, () => {
          if (id === 'conformance') {
            expect(sameColor(byId(id).style.backgroundColor, spec.background as string))
              .withContext(`${id}.background`).toBeTrue();
          }
          assertCommon(id, spec);
        });
      }
    });
  }
});

describe('component-profile conformance fixtures: gauge tree', () => {
  const tree = loadTree('conformance-gauge-tree.json');
  const layout = loadJson<LayoutFixture>('conformance-gauge-layout.json');

  for (const testCase of layout.cases) {
    describe(`basis ${testCase.basis}`, () => {
      beforeEach(() => mount(tree, testCase.tile, testCase.basis));

      for (const [id, spec] of Object.entries(testCase.nodes)) {
        it(`resolves ${id}`, () => {
          const element = byId(id);
          for (const prefix of ['', '-webkit-']) {
            expect(element.style.getPropertyValue(`${prefix}transform`)).withContext(`${id} ${prefix}transform`)
              .toBe((spec.transform as string | null) ?? '');
            expect(element.style.getPropertyValue(`${prefix}transform-origin`))
              .withContext(`${id} ${prefix}transform-origin`).toBe((spec.transformOrigin as string | null) ?? '');
          }

          const box = spec.childBox as { width: number; height: number };
          const children = Array.from(element.children).filter(child => child.hasAttribute('data-node-id'));
          expect(children.length).withContext(`${id} children`).toBeGreaterThan(0);
          for (const child of children as HTMLElement[]) {
            expect(num(child.style.width)).withContext(`${id} child width`).toBeCloseTo(box.width, 2);
            if (child.style.height !== '') {
              expect(num(child.style.height)).withContext(`${id} child height`).toBeCloseTo(box.height, 2);
            }
          }
        });
      }
    });
  }
});

describe('component-profile conformance fixtures: coverage', () => {
  const LAYOUT_FILES = [
    'conformance-layout.json',
    'conformance-clock-layout.json',
    'conformance-clock-formats-layout.json',
    'conformance-slider-layout.json',
    'conformance-action-button-layout.json',
    'conformance-music-player-layout.json',
    'conformance-history-graph-layout.json',
    'conformance-gauge-layout.json',
  ];

  it('records every fixture key exactly once, as asserted or as a documented omission', () => {
    const seen = new Set<string>();
    for (const file of LAYOUT_FILES) {
      const layout = loadJson<LayoutFixture>(file);
      for (const testCase of layout.cases) {
        for (const spec of Object.values(testCase.nodes)) {
          for (const key of Object.keys(spec)) seen.add(key);
        }
      }
    }

    const unhandled = Array.from(seen).filter(key => !ASSERTED_KEYS.has(key) && !UNCHECKED_KEYS.has(key));
    expect(unhandled).withContext(
      'a fixture key appeared that is neither asserted in this file nor listed in UNCHECKED_KEYS - ' +
      'add it to one or the other rather than letting it go unchecked silently').toEqual([]);
  });

  it('lists every tree/layout pair this file exercises', () => {
    // conformance-picker-tree.json has no matching *-layout.json fixture, so it is out of this file's
    // scope - the wire form it pins is still checked by the C# suite.
    const exercised = [
      'conformance-tree.json', 'conformance-clock-tree.json',
      'conformance-clock-formats-tree.json', 'conformance-slider-tree.json',
      'conformance-action-button-tree.json', 'conformance-music-player-tree.json',
      'conformance-history-graph-tree.json', 'conformance-gauge-tree.json',
    ];
    for (const name of exercised) expect(() => loadTree(name)).not.toThrow();
  });
});
