import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentDirections, UiComponents, UiMacroDeckComponents, UiComponentProperties } from '@macro-deck/runtime';
import { LocalizationService } from '../../localization';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick } from './ui-widget-render-test-support';

const Types = UiComponents;
const MacroDeckTypes = UiMacroDeckComponents;
const Props = UiComponentProperties;

const BASIS = 120;

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

const CLOCK: UiNode = {
  id: 'clock',
  type: Types.Stack,
  properties: {
    [Props.Direction]: UiComponentDirections.Vertical,
    [Props.Justify]: 'center',
    [Props.Align]: 'center',
    [Props.Gap]: { basis: 0.03 },
    [Props.Padding]: { basis: 0.08 },
  },
  children: [
    {
      id: 'time',
      type: MacroDeckTypes.DynamicText,
      properties: {
        [Props.Value]: { $time: {} },
        [Props.Format]: 'time',
        [Props.Seconds]: true,
        [Props.Size]: { basis: 0.24 },
        [Props.MinSize]: { basis: 0.13 },
        [Props.Align]: 'center',
      },
    },
    {
      id: 'date',
      type: MacroDeckTypes.DynamicText,
      properties: {
        [Props.Value]: { $time: {} },
        [Props.Format]: 'date',
        [Props.Size]: { basis: 0.08 },
        [Props.Align]: 'center',
      },
    },
  ],
};

async function renderAtScale(scale: number): Promise<RenderedTree> {
  const rendered = await renderTree(CLOCK, withBasis(BASIS));
  const host = el(rendered);
  host.style.transformOrigin = 'top left';
  host.style.transform = `scale(${scale})`;
  TestBed.inject(LocalizationService).culture.set('en-US');
  await tick(rendered);
  await tick(rendered);

  return rendered;
}

function time(rendered: RenderedTree): HTMLElement {
  return el(rendered).querySelector('[data-node-id="time"]') as HTMLElement;
}

function fittedSize(rendered: RenderedTree): number {
  return parseFloat(getComputedStyle(time(rendered)).fontSize);
}

describe('widget text fit under the deck scale', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows a clock with seconds whole on a deck that draws its tiles larger than the basis', async () => {
    const rendered = await renderAtScale(1.9);

    const run = time(rendered);

    expect(run.scrollWidth).toBeLessThanOrEqual(run.clientWidth);
  });

  it('fits a reading to the same size however large the deck draws the tile', async () => {
    const unscaled = fittedSize(await renderAtScale(1));
    const enlarged = fittedSize(await renderAtScale(1.9));
    const reduced = fittedSize(await renderAtScale(0.6));

    expect(enlarged).toBeCloseTo(unscaled, 0);
    expect(reduced).toBeCloseTo(unscaled, 0);
  });

  it('leaves a reading that already fits at the size it declares', async () => {
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Vertical },
        children: [
          {
            id: 'short',
            type: Types.Text,
            properties: { [Props.Text]: '7', [Props.Size]: { basis: 0.1 }, [Props.MinSize]: { basis: 0.05 } },
          },
        ],
      },
      withBasis(BASIS),
    );
    await tick(rendered);

    const short = el(rendered).querySelector('[data-node-id="short"]') as HTMLElement;

    expect(parseFloat(getComputedStyle(short).fontSize)).toBeCloseTo(BASIS * 0.1, 1);
  });
});
