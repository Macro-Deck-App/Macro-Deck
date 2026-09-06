import { InjectionToken } from '@angular/core';
import type { ConditionExpression, ParameterValue, VariableScope } from '@macro-deck/runtime';

export interface ConditionEvaluationResult {
  result: boolean;
  leftDisplay: string;
  rightDisplay: string;
}

export interface ComparisonLeafResult {
  result: boolean | null;
  leftDisplay: string;
  rightDisplay: string;
  error?: string;
}

export interface ExpressionEvaluationResult {
  result: boolean;
  leaves: Record<string, ComparisonLeafResult>;
}

export class TemplatePreviewError extends Error {
  readonly code?: string;

  constructor(message: string, code?: string) {
    super(message);
    this.name = 'TemplatePreviewError';
    this.code = code;
  }
}

export interface TemplatePreviewService {
  renderTemplate(template: string, scope?: VariableScope, scopeRefId?: string): Promise<string>;

  evaluateCondition(
    left: ParameterValue,
    operator: string,
    right: ParameterValue,
    scope?: VariableScope,
    scopeRefId?: string,
  ): Promise<ConditionEvaluationResult>;

  evaluateExpression(
    expression: ConditionExpression,
    scope?: VariableScope,
    scopeRefId?: string,
    eventId?: string,
  ): Promise<ExpressionEvaluationResult>;
}

export const TEMPLATE_PREVIEW_SERVICE = new InjectionToken<TemplatePreviewService>(
  'TEMPLATE_PREVIEW_SERVICE',
);
