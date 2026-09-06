import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick } from './ui-render-test-support';

describe('shared-ui-node layout capabilities', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('a weighted, non-wrapping stack row', () => {
    function row(wrap: boolean | undefined): UiNode {
      return {
        id: 'row',
        type: 'stack',
        properties: wrap === undefined ? { direction: 'horizontal' } : { direction: 'horizontal', wrap },
        children: [
          { id: 'a', type: 'string', properties: { value: 'x', literalOnly: true, rowWeight: 2 } },
          { id: 'b', type: 'string', properties: { value: 'y', literalOnly: true, rowWeight: 1 } },
          { id: 'c', type: 'string', properties: { value: 'z', literalOnly: true } },
        ],
      };
    }

    it('keeps its children on one line and sizes them in the declared proportion when wrap is false', async () => {
      const rendered = await renderTree(row(false));
      const host = el(rendered);

      const container = host.querySelector('.config-chrome-stack') as HTMLElement;
      expect(container.classList.contains('config-chrome-stack-nowrap')).toBeTrue();

      const a = host.querySelector('[data-node-id="a"]') as HTMLElement;
      const b = host.querySelector('[data-node-id="b"]') as HTMLElement;
      const c = host.querySelector('[data-node-id="c"]') as HTMLElement;

      // The 2:1 ratio the weights declare, expressed as the flex shorthand - grow and shrink both equal
      // to the weight, basis zero, so the ratio holds regardless of each child's own content width.
      expect(a.style.flex).toBe('2 2 0%');
      expect(b.style.flex).toBe('1 1 0%');
      // No declared weight: no override at all, so the browser's own content-sized default applies -
      // exactly what lets an icon button stay sized to its icon next to a weighted sibling.
      expect(c.style.flex).toBe('');
    });

    it('ignores a declared weight while the row still wraps, unchanged from before this property existed', async () => {
      for (const wrapValue of [true, undefined]) {
        const rendered = await renderTree(row(wrapValue));
        const host = el(rendered);

        expect(host.querySelector('.config-chrome-stack')?.classList.contains('config-chrome-stack-nowrap'))
          .toBeFalse();
        expect((host.querySelector('[data-node-id="a"]') as HTMLElement).style.flex).toBe('');
      }
    });
  });

  describe('a segmented option with an icon', () => {
    function alignField(): UiNode {
      return {
        id: 'align',
        type: 'choice',
        properties: {
          segmented: true,
          value: 'left',
          events: ['change'],
          options: [
            { value: 'left', label: 'Left', icon: 'align-left' },
            { value: 'center', label: 'Center', icon: 'align-center' },
          ],
        },
      };
    }

    it('renders the icon rather than the label text, but keeps the label as the accessible name', async () => {
      const rendered = await renderTree(alignField());
      const host = el(rendered);

      const options = host.querySelectorAll('.seg-option');
      expect(options.length).toBe(2);
      expect(options[0].querySelector('.icon-align-left')).not.toBeNull();
      expect(options[0].textContent?.trim()).toBe('');
      expect(options[0].getAttribute('aria-label')).toBe('Left');
    });

    it('still emits the option\'s own value when an icon option is picked', async () => {
      const rendered = await renderTree(alignField());
      const host = el(rendered);

      (host.querySelectorAll('.seg-option')[1] as HTMLElement).click();
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'align', name: 'change', data: 'center' }]);
    });
  });

  describe('a label-suppressed input', () => {
    it('renders its control with no visible caption while staying accessible through a named group', async () => {
      const root: UiNode = {
        id: 'activeStateId',
        type: 'choice',
        properties: {
          label: 'Editing Off',
          hideLabel: true,
          value: 'off',
          options: [{ value: 'off', label: 'Off' }],
        },
      };
      const rendered = await renderTree(root);
      const host = el(rendered);

      // The caption block never renders...
      expect(host.querySelector('.config-node-label')).toBeNull();

      // ...but the accessible name is not lost: the field wrapper picks up the label as a named
      // `group`'s own `aria-label`, so a screen reader announces "Editing Off" on the way into the
      // control exactly as it would have read the now-invisible caption.
      const field = host.querySelector('.config-node-field') as HTMLElement;
      expect(field.getAttribute('role')).toBe('group');
      expect(field.getAttribute('aria-label')).toBe('Editing Off');
    });

    it('renders the caption normally when hideLabel is absent', async () => {
      const root: UiNode = {
        id: 'n',
        type: 'string',
        properties: { label: 'Name', literalOnly: true },
      };
      const rendered = await renderTree(root);
      const host = el(rendered);

      expect(host.querySelector('.config-node-label')?.textContent).toContain('Name');
      expect(host.querySelector('.config-node-field')?.getAttribute('role')).toBeNull();
    });
  });
});
