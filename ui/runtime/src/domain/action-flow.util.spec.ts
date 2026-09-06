import { ActionBlock, ActionBlockDefinition, ActionBlockParameter, ActionFlow } from './action-builder.interface';
import {
  cloneBlockWithNewIds,
  collectSecretIds,
  hydrateBlockParameters,
  insertAfterBlock,
  mapSecretReferences,
  removeBlockFromFlows,
} from './action-flow.util';

function makeBlock(overrides: Partial<ActionBlock> = {}): ActionBlock {
  return {
    id: 'block-1',
    type: 'action',
    blockType: 'system.kill-process',
    label: 'Kill Application',
    color: '#000',
    parameters: [{ name: 'processName', type: 'string', label: 'Process', value: 'SteelSeriesGG' }],
    ...overrides,
  };
}

describe('cloneBlockWithNewIds', () => {
  it('assigns a fresh id and deep-copies the parameters', () => {
    const source = makeBlock();

    const clone = cloneBlockWithNewIds(source);

    expect(clone.id).not.toBe(source.id);
    expect(clone.parameters).not.toBe(source.parameters);
    expect(clone.parameters![0].value as string).toBe('SteelSeriesGG');
    expect(source.id).toBe('block-1');
  });

  it('re-ids every nested block and branch', () => {
    const source = makeBlock({
      id: 'if-1',
      type: 'condition',
      branches: [
        {
          id: 'branch-if',
          kind: 'if',
          children: [makeBlock({ id: 'nested-1' })],
        },
        { id: 'branch-else', kind: 'else', children: [] },
      ],
      children: [makeBlock({ id: 'child-1' })],
    });

    const clone = cloneBlockWithNewIds(source);

    expect(clone.branches![0].id).not.toBe('branch-if');
    expect(clone.branches![1].id).not.toBe('branch-else');
    expect(clone.branches![0].children[0].id).not.toBe('nested-1');
    expect(clone.children![0].id).not.toBe('child-1');
  });
});

describe('insertAfterBlock', () => {
  function flows(): ActionFlow[] {
    return [
      {
        triggerId: 't',
        triggerType: 'onShortPress',
        children: [
          makeBlock({ id: 'a' }),
          makeBlock({
            id: 'loop',
            type: 'loop',
            children: [makeBlock({ id: 'inner' })],
          }),
        ],
      },
    ];
  }

  it('inserts directly after a root-level block', () => {
    const tree = flows();

    const inserted = insertAfterBlock(tree, 'a', makeBlock({ id: 'copy' }));

    expect(inserted).toBeTrue();
    expect(tree[0].children.map(b => b.id)).toEqual(['a', 'copy', 'loop']);
  });

  it('inserts inside a nested list', () => {
    const tree = flows();

    const inserted = insertAfterBlock(tree, 'inner', makeBlock({ id: 'copy' }));

    expect(inserted).toBeTrue();
    expect(tree[0].children[1].children!.map(b => b.id)).toEqual(['inner', 'copy']);
  });

  it('returns false when the anchor block does not exist', () => {
    const tree = flows();

    expect(insertAfterBlock(tree, 'missing', makeBlock({ id: 'copy' }))).toBeFalse();
    expect(tree[0].children.length).toBe(2);
  });
});

describe('collectSecretIds / mapSecretReferences', () => {
  it('finds secret references in parameters and nested values', () => {
    const block = makeBlock({
      parameters: [
        { name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } },
        {
          name: 'config',
          type: 'object',
          label: 'Config',
          value: { nested: { $secret: 'secret-2' }, list: [{ $secret: 'secret-1' }] },
        },
      ],
    });

    expect(collectSecretIds(block)).toEqual(['secret-1', 'secret-2', 'secret-1']);
  });

  it('rewrites mapped references and drops unmapped ones to null', () => {
    const block = makeBlock({
      parameters: [
        { name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } },
        { name: 'other', type: 'secret', label: 'Other', value: { $secret: 'secret-2' } },
      ],
    });
    const map = new Map<string, string | null>([
      ['secret-1', 'secret-1-copy'],
      ['secret-2', null],
    ]);

    const mapped = mapSecretReferences(block, map);

    expect(mapped.parameters![0].value as object).toEqual({ $secret: 'secret-1-copy' });
    expect(mapped.parameters![1].value).toBeNull();
    expect(mapped.label).toBe('Kill Application');
  });
});

describe('removeBlockFromFlows', () => {
  function container(id: string, children: ActionBlock[], branchChildren: ActionBlock[]): ActionBlock {
    return {
      id,
      type: 'condition',
      blockType: 'flow.if',
      label: 'If',
      color: '#000',
      children,
      branches: [{ id: `${id}-branch`, kind: 'if', children: branchChildren }],
    };
  }

  function flowsWith(...children: ActionBlock[]): ActionFlow[] {
    return [{ triggerId: 't', triggerType: 'onShortPress', children }];
  }

  it('removes a top-level block and leaves its siblings', () => {
    const result = removeBlockFromFlows(
      flowsWith(makeBlock({ id: 'a' }), makeBlock({ id: 'b' })),
      'a',
    );

    expect(result.removed).toBeTrue();
    expect(result.flows[0].children.map(c => c.id)).toEqual(['b']);
  });

  it('removes a block nested in a loop body and in a branch', () => {
    const flows = flowsWith(
      container('c1', [makeBlock({ id: 'in-body' }), makeBlock({ id: 'body-sibling' })],
        [makeBlock({ id: 'in-branch' })]),
    );

    const body = removeBlockFromFlows(flows, 'in-body');
    expect(body.removed).toBeTrue();
    expect(body.flows[0].children[0].children!.map(c => c.id)).toEqual(['body-sibling']);

    const branch = removeBlockFromFlows(flows, 'in-branch');
    expect(branch.removed).toBeTrue();
    expect(branch.flows[0].children[0].branches![0].children).toEqual([]);
  });

  it('reports nothing removed for an unknown id and leaves the tree alone', () => {
    const flows = flowsWith(makeBlock({ id: 'a' }));

    const result = removeBlockFromFlows(flows, 'missing');

    expect(result.removed).toBeFalse();
    expect(result.flows[0].children.map(c => c.id)).toEqual(['a']);
  });

  it('does not mutate the flows it was given', () => {
    const flows = flowsWith(makeBlock({ id: 'a' }), makeBlock({ id: 'b' }));

    removeBlockFromFlows(flows, 'a');

    expect(flows[0].children.map(c => c.id)).toEqual(['a', 'b']);
  });
});

describe('hydrateBlockParameters', () => {
  function definition(): ActionBlockDefinition {
    return {
      blockType: 'obs.set-scene',
      type: 'action',
      label: 'Set Scene',
      color: '#3b82f6',
      category: 'OBS',
      integrationId: 'obs',
      actionId: 'set-scene',
      parameters: [
        { name: 'sceneName', type: 'string', label: 'Scene Name', defaultValue: 'Default Scene' },
        { name: 'fadeMs', type: 'number', label: 'Fade Duration', defaultValue: 500 },
        { name: 'folderId', type: 'dynamic-choice', label: 'Folder' },
      ],
    };
  }

  function migratedBlock(overrides: Partial<ActionBlock> = {}): ActionBlock {
    return {
      id: 'b1',
      type: 'action',
      blockType: 'obs.set-scene',
      label: 'undefined',
      color: '#000',
      integrationId: 'obs',
      actionId: 'set-scene',
      parameters: [{ name: 'sceneName', type: 'string', label: '', value: 'Intro' }],
      ...overrides,
    };
  }

  function lookup(defs: ActionBlockDefinition[]) {
    return (integrationId: string, actionId: string) =>
      defs.find(d => d.integrationId === integrationId && d.actionId === actionId);
  }

  it('gains type and label from the definition and keeps the stored value', () => {
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [migratedBlock()] }];

    const hydrated = hydrateBlockParameters(flows, lookup([definition()]));

    const scene = hydrated[0].children[0].parameters!.find(p => p.name === 'sceneName')!;
    expect(scene.type).toBe('string');
    expect(scene.label).toBe('Scene Name');
    expect(scene.value as string).toBe('Intro');
  });

  it('adds a definition parameter the block never stored, with the definition default', () => {
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [migratedBlock()] }];

    const hydrated = hydrateBlockParameters(flows, lookup([definition()]));

    const fade = hydrated[0].children[0].parameters!.find(p => p.name === 'fadeMs')!;
    expect(fade.label).toBe('Fade Duration');
    expect(fade.value as number).toBe(500);
  });

  it('keeps a stored parameter the definition does not declare, appended after the declared ones', () => {
    const block = migratedBlock({
      parameters: [
        { name: 'sceneName', type: 'string', label: '', value: 'Intro' },
        { name: 'legacyFlag', type: 'boolean', label: '', value: true },
      ],
    });
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [block] }];

    const hydrated = hydrateBlockParameters(flows, lookup([definition()]));

    const names = hydrated[0].children[0].parameters!.map(p => p.name);
    expect(names).toEqual(['sceneName', 'fadeMs', 'folderId', 'legacyFlag']);
    const legacy = hydrated[0].children[0].parameters!.find(p => p.name === 'legacyFlag')!;
    expect(legacy.value as boolean).toBe(true);
  });

  it('leaves a block untouched when its integration is unknown', () => {
    const block = migratedBlock({ integrationId: 'missing-integration' });
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [block] }];

    const hydrated = hydrateBlockParameters(flows, lookup([definition()]));

    expect(hydrated[0].children[0].parameters).toEqual(block.parameters);
  });

  it('hydrates nested blocks inside children and branches', () => {
    const parametersWithFolder: ActionBlockParameter[] = [
      { name: 'sceneName', type: 'string', label: '', value: 'Intro' },
      { name: 'folderId', type: 'dynamic-choice', label: '', value: 'guid-123', valueLabel: 'Living Room' },
    ];
    const nestedInLoop = migratedBlock({ id: 'in-loop', parameters: parametersWithFolder });
    const nestedInBranch = migratedBlock({ id: 'in-branch', parameters: parametersWithFolder });
    const flows: ActionFlow[] = [
      {
        triggerId: 't',
        triggerType: 'onShortPress',
        children: [
          {
            id: 'loop',
            type: 'loop',
            blockType: 'flow.repeat',
            label: 'Repeat',
            color: '#000',
            children: [nestedInLoop],
          },
          {
            id: 'if',
            type: 'condition',
            blockType: 'flow.if',
            label: 'If',
            color: '#000',
            branches: [{ id: 'branch-if', kind: 'if', children: [nestedInBranch] }],
          },
        ],
      },
    ];

    const hydrated = hydrateBlockParameters(flows, lookup([definition()]));

    const inLoop = hydrated[0].children[0].children![0];
    const inBranch = hydrated[0].children[1].branches![0].children[0];
    expect(inLoop.parameters!.find(p => p.name === 'fadeMs')?.value as number).toBe(500);
    expect(inBranch.parameters!.find(p => p.name === 'fadeMs')?.value as number).toBe(500);
    expect(inLoop.parameters!.find(p => p.name === 'folderId')?.valueLabel).toBe('Living Room');
    expect(inBranch.parameters!.find(p => p.name === 'folderId')?.valueLabel).toBe('Living Room');
  });

  it('keeps the cached label while rebuilding the descriptor', () => {
    const block = migratedBlock({
      parameters: [{ name: 'folderId', type: 'dynamic-choice', label: '', value: 'guid-123', valueLabel: 'Living Room' }],
    });
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [block] }];

    const hydrated = hydrateBlockParameters(flows, lookup([definition()]));

    const folder = hydrated[0].children[0].parameters!.find(p => p.name === 'folderId')!;
    expect(folder.value as string).toBe('guid-123');
    expect(folder.valueLabel).toBe('Living Room');
    expect(folder.label).toBe('Folder');
  });

  it('a definition-supplied parameter comes back unlabelled', () => {
    const def: ActionBlockDefinition = {
      ...definition(),
      parameters: [
        { name: 'deviceId', type: 'dynamic-choice', label: 'Device', defaultValue: 'dev-default' },
      ],
    };
    const block = migratedBlock({ parameters: [] });
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [block] }];

    const hydrated = hydrateBlockParameters(flows, lookup([def]));

    const device = hydrated[0].children[0].parameters!.find(p => p.name === 'deviceId')!;
    expect(device.value as string).toBe('dev-default');
    expect(device.label).toBe('Device');
    expect(device.valueLabel).toBeUndefined();
  });

  it('matches the stored parameter by name, not by position', () => {
    const def: ActionBlockDefinition = {
      ...definition(),
      parameters: [
        { name: 'sceneName', type: 'string', label: 'Scene Name', defaultValue: 'Default Scene' },
        { name: 'fadeMs', type: 'number', label: 'Fade Duration', defaultValue: 500 },
        { name: 'folderId', type: 'dynamic-choice', label: 'Folder' },
      ],
    };
    const block = migratedBlock({
      parameters: [{ name: 'folderId', type: 'dynamic-choice', label: '', value: 'guid-123', valueLabel: 'Living Room' }],
    });
    const flows: ActionFlow[] = [{ triggerId: 't', triggerType: 'onShortPress', children: [block] }];

    const hydrated = hydrateBlockParameters(flows, lookup([def]));

    const params = hydrated[0].children[0].parameters!;
    expect(params.find(p => p.name === 'sceneName')?.valueLabel).toBeUndefined();
    expect(params.find(p => p.name === 'fadeMs')?.valueLabel).toBeUndefined();
    const folder = params.find(p => p.name === 'folderId')!;
    expect(folder.valueLabel).toBe('Living Room');
    expect(folder.value as string).toBe('guid-123');
  });
});
