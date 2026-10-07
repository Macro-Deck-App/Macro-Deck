import type { VariableScope, VariableTemplateError, VariableType } from '../../domain/variable.interface';
import { ResultResponse } from './common';

export interface RenderTemplateRequest {
  template: string;
  scope?: VariableScope;
  scopeRefId?: string;
  resultType?: VariableType;
  decimalPlaces?: number;
  variableId?: string;
  variableName?: string;
}

export interface RenderTemplateResponse extends ResultResponse {
  rendered: string;
  value?: string | null;
  templateError?: VariableTemplateError | null;
}

export interface ConditionSidePayload {
  value: unknown;
}

export interface EvaluateConditionRequest {
  left: ConditionSidePayload;
  operator: string;
  right: ConditionSidePayload;
  scope?: VariableScope;
  scopeRefId?: string;
}

export interface EvaluateConditionResponse extends ResultResponse {
  result: boolean;
  leftDisplay: string;
  rightDisplay: string;
}

export interface ComparisonExpressionPayload {
  kind: 'compare';
  id: string;
  left: ConditionSidePayload;
  operator: string;
  right: ConditionSidePayload;
}

export interface LogicalExpressionPayload {
  kind: 'and' | 'or';
  id: string;
  operands: ConditionExpressionPayload[];
}

export type ConditionExpressionPayload =
  | ComparisonExpressionPayload
  | LogicalExpressionPayload;

export interface EvaluateExpressionRequest {
  eventId?: string;
  expression: ConditionExpressionPayload;
  scope?: VariableScope;
  scopeRefId?: string;
}

export interface ComparisonLeafEvaluation {
  result: boolean | null;
  leftDisplay: string;
  rightDisplay: string;
  error?: string;
}

export interface EvaluateExpressionResponse extends ResultResponse {
  result: boolean;
  leaves: Record<string, ComparisonLeafEvaluation>;
}
