import { ActionBlockParameter, ActionParamControlType, ActionParameterDef, ActionParameterType, LocalizationTranslator, ParameterValue, resolveLocalizedText } from '@macro-deck/runtime';

export function mapActionParameterDef(
  p: ActionParameterDef,
  localization: LocalizationTranslator,
): Omit<ActionBlockParameter, 'value'> {
  const label = resolveLocalizedText(p.label, localization);
  const description = resolveLocalizedText(p.description, localization);
  return {
    name: p.name,
    type: mapActionParamType(p.type),
    label: label || p.name,
    description: description || undefined,
    placeholder: resolveLocalizedText(p.placeholder, localization) || undefined,
    autoPrefixHttps: p.autoPrefixHttps,
    defaultValue: p.defaultValue as ParameterValue | undefined,
    required: p.required,
    multiline: p.multiline,
    supportsReset: p.supportsReset,
    literalOnly: p.literalOnly,
    validationRegex: p.validationRegex,
    maxLength: p.maxLength,
    min: p.min,
    max: p.max,
    step: p.step,
    showSlider: p.showSlider,
    options: p.options?.map(o => ({
      label: resolveLocalizedText(o.label, localization) || o.value,
      value: o.value,
    })),
    dynamicOptions: p.dynamicOptions,
    optionsSourceId: p.optionsSourceId,
    allowSelf: p.allowSelf,
    widgetTypes: p.widgetTypes,
    fileExtensions: p.fileExtensions,
    language: p.language,
    children: p.children?.map(c => mapActionParameterDef(c, localization)),
    itemTemplate: p.itemTemplate ? mapActionParameterDef(p.itemTemplate, localization) : undefined,
    visibleWhen: p.visibleWhen
      ? { parameterName: p.visibleWhen.parameterName, values: [...p.visibleWhen.values] }
      : undefined,
  };
}

export function mapActionParamType(type: ActionParameterType | string): ActionParamControlType {
  switch (type) {
    case ActionParameterType.Number: return 'number';
    case ActionParameterType.Boolean: return 'boolean';
    case ActionParameterType.Choice: return 'choice';
    case ActionParameterType.Password: return 'password';
    case ActionParameterType.Secret: return 'secret';
    case ActionParameterType.DynamicChoice: return 'dynamic-choice';
    case ActionParameterType.Autocomplete: return 'autocomplete';
    case ActionParameterType.MultiSelect: return 'multiselect';
    case ActionParameterType.Color: return 'color';
    case ActionParameterType.File: return 'file';
    case ActionParameterType.Folder: return 'folder';
    case ActionParameterType.Hotkey: return 'hotkey';
    case ActionParameterType.Duration: return 'duration';
    case ActionParameterType.DateTime: return 'datetime';
    case ActionParameterType.Json: return 'json';
    case ActionParameterType.Code: return 'code';
    case ActionParameterType.KeyValue: return 'keyvalue';
    case ActionParameterType.Object: return 'object';
    case ActionParameterType.Array: return 'array';
    case ActionParameterType.IpAddress: return 'ipaddress';
    case ActionParameterType.Url: return 'url';
    case ActionParameterType.Icon: return 'icon';
    case ActionParameterType.Image: return 'image';
    case ActionParameterType.KeyboardSequence: return 'keyboard-sequence';
    case ActionParameterType.KeyboardCombo: return 'keyboard-combo';
    case ActionParameterType.WidgetTarget: return 'widget-target';
    case 'Select': return 'choice';
    default: return 'string';
  }
}
