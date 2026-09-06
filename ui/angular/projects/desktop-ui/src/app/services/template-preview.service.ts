import { Injectable, inject } from '@angular/core';
import { AppStrings, isComparisonExpression } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import type { ConditionExpression, ConditionExpressionPayload, ParameterValue, VariableScope } from '@macro-deck/runtime';
import { TemplatePreviewError, ConditionEvaluationResult, ExpressionEvaluationResult, TemplatePreviewService } from '../domain/template-preview.interface';

@Injectable({ providedIn: 'root' })
export class TemplatePreviewApiService implements TemplatePreviewService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  async renderTemplate(template: string, scope?: VariableScope, scopeRefId?: string): Promise<string> {
    const response = await this.renderTemplateResponse(template, scope, scopeRefId);
    if (!response.success) {
      throw new TemplatePreviewError(
        response.error?.message ?? this.localization.translateKey(AppStrings.Errors.TemplatePreview.RenderFailed),
        response.error?.code,
      );
    }
    return response.rendered;
  }

  private async renderTemplateResponse(template: string, scope?: VariableScope, scopeRefId?: string) {
    try {
      return await this.api.renderTemplate({ template, scope, scopeRefId });
    } catch {
      throw new TemplatePreviewError(
        this.localization.translateKey(AppStrings.Errors.TemplatePreview.RenderFailed),
        'NETWORK_ERROR',
      );
    }
  }

  async evaluateCondition(
    left: ParameterValue,
    operator: string,
    right: ParameterValue,
    scope?: VariableScope,
    scopeRefId?: string,
  ): Promise<ConditionEvaluationResult> {
    const response = await this.api.evaluateCondition({
      left: { value: left as unknown },
      operator,
      right: { value: right as unknown },
      scope,
      scopeRefId,
    });
    if (!response.success) {
      throw new Error(
        response.error?.message
          ?? this.localization.translateKey(AppStrings.Errors.TemplatePreview.ConditionEvaluationFailed),
      );
    }
    return {
      result: response.result,
      leftDisplay: response.leftDisplay,
      rightDisplay: response.rightDisplay,
    };
  }

  async evaluateExpression(
    expression: ConditionExpression,
    scope?: VariableScope,
    scopeRefId?: string,
    eventId?: string,
  ): Promise<ExpressionEvaluationResult> {
    const response = await this.api.evaluateExpression({
      expression: toExpressionPayload(expression),
      scope,
      scopeRefId,
      eventId,
    });
    if (!response.success) {
      throw new Error(
        response.error?.message
          ?? this.localization.translateKey(AppStrings.Errors.TemplatePreview.ExpressionEvaluationFailed),
      );
    }
    return {
      result: response.result,
      leaves: response.leaves,
    };
  }
}

function toExpressionPayload(expr: ConditionExpression): ConditionExpressionPayload {
  if (isComparisonExpression(expr)) {
    return {
      kind: 'compare',
      id: expr.id,
      left: { value: expr.left as unknown },
      operator: expr.operator,
      right: { value: expr.right as unknown },
    };
  }
  return {
    kind: expr.kind,
    id: expr.id,
    operands: expr.operands.map(toExpressionPayload),
  };
}

