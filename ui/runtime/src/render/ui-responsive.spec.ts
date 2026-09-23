import { UiNode } from '../ui-framework/ui-node.interface';
import { renderUiNode } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';
import { activationClaim, treeClaimsGesture } from '../ui-framework/node-gestures';
import { effectiveTreeRoot } from '../ui-framework/responsive';

function host(): UiRenderHost {
  return {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: () => null,
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  };
}

function text(id: string, size = 0.1): UiNode {
  return { id, type: 'ui.text', properties: { text: id, size: { basis: size } } };
}

function responsive(id: string, variants: unknown[], children: UiNode[], properties: Record<string, unknown> = {}): UiNode {
  return { id, type: 'ui.responsive', properties: { variants, ...properties }, children };
}

function drawn(container: Element, responsiveId: string): string[] {
  const element = container.querySelector(`[data-node-id="${responsiveId}"]`)!;
  return Array.from(element.children)
    .map(child => child.getAttribute('data-node-id'))
    .filter((id): id is string => id !== null);
}

describe('ui.responsive', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const weather = responsive('weather', [{ minWidth: 1.5 }, { maxAspect: 0.67 }], [
    text('weather.compact'),
    text('weather.wide'),
    text('weather.tall'),
  ]);

  it('draws the layout for the box it is given and switches when a relayout hands it another', () => {
    const handle = renderUiNode(container, weather, { width: 120, height: 120 }, null, 120, host());
    const square = drawn(container, 'weather');

    handle.update(weather, { width: 252, height: 120 }, null, 120);
    const wide = drawn(container, 'weather');

    handle.update(weather, { width: 120, height: 252 }, null, 120);
    const tall = drawn(container, 'weather');

    expect(square).toEqual(['weather.compact']);
    expect(wide).toEqual(['weather.wide']);
    expect(tall).toEqual(['weather.tall']);
    expect(container.querySelector('[data-node-id="weather.compact"]')).toBeNull();
  });

  it('draws the chosen layout across its whole box', () => {
    const card = responsive('card', [{ minWidth: 1.5 }], [
      { id: 'card.compact', type: 'ui.stack', properties: {}, children: [] },
      { id: 'card.wide', type: 'ui.stack', properties: {}, children: [] },
    ]);
    renderUiNode(container, card, { width: 252, height: 120 }, null, 120, host());

    const chosen = container.querySelector('[data-node-id="card.wide"]') as HTMLElement;

    expect(chosen.style.width).toBe('252px');
    expect(chosen.style.height).toBe('120px');
  });

  it('paints a layout at the root of the tree exactly as it paints that layout alone', () => {
    const button: UiNode = { id: 'play', type: 'ui.button', properties: { events: ['press'] }, children: [text('play.label')] };
    const bare = document.createElement('div');
    document.body.appendChild(bare);
    renderUiNode(bare, button, { width: 120, height: 120 }, null, 120, host());
    renderUiNode(container, responsive('root', [], [button]), { width: 120, height: 120 }, null, 120, host());

    const alone = bare.querySelector('[data-node-id="play"]')!.className;
    const wrapped = container.querySelector('[data-node-id="play"]')!.className;
    bare.remove();

    expect(wrapped).toBe(alone);
    expect(wrapped).toContain('widget-node-root');
  });

  it('reserves the space in a stack that the layout it draws needs', () => {
    const small = text('small', 0.1);
    const large = text('large', 0.3);
    const filler = (): UiNode => ({ id: 'filler', type: 'ui.stack', properties: { fill: true }, children: [] });
    const stackOf = (child: UiNode): UiNode => ({ id: 'stack', type: 'ui.stack', properties: {}, children: [child, filler()] });
    const probe = document.createElement('div');
    document.body.appendChild(probe);
    renderUiNode(probe, stackOf(large), { width: 252, height: 120 }, null, 120, host());
    renderUiNode(container, stackOf(responsive('r', [{ minWidth: 1.5 }], [small, large])), { width: 252, height: 120 }, null, 120, host());
    const left = (root: Element): string => (root.querySelector('[data-node-id="filler"]') as HTMLElement).style.height;

    const expected = left(probe);
    const reserved = left(container);
    probe.remove();

    expect(expected).not.toBe('');
    expect(expected).not.toBe('120px');
    expect(reserved).toBe(expected);
  });

  describe('tile-level claims', () => {
    const tile = responsive('tile', [{ minWidth: 1.5 }], [
      text('tile.compact'),
      { id: 'tile.wide', type: 'ui.button', properties: { events: ['press'] }, children: [] },
    ]);

    it('leave a one-cell tile its own press when only the wide layout has a control', () => {
      expect(treeClaimsGesture(tile, { width: 120, height: 120 })).toBeFalse();
      expect(activationClaim(tile, { width: 120, height: 120 })).toBe('none');
    });

    it('go to the control the wide layout shows on a two-cell tile', () => {
      expect(treeClaimsGesture(tile, { width: 252, height: 120 })).toBeTrue();
      expect(activationClaim(tile, { width: 252, height: 120 })).toEqual({ node: tile.children![1] });
    });

    it('pass through a background wrapper but count only the default of a layout nested deeper', () => {
      const wrapped: UiNode = { id: 'bg', type: 'ui.modifier', properties: { opacity: 0.9 }, children: [tile] };
      const padded: UiNode = { id: 'pad', type: 'ui.modifier', properties: { padding: { basis: 0.05 } }, children: [tile] };
      const nested: UiNode = { id: 'stack', type: 'ui.stack', properties: {}, children: [tile] };

      expect(treeClaimsGesture(wrapped, { width: 252, height: 120 })).toBeTrue();
      expect(treeClaimsGesture({ id: 'turn', type: 'ui.transform', properties: { rotation: 5 }, children: [tile] },
        { width: 252, height: 120 })).toBeTrue();
      expect(treeClaimsGesture(padded, { width: 252, height: 120 })).toBeFalse();
      expect(treeClaimsGesture(nested, { width: 252, height: 120 })).toBeFalse();
    });

    it('treat the drawn layout of a root ui.responsive as the root a tile press lights up', () => {
      expect(effectiveTreeRoot(tile, { width: 252, height: 120 })?.id).toBe('tile.wide');
      expect(effectiveTreeRoot(tile, null)?.id).toBe('tile.compact');
    });
  });
});
