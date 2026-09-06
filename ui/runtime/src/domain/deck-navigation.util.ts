import { ActionBlock, ActionFlow, EVENT_TRIGGER_TYPE } from './action-builder.interface';
import { containsLiquid } from './liquid.util';

export type LocalDeckNavigation =
  | { command: 'changeTo'; folderId: string }
  | { command: 'parent' }
  | { command: 'back' };

const DECK_INTEGRATION_ID = 'app.macro-deck.deck';

const DECK_NAV_ACTIONS: Record<string, 'changeTo' | 'parent' | 'back'> = {
  'change-folder': 'changeTo',
  'go-to-parent': 'parent',
  'go-back': 'back'
};

export function findFlowForTrigger(flows: readonly ActionFlow[], triggerType: string): ActionFlow | undefined {
  if (triggerType.toLowerCase() === EVENT_TRIGGER_TYPE.toLowerCase()) return undefined;
  return flows.find(f => f.triggerType.toLowerCase() === triggerType.toLowerCase());
}

export function resolveLocalDeckNavigation(
  flows: readonly ActionFlow[] | undefined,
  triggerType: string
): LocalDeckNavigation | null {
  if (!Array.isArray(flows)) return null;

  const flow = findFlowForTrigger(flows, triggerType);
  if (!flow || flow.children.length !== 1) return null;

  const block = flow.children[0];
  if (!isPlainActionBlock(block)) return null;

  const command = DECK_NAV_ACTIONS[resolveDeckActionId(block) ?? ''];
  if (!command) return null;

  if (command !== 'changeTo') {
    return { command };
  }

  const folderId = literalStringParameter(block, 'folderId');
  return folderId ? { command, folderId } : null;
}

function isPlainActionBlock(block: ActionBlock): boolean {
  return block.type === 'action'
    && !block.condition
    && !(block.children && block.children.length > 0)
    && !(block.branches && block.branches.length > 0);
}

function resolveDeckActionId(block: ActionBlock): string | null {
  if (block.integrationId || block.actionId) {
    return block.integrationId === DECK_INTEGRATION_ID && block.actionId ? block.actionId : null;
  }
  const prefix = `${DECK_INTEGRATION_ID}.`;
  return block.blockType.startsWith(prefix) ? block.blockType.slice(prefix.length) : null;
}

function literalStringParameter(block: ActionBlock, name: string): string | null {
  const parameter = block.parameters?.find(p => p.name === name);
  const value = parameter?.value;
  if (typeof value !== 'string' || value.length === 0 || containsLiquid(value)) {
    return null;
  }
  return value;
}
