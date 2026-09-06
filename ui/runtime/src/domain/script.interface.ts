import { ActionFlow } from './action-builder.interface';
import type { VariableType } from './variable.interface';

export const SCRIPT_TRIGGER_TYPE = 'onRun';

export type ScriptInputType = VariableType;

export interface ScriptInput {
  // Sanitized to ^[a-z][a-z0-9_]*$, because it has to be readable as vars.<name>.
  name: string;
  type: ScriptInputType;
  label?: string;
  description?: string;
  required?: boolean;
  defaultValue?: string;
}

export interface Script {
  id: string;
  name: string;
  description: string;
  flows: ActionFlow[];
  inputs: ScriptInput[];
  runsOnWidget: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ScriptUsage {
  kind: 'widget' | 'script';
  location: string;
  count: number;
}

export function scriptFlow(script: Pick<Script, 'flows'>): ActionFlow | undefined {
  return script.flows.find(flow => flow.triggerType === SCRIPT_TRIGGER_TYPE);
}

export function scriptActionCount(script: Pick<Script, 'flows'>): number {
  return scriptFlow(script)?.children.length ?? 0;
}
