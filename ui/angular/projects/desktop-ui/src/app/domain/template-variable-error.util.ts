import { AppStrings } from '@macro-deck/runtime';
import type { VariableTemplateError } from '@macro-deck/runtime';
import type { LocalizationService } from '@shared';

export function templateVariableErrorMessage(localization: LocalizationService, error: VariableTemplateError): string {
  const detail = error.detail ?? '';
  switch (error.code) {
    case 'RenderFailed':
      return localization.translateKey(AppStrings.Variables.Manager.TemplateErrors.RenderFailed, { details: detail });
    case 'NotNumeric':
      return localization.translateKey(AppStrings.Variables.Manager.TemplateErrors.NotNumeric, { value: detail });
    case 'NotBoolean':
      return localization.translateKey(AppStrings.Variables.Manager.TemplateErrors.NotBoolean, { value: detail });
    case 'CircularReference':
      return localization.translateKey(AppStrings.Variables.Manager.TemplateErrors.CircularReference);
    default:
      return localization.translateKey(AppStrings.Variables.Manager.TemplateErrors.Unknown);
  }
}
