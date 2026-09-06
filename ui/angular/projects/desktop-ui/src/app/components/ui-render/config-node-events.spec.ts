import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick } from './ui-render-test-support';

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('shared-ui-node event gating', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('emits nothing for an input that declares no events, and exactly one change for one that does', async () => {
    const noEvents: UiNode = { id: 'name1', type: 'string', properties: { value: '', literalOnly: true } };
    let rendered = await renderTree(noEvents);
    let input = el(rendered).querySelector('input') as HTMLInputElement;
    await type(input, 'ab');
    await tick(rendered);
    expect(rendered.events.length).toBe(0);

    const withChange: UiNode = {
      id: 'name2',
      type: 'string',
      properties: { value: '', events: ['change'], literalOnly: true },
    };
    rendered = await renderTree(withChange);
    input = el(rendered).querySelector('input') as HTMLInputElement;
    await type(input, 'ab');
    await tick(rendered);

    expect(rendered.events.length).toBe(1);
    expect(rendered.events[0]).toEqual({ nodeId: 'name2', name: 'change', data: 'ab' });
  });

  it('raises activate on a link click instead of navigating, and copy-value raises nothing', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        {
          id: 'docs',
          type: 'link',
          properties: { label: 'Open dashboard', url: 'https://developer.example.com', events: ['activate'] },
        },
        { id: 'uri', type: 'copy-value', properties: { label: 'Redirect URI', value: 'http://192.168.1.5:8080/callback' } },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    const anchor = host.querySelector('[data-node-id="docs"] a') as HTMLAnchorElement;
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    anchor.dispatchEvent(event);
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'docs', name: 'activate' }]);
    expect(event.defaultPrevented).toBeTrue();
    expect(anchor.getAttribute('href')).toBe('https://developer.example.com');

    const copyButton = host.querySelector('[data-node-id="uri"] shared-button') as HTMLElement;
    copyButton?.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await tick(rendered);

    expect(rendered.events.length).toBe(1);
    expect(host.querySelector('[data-node-id="uri"] .cv-value')?.textContent).toBe('http://192.168.1.5:8080/callback');
  });

  it('passes an unknown declared event name through without gating or throwing', async () => {
    spyOn(console, 'error');
    const root: UiNode = { id: 'n', type: 'dynamic-choice', properties: { events: ['open', 'reload', 'somethingNew'] } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();

    (host.querySelector('button.control') as HTMLButtonElement)?.click();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'open' }]);
    expect(console.error).not.toHaveBeenCalled();
  });
});
