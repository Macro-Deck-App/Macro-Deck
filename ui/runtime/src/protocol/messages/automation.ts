import { ResultResponse } from './common';

export interface IpcAutomation {
  id: string;
  name: string;
  description: string;
  enabled: boolean;
  flows: string;
  createdAt: string;
  updatedAt: string;
}

export interface GetAutomationsResponse {
  automations: IpcAutomation[];
}

export interface CreateAutomationRequest {
  name: string;
  description?: string;
  flows?: string;
}

export interface CreateAutomationResponse extends ResultResponse {
  automation?: IpcAutomation;
}

export interface UpdateAutomationRequest {
  id: string;
  name?: string;
  description?: string;
  flows?: string;
  enabled?: boolean;
}

export interface UpdateAutomationResponse extends ResultResponse {
  automation?: IpcAutomation;
}

export interface DuplicateAutomationResponse extends ResultResponse {
  automation?: IpcAutomation;
}

export interface DeleteAutomationResponse extends ResultResponse {}

export interface AutomationCreatedEvent {
  automation: IpcAutomation;
}

export interface AutomationUpdatedEvent {
  automation: IpcAutomation;
}

export interface AutomationDeletedEvent {
  id: string;
}
