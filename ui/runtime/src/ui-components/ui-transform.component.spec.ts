import { UiNode } from '../ui-framework/ui-node.interface';
import { createUiComponentRegistry, UI_CORE_COMPONENTS } from '../ui-framework/component-registry';
import { renderUiNode } from '../render/ui-node-renderer';
import { UiRenderHost } from '../render/ui-render-host';

const TRANSFORM_PROPERTIES = ['transform', 'transform-origin', '-webkit-transform', '-webkit-transform-origin'];

function testHost(): UiRenderHost {
  return {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: resource => (resource ? `/api/ui/resources/${resource.resourceId}` : null),
    now: () => Date.parse('2026-01-02T03:04:05Z'),
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: () => undefined,
  };
}

function transform(properties: Record<string, unknown>, children: UiNode[] = [needle()]): UiNode {
  return { id: 'gauge', type: 'ui.transform', properties, children } as UiNode;
}

function needle(id = 'needle'): UiNode {
  return { id, type: 'ui.text', properties: { text: '|' } } as UiNode;
}

describe('ui.transform', () => {
  let container: HTMLElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const mount = (tree: UiNode, registry = undefined as ReturnType<typeof createUiComponentRegistry> | undefined) =>
    renderUiNode(container, tree, { width: 120, height: 80 }, null, 120, testHost(), registry ? { registry } : undefined);

  const element = () => container.querySelector('.widget-transform') as HTMLElement;
  const style = (name: string) => element().style.getPropertyValue(name);

  it('scales, then rotates about the pivot, then shifts, in both spellings the browser floor needs', () => {
    mount(transform({ rotation: 42, originX: 0.25, originY: 1.5, zoom: 2, offsetX: 0.5, offsetY: -0.25 }));

    for (const prefix of ['', '-webkit-']) {
      expect(style(`${prefix}transform`)).toBe('translate(50%, -25%) rotate(42deg) scale(2)');
      expect(style(`${prefix}transform-origin`)).toBe('25% 150%');
    }
  });

  it('pivots about the centre when no origin is given', () => {
    mount(transform({ rotation: -90 }));

    expect(style('transform-origin')).toBe('50% 50%');
  });

  it('draws every child across the whole box, first furthest back', () => {
    mount(transform({ rotation: 10 }, [needle('back'), needle('front')]));

    const children = Array.from(element().children) as HTMLElement[];
    expect(children.map(child => child.getAttribute('data-node-id'))).toEqual(['back', 'front']);
    expect(element().style.width).toBe('120px');
    expect(element().style.height).toBe('80px');
    for (const child of children) expect(child.style.width).toBe('120px');
  });

  it('writes no transform at all when every key is absent', () => {
    mount(transform({}));

    for (const name of TRANSFORM_PROPERTIES) expect(style(name)).withContext(name).toBe('');
  });

  it('treats a zoom that is not greater than zero as no zoom', () => {
    mount(transform({ zoom: 0 }));

    expect(style('transform')).toBe('');
  });

  it('turns the needle on an angle-only update without rebuilding the element or its children', () => {
    const handle = mount(transform({ rotation: 10, originY: 0.9 }));
    const before = element();
    const child = before.firstElementChild;

    handle.update(transform({ rotation: 57, originY: 0.9 }), { width: 120, height: 80 }, null);

    expect(element()).toBe(before);
    expect(element().firstElementChild).toBe(child);
    expect(style('transform')).toBe('translate(0%, 0%) rotate(57deg) scale(1)');
  });

  it('clears every spelling when an update takes the transform back to identity', () => {
    const handle = mount(transform({ rotation: 10, zoom: 2 }));

    handle.update(transform({}), { width: 120, height: 80 }, null);

    for (const name of TRANSFORM_PROPERTIES) expect(style(name)).withContext(name).toBe('');
  });

  it('gives a nested transform its own transform, so the two compose', () => {
    mount(transform({ rotation: 90 }, [{ ...transform({ zoom: 2 }), id: 'inner' } as UiNode]));

    const [outer, inner] = Array.from(container.querySelectorAll('.widget-transform')) as HTMLElement[];
    expect(outer.style.transform).toBe('translate(0%, 0%) rotate(90deg) scale(1)');
    expect(inner.style.transform).toBe('translate(0%, 0%) rotate(0deg) scale(2)');
  });

  describe('on a reader that does not know the type', () => {
    const olderRegistry = () =>
      createUiComponentRegistry(...UI_CORE_COMPONENTS.filter(definition => definition.type !== 'ui.transform'));

    it('draws the fallback instead', () => {
      const reading = { id: 'reading', type: 'ui.text', properties: { text: '42 km/h' } } as UiNode;

      mount({ ...transform({ rotation: 42 }), fallback: reading }, olderRegistry());

      expect(container.querySelector('.widget-transform')).toBeNull();
      expect(container.textContent).toContain('42 km/h');
    });

    it('draws the unsupported placeholder when there is no fallback', () => {
      expect(() => mount(transform({ rotation: 42 }), olderRegistry())).not.toThrow();

      expect(container.querySelector('[data-unsupported-type="ui.transform"]')).not.toBeNull();
    });
  });
});
