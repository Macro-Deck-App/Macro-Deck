import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentAlignments, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function chart(id: string, properties: Record<string, unknown>): UiNode {
  return { id, type: Types.Chart, properties };
}

function path(rendered: RenderedTree, id: string, which: 'line' | 'area'): SVGPathElement | null {
  return el(rendered).querySelector(`[data-node-id="${id}"] .widget-chart-${which}`);
}

async function render(node: UiNode, box: { width: number; height: number }, basis: number) {
  const root: UiNode = {
    id: 'root',
    type: Types.Layer,
    properties: {},
    children: [node],
  };

  const rendered = await renderTree(root, withBasis(basis));
  rendered.fixture.componentRef.setInput('box', box);
  await rendered.fixture.whenStable();

  return rendered;
}

describe('ui.chart and ui.layer', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('plots the series across the full width, inside the band plotTop names', async () => {
    const rendered = await render(
      chart('dense', { [Props.Points]: [0.02, 0.12, 0.31], [Props.PlotTop]: 0.66 }),
      { width: 480, height: 240 },
      240,
    );

    // Band 158.4..240: y = 240 - point x 81.6. First point on the leading edge, last on the trailing.
    expect(path(rendered, 'dense', 'line')?.getAttribute('d'))
      .toBe('M0 238.37 L240 230.21 L480 214.7');
  });

  it('fills from the line to the element bottom edge, not to the top of the band', async () => {
    const rendered = await render(
      chart('dense', { [Props.Points]: [0.5, 0.5], [Props.PlotTop]: 0.66 }),
      { width: 480, height: 240 },
      240,
    );

    expect(path(rendered, 'dense', 'area')?.getAttribute('d'))
      .toBe('M0 240 L0 199.2 L480 199.2 L480 240 Z');
  });

  it('draws a single sample as a value that has held rather than as a dot', async () => {
    const rendered = await render(
      chart('one', { [Props.Points]: [0.25] }),
      { width: 480, height: 240 },
      240,
    );

    expect(path(rendered, 'one', 'line')?.getAttribute('d')).toBe('M0 180 L480 180');
  });

  it('draws nothing at all for an empty series', async () => {
    const rendered = await render(
      chart('empty', { [Props.Points]: [], [Props.PlotTop]: 0.5 }),
      { width: 480, height: 240 },
      240,
    );

    // A flat line along the foot of the band would read as a real value of zero.
    expect(path(rendered, 'empty', 'line')).toBeNull();
    expect(path(rendered, 'empty', 'area')).toBeNull();
  });

  it('plots the whole element when no band is named', async () => {
    const rendered = await render(
      chart('full', { [Props.Points]: [0, 1] }),
      { width: 480, height: 240 },
      240,
    );

    expect(path(rendered, 'full', 'line')?.getAttribute('d')).toBe('M0 240 L480 0');
  });

  it('holds the line to the cell clamp rather than letting it thicken with the card', async () => {
    const rendered = await render(
      chart('dense', {
        [Props.Points]: [0.5, 0.5],
        [Props.Thickness]: { basis: 0.0167, maxOfCell: 0.0167 },
      }),
      { width: 480, height: 240 },
      240,
    );

    expect(path(rendered, 'dense', 'line')?.getAttribute('stroke-width')).toBe('2.004');
  });

  it('gives every layer child the whole box so one is drawn behind another', async () => {
    const root: UiNode = {
      id: 'root',
      type: Types.Layer,
      properties: {},
      children: [
        chart('behind', { [Props.Points]: [0.5] }),
        { id: 'front', type: Types.Stack, properties: { [Props.Padding]: { basis: 0.07 } } },
      ],
    };

    const rendered = await renderTree(root, withBasis(240));
    rendered.fixture.componentRef.setInput('box', { width: 480, height: 240 });
    await rendered.fixture.whenStable();

    const behind = el(rendered).querySelector('[data-node-id="behind"].widget-chart') as SVGElement;
    const front = el(rendered).querySelector('[data-node-id="front"].widget-stack') as HTMLElement;

    expect(behind.getAttribute('width')).toBe('480');
    expect(behind.getAttribute('height')).toBe('240');
    expect(front.style.width).toBe('480px');
    expect(front.style.height).toBe('240px');
  });
});

describe('ui.text reservations and baseline rows', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('reserves the width a live readout asked for, in digit widths', async () => {
    const rendered = await renderTree(
      {
        id: 'reading',
        type: Types.Text,
        properties: { [Props.Text]: '73.4', [Props.Size]: { basis: 0.26 }, [Props.Digits]: 3.5 },
      },
      withBasis(240),
    );

    const run = el(rendered).querySelector('[data-node-id="reading"].widget-text') as HTMLElement;

    expect(run.style.minWidth).toBe('3.5ch');
    expect(getComputedStyle(run).fontVariantNumeric).toContain('tabular-nums');
  });

  it('leaves a run unreserved and proportional when it asks for nothing', async () => {
    const rendered = await renderTree(
      { id: 'label', type: Types.Text, properties: { [Props.Text]: 'CPU Load' } },
      withBasis(240),
    );

    const run = el(rendered).querySelector('[data-node-id="label"].widget-text') as HTMLElement;

    expect(run.style.minWidth).toBe('');
    expect(run.classList).not.toContain('widget-text-tabular');
  });

  it('aligns a row on its text rather than on its boxes', async () => {
    const rendered = await renderTree(
      {
        id: 'row',
        type: Types.Stack,
        properties: { [Props.Direction]: 'horizontal', [Props.Align]: UiComponentAlignments.Baseline },
        children: [
          { id: 'row.value', type: Types.Text, properties: { [Props.Text]: '73', [Props.Size]: { basis: 0.26 } } },
          { id: 'row.unit', type: Types.Text, properties: { [Props.Text]: '%', [Props.Size]: { basis: 0.12 } } },
        ],
      },
      withBasis(240),
    );

    const row = el(rendered).querySelector('[data-node-id="row"].widget-stack') as HTMLElement;

    expect(row.style.alignItems).toBe('baseline');
  });
});
