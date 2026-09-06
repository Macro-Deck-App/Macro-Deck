import type { LocalizedText } from '../localization/localized-text';

export type VariableScope = 'global' | 'widget';

export type VariableType = 'text' | 'numeric' | 'boolean';

export type VariableClassification = 'user' | 'integration' | 'widget';

export interface Variable {
  id: string;
  name: string;
  scope: VariableScope;
  scopeRefId?: string;
  type: VariableType;
  classification: VariableClassification;
  ownerIntegrationId?: string;
  value: string;
  decimalPlaces?: number;
  available?: boolean;
  origin?: 'event' | 'input';
  displayName?: LocalizedText;
  configurationKey?: string | null;
  configurationName?: LocalizedText;
  dynamicResourceId?: string;
  unit?: string;
  semanticKind?: string;
  min?: number;
  max?: number;
  step?: number;
  attributes?: Record<string, string>;
  canWrite?: boolean;
  commitOnRelease?: boolean;
}

export type { VariableReference } from './action-builder.interface';
export { isVariableReference } from './action-builder.interface';

