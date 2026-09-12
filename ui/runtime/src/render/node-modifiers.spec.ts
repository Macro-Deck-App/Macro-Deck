import { UiNode } from '../ui-framework/ui-node.interface';
import { createUiComponentRegistry, DEFAULT_UI_COMPONENT_REGISTRY, UI_CORE_COMPONENTS, MACRO_DECK_COMPONENTS } from '../ui-framework/component-registry';
import { renderUiNode } from './ui-node-renderer';
import { UiRenderHost } from './ui-render-host';
import { PRESS_FEEDBACK_MIN_VISIBLE_MS } from './press-feedback';

let nextId = 0;
function node(type: string, properties: Record<string, unknown> = {}, children?: UiNode[], id?: string): UiNode {
  const built = { id: id ?? `n${++nextId}`, type, properties } as UiNode;
  if (children) built.children = children;
  return built;
}

function pointer(type: string, fields: { clientX?: number; clientY?: number; pointerId?: number } = {}): Event {
  const event = new Event(type) as Event & { clientX: number; clientY: number; pointerId: number; button: number };
  event.clientX = fields.clientX ?? 0;
  event.clientY = fields.clientY ?? 0;
  event.pointerId = fields.pointerId ?? 1;
  event.button = 0;
  return event;
}

function normalized(name: string, value: string): string {
  const probe = document.createElement('div');
  probe.style.setProperty(name, value);
  return probe.style.getPropertyValue(name);
}

describe('node modifiers', () => {
  let container: HTMLElement;
  let emitted: Array<{ name: string; payload: unknown }>;

  const host = (): UiRenderHost => ({
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: resource => (resource ? `/r/${resource.resourceId}` : null),
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: (_node, name, payload) => emitted.push({ name, payload }),
  });

  beforeEach(() => {
    emitted = [];
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  const mount = (tree: UiNode, width = 120, height = 120, registry = DEFAULT_UI_COMPONENT_REGISTRY) =>
    renderUiNode(container, tree, { width, height }, null, 120, host(), { registry });

  const byId = (id: string) => container.querySelector(`[data-node-id="${id}"]`) as HTMLElement;

  const names = () => emitted.map(entry => entry.name);

  const press = (element: HTMLElement) => {
    element.dispatchEvent(pointer('pointerdown'));
    element.dispatchEvent(pointer('pointerup'));
  };

  describe('on ordinary nodes', () => {
    it('writes nothing for a node that carries no modifiers', () => {
      mount(node('ui.stack', {}, [node('ui.stack', { mainSize: { basis: 0.5 } }, [], 'plain')]));

      const plain = byId('plain');
      for (const name of ['opacity', 'outline', 'outline-offset', 'border-radius', 'background', 'touch-action']) {
        expect(plain.style.getPropertyValue(name)).withContext(name).toBe('');
      }
      for (const name of ['role', 'aria-label', 'aria-description', 'aria-disabled']) {
        expect(plain.getAttribute(name)).withContext(name).toBeNull();
      }
    });

    it('paints every member the node carries', () => {
      mount(node('ui.stack', {}, [node('ui.stack', {
        mainSize: { basis: 0.5 },
        modifiers: {
          background: '#224466', radius: { basis: 0.1 }, borderWidth: { basis: 0.02 }, borderColor: '#ff8800',
          borderLine: 'dotted', accessibilityLabel: 'Card',
          accessibilityHint: { $localized: { scope: 'macrodeck.app', key: 'Hint' } },
        },
      }, [], 'card')]));

      const card = byId('card');
      expect(card.style.getPropertyValue('background')).toBe(normalized('background', '#224466'));
      expect(card.style.getPropertyValue('border-radius')).toBe('12px');
      const overlay = card.querySelector(':scope > .widget-modifier-border') as HTMLElement;
      expect([overlay.style.borderWidth, overlay.style.borderStyle]).toEqual(['2.4px', 'dotted']);
      expect(overlay.style.borderColor).toBe(normalized('border-color', '#ff8800'));
      expect(card.getAttribute('aria-label')).toBe('Card');
      expect(card.getAttribute('aria-description')).toBe('macrodeck.app:Hint');
      expect(card.getAttribute('role')).toBe('group');
    });

    it('draws a border inside the edge, so the children keep the boxes they had', () => {
      const tree = (modifiers?: Record<string, unknown>) => node('ui.stack', modifiers ? { modifiers } : {}, [
        node('ui.text', { text: 'a', mainSize: { basis: 0.25 } }, [], 'child'),
      ], 'root');
      mount(tree());
      const before = [byId('child').style.width, byId('child').style.height];
      container.innerHTML = '';

      mount(tree({ borderWidth: { basis: 0.05 }, borderColor: '#ffffff' }));

      expect([byId('child').style.width, byId('child').style.height]).toEqual(before);
    });

    it('leaves the tile corner to the surface on the tree root', () => {
      mount(node('ui.stack', { modifiers: { radius: { basis: 0.2 } } }, [], 'root'));

      expect(byId('root').style.getPropertyValue('border-radius')).toBe('');
    });

    it('draws the border of a button with artwork as an overlay above it, and keeps the outline on a text field', () => {
      const border = { borderWidth: { basis: 0.025 }, borderColor: '#ffffff' };
      mount(node('ui.stack', {}, [
        node('ui.button', { corner: 'tile', fit: 'cover', source: { resourceId: 'art' }, modifiers: border }, [], 'button'),
        node('ui.text-field', { modifiers: border }, [], 'field'),
      ]));

      const button = byId('button');
      const overlay = button.querySelector(':scope > .widget-modifier-border') as HTMLElement;
      expect(overlay).not.toBeNull();
      expect(overlay.style.borderWidth).toBe('3px');
      expect(button.style.getPropertyValue('outline')).toBe('');
      expect(byId('field').style.getPropertyValue('outline')).toBe('3px solid #ffffff');
      expect(byId('field').querySelector('.widget-modifier-border')).toBeNull();
    });

    it('outlines a bordered text, and keeps the outline when its text changes, while a stack keeps its overlay', () => {
      const tree = (value: string) => node('ui.stack', {}, [
        node('ui.text', { text: value, modifiers: { borderWidth: { basis: 0.02 }, borderColor: '#ffffff' } }, [], 'text'),
        node('ui.stack', { modifiers: { borderWidth: { basis: 0.02 }, borderColor: '#ffffff' } }, [], 'stack'),
      ], 'root');
      const handle = mount(tree('one'));

      handle.update(tree('two'), { width: 120, height: 120 }, null);

      const text = byId('text');
      expect(text.textContent).toBe('two');
      expect(text.style.getPropertyValue('outline')).toBe('2.4px solid #ffffff');
      expect(text.querySelector('.widget-modifier-border')).toBeNull();
      expect(byId('stack').querySelector(':scope > .widget-modifier-border')).not.toBeNull();
      expect(byId('stack').style.getPropertyValue('outline')).toBe('');
    });

    it('outlines a bordered list, so the border stays on its box while it scrolls', () => {
      mount(node('ui.stack', {}, [
        node('ui.list', { modifiers: { borderWidth: { basis: 0.02 }, borderColor: '#ffffff' } }, [node('ui.text', { text: 'row' })], 'list'),
      ]));

      expect(byId('list').style.getPropertyValue('outline')).toBe('2.4px solid #ffffff');
      expect(byId('list').querySelector('.widget-modifier-border')).toBeNull();
    });

    it('keeps the border overlay of a toggle across a repaint', () => {
      const tree = (on: boolean) => node('ui.stack', {}, [
        node('ui.toggle', { on, events: ['change'], modifiers: { borderWidth: { basis: 0.02 }, borderColor: '#ffffff', radius: { basis: 0.1 } } }, [], 'toggle'),
      ], 'root');
      const handle = mount(tree(false));

      handle.update(tree(true), { width: 120, height: 120 }, null);

      const toggle = byId('toggle');
      const overlay = toggle.querySelector(':scope > .widget-modifier-border') as HTMLElement;
      expect(overlay).not.toBeNull();
      expect(overlay.style.borderWidth).toBe('2.4px');
      expect(toggle.style.getPropertyValue('outline')).toBe('');
      expect(toggle.querySelector('.widget-toggle-track')).not.toBeNull();
    });

    it('drops the border overlay once the border is gone', () => {
      const tree = (modifiers: Record<string, unknown>) =>
        node('ui.stack', {}, [node('ui.stack', { modifiers }, [], 'card')], 'root');
      const handle = mount(tree({ borderWidth: { basis: 0.02 } }));
      expect(byId('card').querySelector('.widget-modifier-border')).not.toBeNull();

      handle.update(tree({ background: '#000000' }), { width: 120, height: 120 }, null);

      expect(byId('card').querySelector('.widget-modifier-border')).toBeNull();
    });

    it('keeps the textbox role of a labelled text field', () => {
      mount(node('ui.stack', {}, [node('ui.text-field', { modifiers: { accessibilityLabel: 'Search' } }, [], 'field')]));

      expect(byId('field').getAttribute('role')).toBeNull();
      expect(byId('field').getAttribute('aria-label')).toBe('Search');
    });

    it('names a labelled press claimant a button', () => {
      mount(node('ui.stack', {}, [
        node('ui.button', { events: ['press'], modifiers: { accessibilityLabel: 'Play' } }, [], 'play'),
      ]));

      expect(byId('play').getAttribute('role')).toBe('button');
    });

    it('lets a button with a modifier background keep its own corner', () => {
      mount(node('ui.stack', {}, [
        node('ui.button', { mainSize: { basis: 0.2 }, modifiers: { background: '#ff0000' } }, [], 'button'),
      ]));

      const button = byId('button');
      expect(button.style.getPropertyValue('background')).toBe(normalized('background', '#ff0000'));
      expect(parseFloat(button.style.borderRadius)).toBeCloseTo(120 * 0.2 * 0.12, 2);
    });

    it("gives a stack its own background back once the modifier's is removed", () => {
      const tree = (modifiers?: Record<string, unknown>) => node('ui.stack', {}, [
        node('ui.stack', { background: '#111111', ...(modifiers ? { modifiers } : {}) }, [], 'stack'),
      ], 'root');
      const handle = mount(tree({ background: '#ff0000', accessibilityLabel: 'Card' }));
      expect(byId('stack').style.getPropertyValue('background')).toBe(normalized('background', '#ff0000'));

      handle.update(tree(), { width: 120, height: 120 }, null);

      expect(byId('stack').style.getPropertyValue('background')).toBe(normalized('background', '#111111'));
      expect(byId('stack').getAttribute('aria-label')).toBeNull();
      expect(byId('stack').getAttribute('role')).toBeNull();
    });

    it('flips no property between two paints of the same tree', () => {
      const tree = node('ui.stack', {}, [
        node('ui.stack', { background: '#111111', modifiers: { background: '#ff0000', radius: { basis: 0.1 } } }),
        node('ui.button', { events: ['press'], modifiers: { background: '#00ff00', disabled: true } }),
        node('ui.modifier', { opacity: 0.5, clip: 'capsule', modifiers: { disabled: true } }, [node('ui.text', { text: 'x' })]),
      ], 'root');
      const handle = mount(tree);
      const observer = new MutationObserver(() => undefined);
      observer.observe(container, { attributes: true, subtree: true });

      handle.update(tree, { width: 120, height: 120 }, null);
      handle.update(tree, { width: 120, height: 120 }, null);

      expect(observer.takeRecords().map(record => record.attributeName)).toEqual([]);
      observer.disconnect();
    });

    it('is the only writer of the properties it owns, on every registered type', () => {
      const types = [...UI_CORE_COMPONENTS, ...MACRO_DECK_COMPONENTS].map(definition => definition.type);
      for (const type of types) {
        container.innerHTML = '';
        mount(node('ui.stack', {}, [node(type, {
          modifiers: { background: '#123456', radius: { basis: 0.1 }, borderWidth: { basis: 0.01 }, borderColor: '#ffffff', disabled: true },
        }, [], 'subject')]));

        const subject = byId('subject');
        expect(subject.style.getPropertyValue('background')).withContext(type).toBe(normalized('background', '#123456'));
        expect(subject.style.getPropertyValue('border-radius')).withContext(type).toBe('12px');
        const overlay = subject.querySelector(':scope > .widget-modifier-border') as HTMLElement | null;
        const overlaid = ['ui.stack', 'ui.button', 'ui.layer', 'ui.transform', 'ui.modifier', 'ui.grid', 'ui.toggle', 'ui.segmented'].includes(type);
        expect(overlay === null).withContext(`${type} overlay`).toBe(!overlaid);
        if (!overlaid) expect(subject.style.getPropertyValue('outline')).withContext(type).toBe('1.2px solid #ffffff');
        else expect(overlay?.style.borderWidth).withContext(type).toBe('1.2px');
        expect(subject.style.getPropertyValue('opacity')).withContext(type).toBe('0.4');
        expect(subject.style.getPropertyValue('box-shadow')).withContext(type).toBe('');
      }
    });
  });

  describe('disabled', () => {
    it('dims the node that is disabled and marks it', () => {
      mount(node('ui.stack', {}, [node('ui.stack', { modifiers: { disabled: true } }, [], 'region')]));

      expect(byId('region').style.opacity).toBe('0.4');
      expect(byId('region').getAttribute('aria-disabled')).toBe('true');
    });

    it('dims a nested disabled region once', () => {
      mount(node('ui.stack', {}, [
        node('ui.stack', { modifiers: { disabled: true } }, [
          node('ui.stack', { modifiers: { disabled: true } }, [], 'inner'),
        ], 'outer'),
      ]));

      expect(byId('outer').style.opacity).toBe('0.4');
      expect(byId('inner').style.opacity).toBe('');
      expect(byId('inner').getAttribute('aria-disabled')).toBe('true');
    });

    it('refuses a press a hand-written tree still declares inside a disabled region', () => {
      mount(node('ui.stack', {}, [
        node('ui.stack', { modifiers: { disabled: true } }, [node('ui.button', { events: ['press'] }, [], 'button')]),
      ]));

      press(byId('button'));

      expect(emitted).toEqual([]);
      expect(byId('button').classList.contains('widget-pressable')).toBeFalse();
    });

    it('follows an ancestor that is disabled and enabled again after mount', () => {
      const tree = (disabled: boolean) => node('ui.stack', {}, [
        node('ui.stack', disabled ? { modifiers: { disabled: true } } : {}, [
          node('ui.button', { events: ['press'], modifiers: { accessibilityLabel: 'Go' } }, [], 'button'),
        ], 'region'),
      ], 'root');
      const handle = mount(tree(false));

      handle.update(tree(true), { width: 120, height: 120 }, null);
      press(byId('button'));
      expect(emitted).toEqual([]);
      expect(byId('button').getAttribute('aria-disabled')).toBe('true');

      handle.update(tree(false), { width: 120, height: 120 }, null);
      press(byId('button'));
      expect(names()).toEqual(['press']);
      expect(byId('button').getAttribute('aria-disabled')).toBeNull();
    });

    it("multiplies a disabled wrapper's own opacity by the dim", () => {
      mount(node('ui.modifier', { opacity: 0.5, modifiers: { disabled: true } }, [node('ui.text', { text: 'x' })], 'wrapper'));

      expect(byId('wrapper').style.opacity).toBe('0.2');
    });

    it('takes no typing in a text field inside a disabled region', () => {
      mount(node('ui.stack', { modifiers: { disabled: true } }, [
        node('ui.text-field', { events: ['adjust', 'change'] }, [], 'field'),
      ]));

      expect((byId('field') as HTMLInputElement).readOnly).toBeTrue();
    });

    it('refuses local interaction on the toggle, segmented control and dial inside a disabled region', () => {
      const controls: UiNode[] = [
        node('ui.toggle', { events: ['change'] }, [], 'control'),
        node('ui.segmented', { events: ['change'] }, [node('ui.text', { text: 'a' }), node('ui.text', { text: 'b' })], 'control'),
        node('ui.dial', { events: ['adjust', 'change'], level: 0.5 }, [], 'control'),
      ];
      const pressed = (disabled: boolean, control: UiNode) => {
        container.innerHTML = '';
        emitted = [];
        mount(node('ui.stack', disabled ? { modifiers: { disabled: true } } : {}, [control]));
        const element = byId('control');
        element.getBoundingClientRect = () => ({ left: 0, top: 0, right: 100, bottom: 100, width: 100, height: 100 }) as DOMRect;
        element.dispatchEvent(pointer('pointerdown', { clientX: 90, clientY: 50 }));
        element.dispatchEvent(pointer('pointermove', { clientX: 80, clientY: 10 }));
        element.dispatchEvent(pointer('pointerup', { clientX: 80, clientY: 10 }));
        return { names: names(), pressable: element.classList.contains('widget-pressable') };
      };

      for (const control of controls) {
        expect(pressed(false, control).names.length).withContext(`${control.type} enabled`).toBeGreaterThan(0);
        expect(pressed(true, control)).withContext(`${control.type} disabled`).toEqual({ names: [], pressable: false });
      }
    });

    it('does not move a slider inside a disabled region', () => {
      mount(node('ui.stack', { modifiers: { disabled: true } }, [
        node('ui.slider', { events: ['adjust', 'change'], level: 0.5 }, [], 'slider'),
      ]));
      const slider = byId('slider');
      slider.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 20 }) as DOMRect;

      slider.dispatchEvent(pointer('pointerdown', { clientX: 90 }));
      slider.dispatchEvent(pointer('pointerup', { clientX: 90 }));

      expect(emitted).toEqual([]);
      expect(slider.classList.contains('widget-pressable')).toBeFalse();
    });
  });

  describe('ui.modifier', () => {
    it('draws its frame and lays its child out inside the padding', () => {
      mount(node('ui.modifier', { frame: { width: { basis: 0.5 }, height: { basis: 0.25 } }, padding: { basis: 0.05 } }, [
        node('ui.stack', {}, [], 'child'),
      ], 'wrapper'));

      const wrapper = byId('wrapper');
      expect([wrapper.style.width, wrapper.style.height, wrapper.style.padding]).toEqual(['60px', '30px', '6px']);
      expect([byId('child').style.width, byId('child').style.height]).toEqual(['48px', '18px']);
    });

    it('fits an aspect ratio inside a box whose sides are both known', () => {
      mount(node('ui.modifier', { frame: { aspectRatio: 2 } }, [node('ui.stack')], 'wrapper'));

      expect([byId('wrapper').style.width, byId('wrapper').style.height]).toEqual(['120px', '60px']);
    });

    it('asks its stack for its child plus padding, or for its fixed frame', () => {
      const column = (wrapper: UiNode) => node('ui.stack', {}, [wrapper, node('ui.stack', { fill: true }, [], 'rest')]);
      const content = node('ui.stack', {}, [node('ui.text', { text: 'x', mainSize: { basis: 0.2 } })]);

      mount(column(node('ui.modifier', { padding: { basis: 0.05 } }, [content])));
      expect(byId('rest').style.height).toBe('84px');

      container.innerHTML = '';
      mount(column(node('ui.modifier', { frame: { height: { basis: 0.3 } } }, [content])));
      expect(byId('rest').style.height).toBe('84px');
    });

    it('clips to its bounds, a capsule or a circle', () => {
      mount(node('ui.stack', {}, [
        node('ui.modifier', { clip: 'bounds', modifiers: { radius: { basis: 0.05 } } }, [node('ui.stack')], 'bounds'),
        node('ui.modifier', { clip: 'capsule' }, [node('ui.stack')], 'capsule'),
        node('ui.modifier', { clip: 'circle' }, [node('ui.stack')], 'circle'),
      ]));

      expect([byId('bounds').style.overflow, byId('bounds').style.borderRadius]).toEqual(['hidden', '6px']);
      expect([byId('capsule').style.overflow, byId('capsule').style.borderRadius]).toEqual(['hidden', '9999px']);
      expect(byId('circle').style.getPropertyValue('clip-path')).toBe('circle(50% at 50% 50%)');
      expect(byId('circle').style.overflow).toBe('');
    });

    it('draws the explicit fallback for a reader without ui.modifier', () => {
      const registry = createUiComponentRegistry(
        ...UI_CORE_COMPONENTS.filter(definition => definition.type !== 'ui.modifier'), ...MACRO_DECK_COMPONENTS);
      const wrapper = node('ui.modifier', { padding: { basis: 0.1 } }, [node('ui.text', { text: 'x' })], 'wrapper');
      wrapper.fallback = node('ui.text', { text: 'fallback' }, [], 'fallback');

      mount(node('ui.stack', {}, [wrapper]), 120, 120, registry);

      expect(byId('wrapper')).toBeNull();
      expect(byId('fallback').textContent).toBe('fallback');
    });
  });

  describe('gestures', () => {
    beforeEach(() => {
      jasmine.clock().install();
      jasmine.clock().mockDate(new Date(0));
    });

    afterEach(() => jasmine.clock().uninstall());

    const surface = (events: string[], children: UiNode[] = [node('ui.stack')]) =>
      node('ui.modifier', { events }, children, 'surface');

    it('reports a drag as a translation in basis fractions, at most ten times a second', () => {
      mount(surface(['drag', 'drag-end']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown'));
      element.dispatchEvent(pointer('pointermove', { clientX: 12 }));
      element.dispatchEvent(pointer('pointermove', { clientX: 24, clientY: -6 }));
      expect(emitted).toEqual([{ name: 'drag', payload: { x: 0.1, y: 0 } }]);

      jasmine.clock().tick(100);
      element.dispatchEvent(pointer('pointerup', { clientX: 30, clientY: -6 }));

      expect(emitted).toEqual([
        { name: 'drag', payload: { x: 0.1, y: 0 } },
        { name: 'drag', payload: { x: 0.2, y: -0.05 } },
        { name: 'drag-end', payload: { x: 0.25, y: -0.05 } },
      ]);
    });

    it('reports no drag and no drag-end for a movement inside the slop', () => {
      mount(surface(['drag', 'drag-end']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown'));
      element.dispatchEvent(pointer('pointermove', { clientX: 4 }));
      element.dispatchEvent(pointer('pointerup', { clientX: 4 }));

      expect(emitted).toEqual([]);
    });

    it('reports a quick swipe along its dominant axis', () => {
      mount(surface(['swipe']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown'));
      element.dispatchEvent(pointer('pointermove', { clientX: 5, clientY: -30 }));
      jasmine.clock().tick(200);
      element.dispatchEvent(pointer('pointerup', { clientX: 5, clientY: -30 }));

      expect(emitted).toEqual([{ name: 'swipe', payload: 'up' }]);
    });

    it('reports no swipe for a movement slower than the swipe window', () => {
      mount(surface(['swipe']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown'));
      element.dispatchEvent(pointer('pointermove', { clientX: 60 }));
      jasmine.clock().tick(600);
      element.dispatchEvent(pointer('pointerup', { clientX: 60 }));

      expect(emitted).toEqual([]);
    });

    it('reports a pinch as the scale since it began, and pinch-end once', () => {
      mount(surface(['pinch', 'pinch-end']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown', { pointerId: 1 }));
      element.dispatchEvent(pointer('pointerdown', { pointerId: 2, clientX: 60 }));
      element.dispatchEvent(pointer('pointermove', { pointerId: 2, clientX: 120 }));
      element.dispatchEvent(pointer('pointerup', { pointerId: 2, clientX: 120 }));
      element.dispatchEvent(pointer('pointerup', { pointerId: 1 }));

      expect(emitted).toEqual([{ name: 'pinch', payload: 2 }, { name: 'pinch-end', payload: 2 }]);
    });

    it('ends a drag exactly once when a second pointer lands mid-drag', () => {
      mount(surface(['drag', 'drag-end']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown', { pointerId: 1 }));
      element.dispatchEvent(pointer('pointermove', { pointerId: 1, clientX: 12 }));
      element.dispatchEvent(pointer('pointerdown', { pointerId: 2, clientX: 60 }));
      element.dispatchEvent(pointer('pointerup', { pointerId: 1, clientX: 30 }));
      element.dispatchEvent(pointer('pointerup', { pointerId: 2, clientX: 60 }));

      expect(emitted).toEqual([
        { name: 'drag', payload: { x: 0.1, y: 0 } },
        { name: 'drag-end', payload: { x: 0.1, y: 0 } },
      ]);
    });

    it('ends a pinch exactly once when a third pointer lands mid-pinch', () => {
      mount(surface(['pinch', 'pinch-end']));
      const element = byId('surface');

      element.dispatchEvent(pointer('pointerdown', { pointerId: 1 }));
      element.dispatchEvent(pointer('pointerdown', { pointerId: 2, clientX: 60 }));
      element.dispatchEvent(pointer('pointermove', { pointerId: 2, clientX: 120 }));
      element.dispatchEvent(pointer('pointerdown', { pointerId: 3, clientX: 90 }));
      for (const pointerId of [1, 2, 3]) element.dispatchEvent(pointer('pointerup', { pointerId }));

      expect(emitted).toEqual([{ name: 'pinch', payload: 2 }, { name: 'pinch-end', payload: 2 }]);
    });

    it('keeps the platform from panning only while a gesture is declared', () => {
      const handle = mount(surface(['drag']));
      expect(byId('surface').style.getPropertyValue('touch-action')).toBe('none');

      handle.update(surface([]), { width: 120, height: 120 }, null);
      expect(byId('surface').style.getPropertyValue('touch-action')).toBe('');

      byId('surface').dispatchEvent(pointer('pointerdown'));
      byId('surface').dispatchEvent(pointer('pointermove', { clientX: 60 }));
      byId('surface').dispatchEvent(pointer('pointerup', { clientX: 60 }));
      expect(emitted).toEqual([]);
    });

    it('leaves a pointer that starts on a slider or a list inside it to that node', () => {
      mount(surface(['drag', 'drag-end'], [node('ui.stack', {}, [
        node('ui.slider', { level: 0.5 , events: ['change'] }, [], 'slider'),
        node('ui.list', {}, [node('ui.text', { text: 'row' }, [], 'row')]),
      ])]));
      byId('slider').getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 20 }) as DOMRect;

      for (const id of ['slider', 'row']) {
        byId(id).dispatchEvent(pointer('pointerdown'));
        byId(id).dispatchEvent(pointer('pointermove', { clientX: 60 }));
        byId(id).dispatchEvent(pointer('pointerup', { clientX: 60 }));
      }

      expect(names().filter(name => name.startsWith('drag'))).toEqual([]);
    });

    it('lets an inner press win until the slop is exceeded, then cancels it', () => {
      mount(surface(['drag', 'drag-end'], [node('ui.button', { events: ['press-start', 'press-end', 'press'] }, [], 'button')]));
      const button = byId('button');

      button.dispatchEvent(pointer('pointerdown'));
      button.dispatchEvent(pointer('pointerup'));
      expect(names()).toEqual(['press-start', 'press-end', 'press']);

      emitted = [];
      button.dispatchEvent(pointer('pointerdown'));
      button.dispatchEvent(pointer('pointermove', { clientX: 12 }));
      button.dispatchEvent(pointer('pointerup', { clientX: 12 }));

      expect(names()).toEqual(['press-start', 'press-end', 'drag', 'drag-end']);
      jasmine.clock().tick(PRESS_FEEDBACK_MIN_VISIBLE_MS);
      expect(button.querySelector('.widget-press-tint-active')).toBeNull();
    });
  });
});
