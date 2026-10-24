import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { ButtonComponent } from '@shared';
import { RailItemComponent } from '../rail-page/rail-item.component';
import { el, renderTree, tick } from './ui-render-test-support';

function split(): UiNode {
  return {
    id: 'scripts',
    type: 'split',
    properties: { railTitle: 'Scripts' },
    children: [
      {
        id: 'scripts.rail-actions',
        type: 'stack',
        children: [{ id: 'new', type: 'button', properties: { label: 'New', icon: 'plus', events: ['activate'] } }],
      },
      {
        id: 'scripts.rail',
        type: 'stack',
        children: [{ id: 'list', type: 'list', properties: { label: 'Scripts' }, children: [] }],
      },
      {
        id: 'scripts.detail',
        type: 'stack',
        children: [{ id: 'name', type: 'string', properties: { label: 'Name', value: 'Hello' } }],
      },
    ],
  };
}

function listItem(properties: Record<string, unknown>, children: UiNode[] = []): UiNode {
  return {
    id: 'list',
    type: 'list',
    properties: { label: 'Scripts' },
    children: [{ id: 'item', type: 'list-item', properties: { events: ['activate'], ...properties }, children }],
  };
}

describe('shared-ui-node page layout primitives', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('draws a split as the app\'s rail page: titled rail with its actions and list, detail in the card', async () => {
    const host = el(await renderTree(split()));

    const page = host.querySelector('shared-rail-page.config-chrome-split') as HTMLElement;
    expect(page).not.toBeNull();
    expect(page.querySelector('.rail-header h1')?.textContent).toBe('Scripts');
    expect(page.querySelector('.rail-header [data-node-id="new"]')).not.toBeNull();
    expect(page.querySelector('.rail [data-node-id="list"]')).not.toBeNull();
    expect(page.querySelector('.detail [data-node-id="name"]')).not.toBeNull();
    expect(page.querySelector('.detail [data-node-id="new"]')).toBeNull();
  });

  it('draws a list as a labelled listbox of rows with label, description, icon, tag and status dot', async () => {
    const host = el(await renderTree(listItem({
      label: 'Hotkeys', description: 'Exited with 0', icon: 'file-text', badge: 'Running', severity: 'success', selected: true,
    })));

    const listbox = host.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.getAttribute('aria-label')).toBe('Scripts');
    const option = listbox.querySelector('button[role="option"]') as HTMLButtonElement;
    expect(option.getAttribute('aria-selected')).toBe('true');
    expect(option.querySelector('.config-chrome-list-item-label')?.textContent).toBe('Hotkeys');
    expect(option.querySelector('.config-chrome-list-item-description')?.textContent).toBe('Exited with 0');
    expect(option.querySelector('.icon-file-text')).not.toBeNull();
    expect(option.querySelector('.config-chrome-tag')?.textContent).toBe('Running');
    expect(option.querySelector('.config-chrome-list-item-dot')?.getAttribute('data-severity')).toBe('success');
  });

  it('draws no dot without a severity and a neutral one for a severity it does not know', async () => {
    const plain = el(await renderTree(listItem({ label: 'A' })));
    expect(plain.querySelector('.config-chrome-list-item-dot')).toBeNull();
    expect(plain.querySelector('[role="option"]')?.getAttribute('aria-selected')).toBe('false');

    const unknown = el(await renderTree(listItem({ label: 'A', severity: 'critical' })));
    expect(unknown.querySelector('.config-chrome-list-item-dot')?.getAttribute('data-severity')).toBe('neutral');
  });

  it('raises activate when a row is chosen, and not when its trailing menu is opened', async () => {
    const rendered = await renderTree(listItem({ label: 'Hotkeys' }, [
      {
        id: 'item.menu',
        type: 'menu',
        properties: { label: 'More' },
        children: [{ id: 'item.delete', type: 'button', properties: { label: 'Delete', events: ['activate'] } }],
      },
    ]));
    const host = el(rendered);

    const trailing = host.querySelector('.config-chrome-list-item-trailing') as HTMLElement;
    expect(trailing.closest('button')).toBeNull();
    (trailing.querySelector('button') as HTMLButtonElement).click();
    await tick(rendered);
    expect(rendered.events).toEqual([]);

    (host.querySelector('button[role="option"]') as HTMLButtonElement).click();
    await tick(rendered);
    expect(rendered.events).toEqual([{ nodeId: 'item', name: 'activate' }]);
  });

  it('keeps every list row as tall as the app\'s rail rows, selected or not, with or without a trailing menu', async () => {
    const menu = (id: string): UiNode => ({
      id: `${id}.menu`,
      type: 'menu',
      properties: { label: 'More' },
      children: [{ id: `${id}.delete`, type: 'button', properties: { label: 'Delete', events: ['activate'] } }],
    });
    const item = (id: string, selected: boolean, trailing: boolean): UiNode => ({
      id,
      type: 'list-item',
      properties: { label: id, selected, events: ['activate'] },
      children: trailing ? [menu(id)] : [],
    });
    const host = el(await renderTree({
      id: 'list',
      type: 'list',
      children: [item('plain', false, false), item('picked', true, false), item('menu', false, true), item('both', true, true)],
    }));
    const rows = [...host.querySelectorAll<HTMLElement>('.config-chrome-list-item')].map(row => row.getBoundingClientRect().height);
    const trigger = host.querySelector<HTMLElement>('.config-chrome-list-item-trailing .dropdown-trigger')!;
    const triggerBorder = getComputedStyle(trigger).borderTopStyle;
    const rail = TestBed.createComponent(RailItemComponent);
    rail.componentRef.setInput('label', 'Rail');
    rail.detectChanges();
    const railHeight = (rail.nativeElement as HTMLElement).querySelector('.rail-item')!.getBoundingClientRect().height;
    expect(rows.length).toBe(4);
    expect(rows).toEqual([railHeight, railHeight, railHeight, railHeight]);
    expect(triggerBorder).toBe('none');
  });

  it('fits a trailing icon button with a variant into the compact row', async () => {
    const host = el(await renderTree({
      id: 'list',
      type: 'list',
      children: [
        { id: 'plain', type: 'list-item', properties: { label: 'plain', events: ['activate'] } },
        {
          id: 'run',
          type: 'list-item',
          properties: { label: 'run', events: ['activate'] },
          children: [{
            id: 'run.play',
            type: 'button',
            properties: { label: 'Run', icon: 'play', variant: 'ghost', events: ['activate'] },
          }],
        },
      ],
    }));
    const rows = [...host.querySelectorAll<HTMLElement>('.config-chrome-list-item')].map(row => row.getBoundingClientRect().height);
    const button = host.querySelector<HTMLElement>('.config-chrome-list-item-trailing button')!.getBoundingClientRect();
    const probe = document.body.appendChild(document.createElement('div'));
    probe.style.height = 'var(--control-height-compact)';
    const compact = probe.getBoundingClientRect().height;
    probe.remove();
    const rail = TestBed.createComponent(RailItemComponent);
    rail.componentRef.setInput('label', 'Rail');
    rail.detectChanges();
    const railHeight = (rail.nativeElement as HTMLElement).querySelector('.rail-item')!.getBoundingClientRect().height;

    expect(rows).toEqual([railHeight, railHeight]);
    expect(button.height).toBe(compact);
    expect(button.width).toBe(compact);
  });

  it('draws an icon-only button with a variant exactly like the app\'s icon buttons, and without one as before', async () => {
    const host = el(await renderTree({
      id: 'actions',
      type: 'stack',
      properties: { direction: 'horizontal' },
      children: [
        { id: 'new', type: 'button', properties: { label: 'New', icon: 'plus', variant: 'secondary', events: ['activate'] } },
        { id: 'old', type: 'button', properties: { label: 'Old', icon: 'plus', events: ['activate'] } },
      ],
    }));
    const drawnHeight = (host.querySelector('[data-node-id="new"] button') as HTMLElement).getBoundingClientRect().height;
    const app = TestBed.createComponent(ButtonComponent);
    app.componentRef.setInput('variant', 'secondary');
    app.componentRef.setInput('size', 'icon');
    app.detectChanges();

    const drawn = host.querySelector('[data-node-id="new"] button') as HTMLButtonElement;
    const reference = (app.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;
    expect(drawn.className).toBe(reference.className);
    expect(drawnHeight).toBe(reference.getBoundingClientRect().height);
    expect(drawn.querySelector('.icon-plus')?.classList).toContain('icon-sm');
    expect(host.querySelector('[data-node-id="old"] button')?.classList).toContain('config-chrome-icon-button');
  });

  it('draws a button in its variant, and an icon with its label when asked to', async () => {
    const root: UiNode = {
      id: 'row',
      type: 'stack',
      properties: { direction: 'horizontal' },
      children: [
        { id: 'save', type: 'button', properties: { label: 'Save', variant: 'primary', events: ['activate'] } },
        { id: 'delete', type: 'button', properties: { label: 'Delete', variant: 'danger', events: ['activate'] } },
        { id: 'odd', type: 'button', properties: { label: 'Odd', variant: 'sparkly', events: ['activate'] } },
        { id: 'run', type: 'button', properties: { label: 'Run', icon: 'play', iconWithLabel: true, events: ['activate'] } },
        { id: 'more', type: 'button', properties: { label: 'More', icon: 'dots-vertical', events: ['activate'] } },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);
    const button = (id: string) => host.querySelector(`[data-node-id="${id}"] button`) as HTMLButtonElement;

    expect(button('save').classList).toContain('sb-primary');
    expect(button('delete').classList).toContain('sb-danger');
    expect(button('odd').classList).toContain('sb-secondary');
    expect(button('run').querySelector('.icon-play')).not.toBeNull();
    expect(button('run').textContent?.trim()).toBe('Run');
    expect(button('more').classList).toContain('config-chrome-icon-button');

    button('run').click();
    await tick(rendered);
    expect(rendered.events).toEqual([{ nodeId: 'run', name: 'activate' }]);
  });

  it('aligns and spaces a stack as asked, and keeps the old spacing without the properties', async () => {
    const host = el(await renderTree({
      id: 'root',
      type: 'stack',
      children: [
        {
          id: 'footer',
          type: 'stack',
          properties: { direction: 'horizontal', align: 'end', gap: 'small' },
          children: [{ id: 'save', type: 'button', properties: { label: 'Save' } }],
        },
        { id: 'plain', type: 'stack', children: [{ id: 'p', type: 'prose', properties: { text: 'x' } }] },
      ],
    }));

    const footer = getComputedStyle(host.querySelector('[data-node-id="footer"] .config-chrome-stack') as HTMLElement);
    expect(footer.justifyContent).toBe('flex-end');
    const plain = host.querySelector('[data-node-id="plain"] .config-chrome-stack') as HTMLElement;
    expect(plain.hasAttribute('data-align')).toBeFalse();
    expect(plain.hasAttribute('data-gap')).toBeFalse();
    const small = parseFloat(footer.columnGap);
    expect(small).toBeLessThan(parseFloat(getComputedStyle(plain).rowGap));
  });

  it('draws a card stack as the theme card, with a leading bar only when it has an accent', async () => {
    const host = el(await renderTree({
      id: 'root',
      type: 'stack',
      children: [
        { id: 'card', type: 'stack', properties: { variant: 'card' }, children: [] },
        { id: 'accented', type: 'stack', properties: { variant: 'card', accent: '#ff0000' }, children: [] },
      ],
    }));

    const card = host.querySelector('[data-node-id="card"] .config-chrome-stack') as HTMLElement;
    expect(card.classList).toContain('config-chrome-stack-card');
    expect(card.classList).not.toContain('config-chrome-stack-card-accent');
    const accented = host.querySelector('[data-node-id="accented"] .config-chrome-stack') as HTMLElement;
    expect(accented.classList).toContain('config-chrome-stack-card-accent');
    expect(accented.classList).not.toContain('config-chrome-stack-accent');
    expect(getComputedStyle(accented).borderInlineStartColor).toBe('rgb(255, 0, 0)');
  });

  it('draws an empty state with its icon, title, text and the buttons beneath', async () => {
    const rendered = await renderTree({
      id: 'empty',
      type: 'empty-state',
      properties: { icon: 'file-text', title: 'No script selected', text: 'Pick one on the left.' },
      children: [{ id: 'create', type: 'button', properties: { label: 'New script', events: ['activate'] } }],
    });
    const host = el(rendered);

    const state = host.querySelector('shared-empty-state') as HTMLElement;
    expect(state.querySelector('.icon-file-text')).not.toBeNull();
    expect(state.querySelector('.es-title')?.textContent).toBe('No script selected');
    expect(state.querySelector('.es-message')?.textContent).toBe('Pick one on the left.');
    (state.querySelector('[data-node-id="create"] button') as HTMLButtonElement).click();
    await tick(rendered);
    expect(rendered.events).toEqual([{ nodeId: 'create', name: 'activate' }]);
  });

  it('draws a menu entry in red when its button is a danger button, by variant or by a danger confirmation', async () => {
    const rendered = await renderTree({
      id: 'row',
      type: 'menu',
      properties: { label: 'More', icon: 'dots-vertical' },
      children: [
        { id: 'row.rename', type: 'button', properties: { label: 'Rename', events: ['activate'] } },
        { id: 'row.remove', type: 'button', properties: { label: 'Remove', variant: 'danger', events: ['activate'] } },
        {
          id: 'row.delete',
          type: 'button',
          properties: { label: 'Delete', variant: 'danger', confirmDanger: true, confirmMessage: 'Sure?', events: ['activate'] },
        },
        { id: 'row.reset', type: 'button', properties: { label: 'Reset', confirmDanger: true, events: ['activate'] } },
      ],
    });

    (el(rendered).querySelector('.dropdown-trigger') as HTMLButtonElement).click();
    await tick(rendered);

    const entries = [...document.querySelectorAll<HTMLButtonElement>('.config-chrome-menu-item')];
    const entry = (label: string) => entries.find(item => item.textContent?.trim() === label)!;
    expect(entries.filter(item => item.classList.contains('is-danger')).map(item => item.textContent?.trim()))
      .toEqual(['Remove', 'Delete', 'Reset']);
    expect(getComputedStyle(entry('Delete')).color).not.toBe(getComputedStyle(entry('Rename')).color);

    entry('Delete').click();
    await tick(rendered);
    const confirm = [...document.querySelectorAll('shared-confirmation-modal shared-button button')]
      .find(button => button.textContent?.trim() === 'Delete') as HTMLButtonElement;
    expect(confirm.classList).toContain('sb-danger');
  });

  it('puts a heading badge beside the caption as a tag', async () => {
    const host = el(await renderTree({ id: 'h', type: 'heading', properties: { text: 'Hotkeys', badge: 'Unsaved' } }));

    const heading = host.querySelector('.config-chrome-heading') as HTMLElement;
    expect(heading.querySelector('.config-chrome-tag')?.textContent).toBe('Unsaved');

    const plain = el(await renderTree({ id: 'h', type: 'heading', properties: { text: 'Hotkeys' } }));
    expect(plain.querySelector('.config-chrome-tag')).toBeNull();
  });
});
