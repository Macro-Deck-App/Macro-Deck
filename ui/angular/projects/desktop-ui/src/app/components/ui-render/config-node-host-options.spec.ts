import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import type { GetActionParameterOptionsRequest, UiNode } from '@macro-deck/runtime';
import { ActionOptionsService } from '../../services/action-options.service';
import { SelectComponent } from '../forms/select/select.component';
import { renderTree, tick } from './ui-render-test-support';

describe('shared-ui-node options a provider tree asks the host for', () => {
  let requests: GetActionParameterOptionsRequest[];

  const fakeOptions = {
    getOptions: async (request: GetActionParameterOptionsRequest) => {
      requests.push(request);
      return { options: [{ value: 'inter-bold', label: 'Inter Bold' }] };
    },
  };

  beforeEach(() => (requests = []));
  afterEach(() => TestBed.resetTestingModule());

  function dynamicChoice(properties: Record<string, unknown>): UiNode {
    return { id: 'fontFaceId', type: 'dynamic-choice', properties: { label: 'Font', ...properties } };
  }

  async function render(node: UiNode) {
    const rendered = await renderTree(node, null, [{ provide: ActionOptionsService, useValue: fakeOptions }]);
    await tick(rendered);
    await tick(rendered);
    return rendered.fixture.debugElement.query(By.directive(SelectComponent)).componentInstance as SelectComponent;
  }

  it('lists the host fonts for the macrodeck.fonts source', async () => {
    const select = await render(dynamicChoice({ optionsSourceId: 'macrodeck.fonts', dynamicOptions: true }));

    expect(requests.map(request => request.optionsSourceId)).toEqual(['macrodeck.fonts']);
    expect(select.options).toEqual([{ value: 'inter-bold', label: 'Inter Bold' }]);
  });

  it('does not ask the host for another Macro Deck source', async () => {
    await render(dynamicChoice({ optionsSourceId: 'macrodeck.variables', dynamicOptions: true }));

    expect(requests).toEqual([]);
  });

  it('keeps the options a tree ships itself', async () => {
    const select = await render(dynamicChoice({
      optionsSourceId: 'macrodeck.fonts',
      dynamicOptions: true,
      options: [{ value: 'own', label: 'Own' }],
    }));

    expect(requests).toEqual([]);
    expect(select.options).toEqual([{ value: 'own', label: 'Own' }]);
  });
});
