import { ActionBlock, ActionBranch, ActionFlow, ComparisonOperator, ConditionExpression, WIDGET_TARGET_SELF } from '@macro-deck/runtime';
import { isConditionComplete, validateActionFlows } from './action-flow-validation.util';

function block(overrides: Partial<ActionBlock> = {}): ActionBlock {
  return {
    id: 'block-1',
    type: 'action',
    blockType: 'system.run',
    label: 'Run Application',
    color: '#000',
    parameters: [{ name: 'path', type: 'string', label: 'Path', value: '' }],
    ...overrides,
  };
}

function flow(...children: ActionBlock[]): ActionFlow {
  return { triggerId: 't', triggerType: 'onShortPress', children };
}

describe('validateActionFlows for switched-off blocks', () => {
  it('reports an unfilled parameter on an enabled block', () => {
    const result = validateActionFlows([flow(block())]);

    expect(result.valid).toBeFalse();
    expect(result.errors[0].paramName).toBe('path');
  });

  it('ignores an unfilled parameter on a disabled block', () => {
    const result = validateActionFlows([flow(block({ disabled: true }))]);

    expect(result.valid).toBeTrue();
  });

  it('ignores the subtree of a disabled container, which does not run either', () => {
    const result = validateActionFlows([
      flow(block({
        id: 'container',
        type: 'loop',
        blockType: 'repeatLoop',
        parameters: [],
        disabled: true,
        children: [block()],
      })),
    ]);

    expect(result.valid).toBeTrue();
  });

  it('still reports an enabled sibling of a disabled block', () => {
    const result = validateActionFlows([
      flow(block({ id: 'off', disabled: true }), block({ id: 'on' })),
    ]);

    expect(result.valid).toBeFalse();
    expect(result.errors.map(e => e.blockId)).toEqual(['on']);
  });
});

describe('validateActionFlows URL validation', () => {
  function urlFlow(value: string): ActionFlow {
    return flow(block({
      parameters: [{ name: 'url', type: 'url', label: 'Website URL', value, required: true }],
    }));
  }

  it('rejects an HTTP(S) URL with only one slash after its protocol', () => {
    const result = validateActionFlows([urlFlow('https:/macro-deck.app')]);

    expect(result.valid).toBeFalse();
    expect(result.errors[0].message).toBe('Website URL is not a valid URL');
  });

  it('accepts a correctly formed HTTPS URL', () => {
    expect(validateActionFlows([urlFlow('https://macro-deck.app')]).valid).toBeTrue();
  });
});

describe('validateActionFlows legacy Widget font sizes', () => {
  it('accepts zero as the persisted Unchanged sentinel while concrete values start at one', () => {
    const result = validateActionFlows([flow(block({
      blockType: 'app.macro-deck.widget.set-font',
      parameters: [{ name: 'fontSize', type: 'number', label: 'Size', value: 0, min: 0, max: 100 }],
    }))]);

    expect(result.valid).toBeTrue();
  });
});

describe('validateActionFlows for a widget target', () => {
  function widgetAction(value: string): ActionFlow {
    return flow(block({
      blockType: 'app.macro-deck.widget.set-label',
      parameters: [{ name: 'widget', type: 'widget-target', label: 'Widget', value, required: true }],
    }));
  }

  it('accepts "this widget" inside a widget flow', () => {
    const result = validateActionFlows([widgetAction(WIDGET_TARGET_SELF)], { hasOwnerWidget: true });

    expect(result.valid).toBeTrue();
  });

  it('rejects "this widget" in a script or automation, which owns none', () => {
    const result = validateActionFlows([widgetAction(WIDGET_TARGET_SELF)], { hasOwnerWidget: false });

    expect(result.valid).toBeFalse();
    expect(result.errors[0].paramName).toBe('widget');
  });

  it('defaults to having no owning widget when the caller does not say', () => {
    expect(validateActionFlows([widgetAction(WIDGET_TARGET_SELF)]).valid).toBeFalse();
  });

  it('accepts a named widget anywhere', () => {
    for (const hasOwnerWidget of [true, false]) {
      expect(validateActionFlows([widgetAction('a-widget-id')], { hasOwnerWidget }).valid)
        .withContext(`hasOwnerWidget=${hasOwnerWidget}`)
        .toBeTrue();
    }
  });

  it('rejects a cleared target wherever it is', () => {
    expect(validateActionFlows([widgetAction('')], { hasOwnerWidget: true }).valid).toBeFalse();
  });
});

describe('validateActionFlows for conditionally visible parameters', () => {
  function bodyFlow(bodyType: string): ActionFlow {
    return flow(block({
      blockType: 'app.macro-deck.http.send-request',
      parameters: [
        { name: 'bodyType', type: 'choice', label: 'Body type', value: bodyType },
        {
          name: 'jsonBody',
          type: 'json',
          label: 'JSON body',
          value: '',
          required: true,
          visibleWhen: { parameterName: 'bodyType', values: ['json'] },
        },
      ],
    }));
  }

  it('ignores a hidden required parameter', () => {
    expect(validateActionFlows([bodyFlow('none')]).valid).toBeTrue();
  });

  it('still enforces the parameter once its condition is met', () => {
    const result = validateActionFlows([bodyFlow('json')]);

    expect(result.valid).toBeFalse();
    expect(result.errors[0].paramName).toBe('jsonBody');
  });

  it('skips the format check for a hidden parameter', () => {
    const result = validateActionFlows([flow(block({
      blockType: 'app.macro-deck.http.send-request',
      parameters: [
        { name: 'bodyType', type: 'choice', label: 'Body type', value: 'text' },
        {
          name: 'jsonBody',
          type: 'json',
          label: 'JSON body',
          value: 'not json at all',
          required: false,
          visibleWhen: { parameterName: 'bodyType', values: ['json'] },
        },
      ],
    }))]);

    expect(result.valid).toBeTrue();
  });
});

function ifFlow(...branches: Partial<ActionBranch>[]): ActionFlow {
  return flow(block({
    id: 'if-block',
    type: 'condition',
    blockType: 'ifElse',
    parameters: [],
    branches: branches.map((b, i) => ({
      id: `branch-${i}`,
      kind: i === 0 ? 'if' : 'elseif',
      children: [],
      ...b,
    })),
  }));
}

describe('validateActionFlows for state-operator conditions (issue #876)', () => {
  it('D2: an isNotEmpty condition with a variable left operand and a blank right operand validates clean', () => {
    const result = validateActionFlows([ifFlow({
      condition: { kind: 'compare', id: 'c1', left: { $var: 'artist' }, operator: 'isNotEmpty', right: '' },
    })]);

    expect(result.valid).toBeTrue();
    expect(result.errors.length).toBe(0);
    expect(result.errors.some(e => e.message === 'macrodeck.app:ActionBuilder.Validation.ConditionRightEmpty'))
      .toBeFalse();
  });

  it('D2 companion: the identical comparison with == still requires the right operand', () => {
    const result = validateActionFlows([ifFlow({
      condition: { kind: 'compare', id: 'c1', left: { $var: 'artist' }, operator: '==', right: '' },
    })]);

    expect(result.errors.length).toBe(1);
    expect(result.errors[0].message).toBe('macrodeck.app:ActionBuilder.Validation.ConditionRightEmpty');
  });

  it('D3: a state operator with an empty left operand is still meaningless', () => {
    const result = validateActionFlows([ifFlow({
      condition: { kind: 'compare', id: 'c1', left: '', operator: 'isEmpty', right: '' },
    })]);

    expect(result.valid).toBeFalse();
    expect(result.errors.length).toBe(1);
    expect(result.errors[0].message).toBe('macrodeck.app:ActionBuilder.Validation.ConditionLeftEmpty');
  });

  it('D4: a state operator requires a variable/event reference on the left, never rendered free text', () => {
    const result = validateActionFlows([ifFlow(
      { id: 'branch-var', kind: 'if', condition: { kind: 'compare', id: 'c-var', left: { $var: 'artist' }, operator: 'isNotEmpty', right: '' } },
      { id: 'branch-event', kind: 'elseif', condition: { kind: 'compare', id: 'c-event', left: { $event: 'artist' }, operator: 'isNotEmpty', right: '' } },
      { id: 'branch-text', kind: 'elseif', condition: { kind: 'compare', id: 'c-text', left: '{{ vars.artist }}', operator: 'isNotEmpty', right: '' } },
    )]);

    expect(result.errors.length).toBe(1);
    const error = result.errors[0];
    expect(error.message).toBe('macrodeck.app:ActionBuilder.Validation.ConditionStateOperandNotVariable');
    expect(error.blockId).toBe('if-block');
    expect(error.branchId).toBe('branch-text');
  });
});

describe('isConditionComplete vs validateActionFlows agreement (issue #876, D5)', () => {
  const ALL_OPERATORS: ComparisonOperator[] = [
    '==', '!=', '>', '<', '>=', '<=', 'contains', 'startsWith',
    'isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable',
  ];

  function isValid(expr: ConditionExpression): boolean {
    return validateActionFlows([ifFlow({ condition: expr })]).errors.length === 0;
  }

  it('never drifts, across every operator x right-operand x left-operand combination', () => {
    for (const operator of ALL_OPERATORS) {
      for (const right of ['', 'x']) {
        for (const left of [{ $var: 'a' }, '']) {
          const expr: ConditionExpression = { kind: 'compare', id: 'c1', left, operator, right };
          expect(isConditionComplete(expr))
            .withContext(`operator=${operator} left=${JSON.stringify(left)} right=${JSON.stringify(right)}`)
            .toBe(isValid(expr));
        }
      }
    }
  });

  it('D5: a variable left operand with isNotEmpty and a blank right is complete and valid', () => {
    const expr: ConditionExpression = { kind: 'compare', id: 'c1', left: { $var: 'a' }, operator: 'isNotEmpty', right: '' };
    expect(isConditionComplete(expr)).toBeTrue();
    expect(isValid(expr)).toBeTrue();
  });

  it('D5: an empty left operand with isNotEmpty and a blank right is incomplete and invalid', () => {
    const expr: ConditionExpression = { kind: 'compare', id: 'c1', left: '', operator: 'isNotEmpty', right: '' };
    expect(isConditionComplete(expr)).toBeFalse();
    expect(isValid(expr)).toBeFalse();
  });
});
