import { UiNode } from '../ui-framework/ui-node.interface';
import { renderUiNode } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';
import { activationClaim } from '../ui-framework/node-gestures';
import { contentFits } from './content-fit';
import { uiFirstFitComponent } from '../ui-components/ui-first-fit.component';

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

function text(id: string): UiNode {
  return { id, type: 'ui.text', properties: { text: id, size: { basis: 0.1 } } };
}

function firstFit(children: UiNode[]): UiNode {
  return { id: 'group', type: 'ui.first-fit', properties: { fill: true }, children };
}

function metrics(element: Element, values: Partial<Record<'scrollWidth' | 'clientWidth' | 'scrollHeight' | 'clientHeight', number>>): void {
  for (const [name, value] of Object.entries(values)) {
    Object.defineProperty(element, name, { configurable: true, get: () => value });
  }
}

function overflowing(element: Element): void {
  metrics(element, { scrollWidth: 200, clientWidth: 100 });
}

function fitting(element: Element): void {
  metrics(element, { scrollWidth: 100, clientWidth: 100, scrollHeight: 20, clientHeight: 20 });
}

describe('ui.first-fit', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const tree = firstFit([text('group.inline'), text('group.mid'), text('group.stacked')]);

  function candidate(id: string): HTMLElement {
    return container.querySelector(`[data-node-id="${id}"]`) as HTMLElement;
  }

  function visible(): string[] {
    return ['group.inline', 'group.mid', 'group.stacked']
      .filter(id => !candidate(id).hasAttribute('data-first-fit-hidden'));
  }

  function relayout(handle: ReturnType<typeof renderUiNode>, width: number): void {
    handle.update(tree, { width, height: 120 }, null, 120);
  }

  it('paints every layout and shows the first one whose content fits', () => {
    const handle = renderUiNode(container, tree, { width: 240, height: 120 }, null, 120, host());

    expect(Array.from(candidate('group').children).length).toBe(3);
    expect(visible()).toEqual(['group.inline']);

    overflowing(candidate('group.inline'));
    relayout(handle, 200);

    expect(visible()).toEqual(['group.mid']);
    expect(candidate('group.inline').getAttribute('aria-hidden')).toBe('true');
    expect(candidate('group.mid').hasAttribute('aria-hidden')).toBeFalse();
  });

  it('draws the last layout when none fits, and goes back when room returns', () => {
    const handle = renderUiNode(container, tree, { width: 240, height: 120 }, null, 120, host());

    overflowing(candidate('group.inline'));
    overflowing(candidate('group.mid'));
    relayout(handle, 100);
    expect(visible()).toEqual(['group.stacked']);

    fitting(candidate('group.inline'));
    relayout(handle, 300);
    expect(visible()).toEqual(['group.inline']);
  });

  it('re-chooses when only the height changes the verdict', () => {
    const handle = renderUiNode(container, tree, { width: 240, height: 120 }, null, 120, host());

    metrics(candidate('group.inline'), { scrollHeight: 90, clientHeight: 40 });
    handle.update(tree, { width: 240, height: 40 }, null, 120);

    expect(visible()).toEqual(['group.mid']);
  });

  it('does not inspect or hide anything for a node with a single layout', () => {
    renderUiNode(container, firstFit([text('only')]), { width: 100, height: 100 }, null, 120, host());

    expect(candidate('only').hasAttribute('data-first-fit-hidden')).toBeFalse();
  });

  it('sizes itself like its last layout where the box does not decide', () => {
    const sizes: Record<string, number> = { 'group.inline': 10, 'group.mid': 20, 'group.stacked': 50 };
    const extent = uiFirstFitComponent.intrinsicMainPx!(tree, {
      basis: 120, crossExtent: null, horizontal: false, ofChild: child => sizes[child.id],
    });

    expect(extent).toBe(50);
  });

  it('claims a tile-level press from a button in any layout', () => {
    const button: UiNode = { id: 'group.stacked.go', type: 'ui.button', properties: { events: ['press'] } };
    const withButton = firstFit([text('group.inline'), { id: 'group.stacked', type: 'ui.stack', properties: {}, children: [button] }]);

    expect(activationClaim(withButton, null)).toEqual({ node: button });
  });
});

describe('contentFits', () => {
  function element(): HTMLElement {
    return document.createElement('div');
  }

  it('is false when the layout overflows its own box and true within half a pixel of it', () => {
    const root = element();
    metrics(root, { scrollWidth: 100.4, clientWidth: 100, scrollHeight: 20, clientHeight: 20 });
    expect(contentFits(root)).toBeTrue();

    metrics(root, { scrollWidth: 100.6, clientWidth: 100 });
    expect(contentFits(root)).toBeFalse();
  });

  it('is false when a clamped or wrapping text runs past its lines', () => {
    const root = element();
    const label = document.createElement('div');
    label.className = 'widget-text';
    root.appendChild(label);

    metrics(label, { scrollHeight: 40, clientHeight: 20 });
    expect(contentFits(root)).toBeFalse();

    metrics(label, { scrollHeight: 20, clientHeight: 20 });
    expect(contentFits(root)).toBeTrue();
  });
});
