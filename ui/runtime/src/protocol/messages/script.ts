import type { ScriptInput } from '../../domain/script.interface';
import type { ScriptInputValue } from '../../domain/script-input.util';
import { ActionExecutionStatus, ActionOutcome } from './action';
import { ResultResponse } from './common';

export interface IpcScript {
  id: string;
  name: string;
  description: string;
  flows: string;
  inputs?: ScriptInput[];
  runsOnWidget?: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface GetScriptsResponse {
  scripts: IpcScript[];
}

export interface IpcScriptUsage {
  kind: 'widget' | 'script';
  location: string;
  count: number;
}

export interface GetScriptUsagesResponse {
  usages: IpcScriptUsage[];
}

export interface CreateScriptRequest {
  name: string;
  description?: string;
  flows?: string;
  inputs?: ScriptInput[];
}

export interface CreateScriptResponse extends ResultResponse {
  script?: IpcScript;
}

export interface UpdateScriptRequest {
  id: string;
  name?: string;
  description?: string;
  flows?: string;
  inputs?: ScriptInput[];
  runsOnWidget?: boolean;
}

export interface UpdateScriptResponse extends ResultResponse {
  script?: IpcScript;
}

export interface DuplicateScriptResponse extends ResultResponse {
  script?: IpcScript;
}

export interface DeleteScriptResponse extends ResultResponse {}

export interface RunScriptRequest {
  id: string;
  clientId?: string;
  inputs?: Record<string, ScriptInputValue>;
}

export interface RunScriptResponse extends ResultResponse {
  appliedInputs?: string[];
  executionId?: string;
  status?: ActionExecutionStatus;
  durationMs?: number;
  actions?: ActionOutcome[];
}

export interface ScriptCreatedEvent {
  script: IpcScript;
}

export interface ScriptUpdatedEvent {
  script: IpcScript;
}

export interface ScriptDeletedEvent {
  id: string;
}
