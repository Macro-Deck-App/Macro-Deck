import { ActionBlock, ActionFlow } from './action-builder.interface';
import { findFlowForTrigger, resolveLocalDeckNavigation } from './deck-navigation.util';

describe('deck-navigation.util', () => {
  const targetFolderId = '22222222-2222-2222-2222-222222222222';

  function navBlock(overrides: Partial<ActionBlock> = {}): ActionBlock {
    return {
      id: 'b1',
      type: 'action',
      blockType: 'app.macro-deck.deck.change-folder',
      label: 'Change Folder to',
      color: '#000',
      integrationId: 'app.macro-deck.deck',
      actionId: 'change-folder',
      parameters: [{ name: 'folderId', type: 'dynamic-choice', value: targetFolderId, label: 'Folder' }],
      ...overrides
    };
  }

  function flow(children: ActionBlock[], triggerType = 'onShortPress'): ActionFlow {
    return { triggerId: 't1', triggerType, children };
  }

  describe('findFlowForTrigger', () => {
    it('matches the trigger type case-insensitively like the host', () => {
      const flows = [flow([navBlock()], 'onshortpress')];
      expect(findFlowForTrigger(flows, 'onShortPress')).toBe(flows[0]);
    });

    it('returns undefined when no flow matches', () => {
      expect(findFlowForTrigger([flow([navBlock()])], 'onLongPress')).toBeUndefined();
    });
  });

  describe('resolveLocalDeckNavigation', () => {
    it('resolves a single change-folder action with a literal target', () => {
      expect(resolveLocalDeckNavigation([flow([navBlock()])], 'onShortPress'))
        .toEqual({ command: 'changeTo', folderId: targetFolderId });
    });

    it('resolves go-to-parent and go-back', () => {
      const parent = navBlock({
        blockType: 'app.macro-deck.deck.go-to-parent',
        actionId: 'go-to-parent',
        parameters: []
      });
      const back = navBlock({ blockType: 'app.macro-deck.deck.go-back', actionId: 'go-back', parameters: [] });
      expect(resolveLocalDeckNavigation([flow([parent])], 'onShortPress')).toEqual({ command: 'parent' });
      expect(resolveLocalDeckNavigation([flow([back])], 'onShortPress')).toEqual({ command: 'back' });
    });

    it('resolves the deck action from the blockType when integrationId/actionId are absent', () => {
      const block = navBlock({ integrationId: undefined, actionId: undefined });
      expect(resolveLocalDeckNavigation([flow([block])], 'onShortPress'))
        .toEqual({ command: 'changeTo', folderId: targetFolderId });
    });

    it('returns null when the flow has more than one block', () => {
      expect(resolveLocalDeckNavigation([flow([navBlock(), navBlock({ id: 'b2' })])], 'onShortPress')).toBeNull();
    });

    it('returns null for non-deck actions', () => {
      const block = navBlock({
        blockType: 'app.macro-deck.system.run',
        integrationId: 'app.macro-deck.system',
        actionId: 'run'
      });
      expect(resolveLocalDeckNavigation([flow([block])], 'onShortPress')).toBeNull();
    });

    it('returns null when the target is a variable reference or a template', () => {
      const reference = navBlock({
        parameters: [{ name: 'folderId', type: 'dynamic-choice', value: { $var: 'target' }, label: 'Folder' }]
      });
      const template = navBlock({
        parameters: [{ name: 'folderId', type: 'dynamic-choice', value: '{{ vars.target }}', label: 'Folder' }]
      });
      expect(resolveLocalDeckNavigation([flow([reference])], 'onShortPress')).toBeNull();
      expect(resolveLocalDeckNavigation([flow([template])], 'onShortPress')).toBeNull();
    });

    it('returns null when the target is a control-flow-only template (no {{ }})', () => {
      const controlFlow = navBlock({
        parameters: [{ name: 'folderId', type: 'dynamic-choice', value: '{% if true %}x{% endif %}', label: 'Folder' }]
      });
      expect(resolveLocalDeckNavigation([flow([controlFlow])], 'onShortPress')).toBeNull();
    });

    it('returns null when the block carries a condition or nested blocks', () => {
      const conditional = navBlock({
        condition: { kind: 'compare', id: 'c1', left: '1', operator: '==', right: '1' }
      });
      const container = navBlock({ children: [navBlock({ id: 'b2' })] });
      expect(resolveLocalDeckNavigation([flow([conditional])], 'onShortPress')).toBeNull();
      expect(resolveLocalDeckNavigation([flow([container])], 'onShortPress')).toBeNull();
    });

    it('returns null when flows are missing or no flow matches the trigger', () => {
      expect(resolveLocalDeckNavigation(undefined, 'onShortPress')).toBeNull();
      expect(resolveLocalDeckNavigation([flow([navBlock()])], 'onLongPress')).toBeNull();
    });
  });
});

describe('findFlowForTrigger with event triggers', () => {
  it('never resolves an event trigger', () => {
    const flows: ActionFlow[] = [
      { triggerId: 'a', triggerType: 'onEvent', children: [] },
      { triggerId: 'b', triggerType: 'onEvent', children: [] },
    ];

    expect(findFlowForTrigger(flows, 'onEvent')).toBeUndefined();
    expect(findFlowForTrigger(flows, 'ONEVENT')).toBeUndefined();
  });

  it('still resolves a press trigger', () => {
    const flows: ActionFlow[] = [
      { triggerId: 'a', triggerType: 'onEvent', children: [] },
      { triggerId: 'p', triggerType: 'onShortPress', children: [] },
    ];

    expect(findFlowForTrigger(flows, 'onShortPress')?.triggerId).toBe('p');
  });
});
