import { asLocalizedRef, LocalizationTranslator } from '../localization/localized-text';
import { asHexColor, asUiLength, resolveLength } from '../ui-framework/length';
import { nodeClaimsGesture, nodeDeclaresGesture } from '../ui-framework/node-gestures';
import { nodeRecord } from '../ui-framework/node-properties.util';
import { UiNode } from '../ui-framework/ui-node.interface';
import {
  UI_COMPONENT_BORDER_LINES_WELL_KNOWN,
  UI_MODIFIER_DIM_OPACITY,
  UiComponentBorderLines,
  UiComponentModifiers,
} from '../ui-components/component-modifiers';
import { UiComponentProperties } from '../ui-components/component-properties';
import { UiComponents } from '../ui-components/ui-component-types';

export interface UiModifierInputs {
  opacity?: number;
  radius?: string;
}

export interface UiModifierBorder {
  width: string;
  line: string;
  color: string;
}

export interface UiModifierPlan {
  styles: Record<string, string>;
  attributes: Record<string, string>;
  border?: UiModifierBorder;
}

export const MODIFIER_BORDER_PART = 'modifier-border';

export interface UiModifierScope {
  readonly basis: number;
  readonly crossExtent: number | null;
  readonly isTreeRoot: boolean;
  isDisabled(): boolean;
}

const NATIVE_ROLE_TAGS = ['input', 'textarea', 'select', 'img', 'svg'];

const OVERLAY_BORDER_TYPES: readonly string[] = [
  UiComponents.Stack, UiComponents.Button, UiComponents.Layer, UiComponents.Transform, UiComponents.Modifier,
];

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function finite(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function trimmed(value: number): string {
  return String(Number(value.toFixed(4)));
}

function percent(fraction: number): string {
  return `${trimmed(fraction * 100)}%`;
}

export function gradientCss(raw: unknown, stopColor: (stop: Record<string, unknown>) => string | undefined): string | undefined {
  if (!isPlainObject(raw)) return undefined;

  const stopsOf = (shape: Record<string, unknown>): string | undefined => {
    const stops = shape['stops'];
    if (!Array.isArray(stops) || stops.length < 2) return undefined;
    const parts: string[] = [];
    for (const stop of stops) {
      if (!isPlainObject(stop)) return undefined;
      const offset = finite(stop['offset']);
      const color = stopColor(stop);
      if (offset === undefined || color === undefined) return undefined;
      parts.push(`${color} ${percent(offset)}`);
    }
    return parts.join(', ');
  };

  const linear = raw['linear'];
  if (isPlainObject(linear)) {
    const angle = finite(linear['angle']);
    const stops = stopsOf(linear);
    return angle === undefined || stops === undefined ? undefined : `linear-gradient(${trimmed(angle)}deg, ${stops})`;
  }

  const radial = raw['radial'];
  if (isPlainObject(radial)) {
    const centerX = finite(radial['centerX']);
    const centerY = finite(radial['centerY']);
    const stops = stopsOf(radial);
    if (centerX === undefined || centerY === undefined || stops === undefined) return undefined;
    return `radial-gradient(at ${percent(centerX)} ${percent(centerY)}, ${stops})`;
  }

  return undefined;
}

function modifiersOf(node: UiNode): Record<string, unknown> | undefined {
  return nodeRecord(node, UiComponentProperties.Modifiers);
}

function backgroundOf(modifiers: Record<string, unknown>): string | undefined {
  const raw = modifiers[UiComponentModifiers.Background];
  return asHexColor(raw) ?? gradientCss(raw, stop => asHexColor(stop['color']));
}

function radiusOf(modifiers: Record<string, unknown>, scope: UiModifierScope): number | undefined {
  if (scope.isTreeRoot) return undefined;
  return resolveLength(asUiLength(modifiers[UiComponentModifiers.Radius]), scope.basis, scope.crossExtent);
}

function textOf(raw: unknown, localization: LocalizationTranslator): string | undefined {
  if (typeof raw === 'string') return raw;
  const ref = asLocalizedRef(raw);
  return ref ? localization.translate(ref.scope, ref.key, ref.arguments) : undefined;
}

export function nodeModifierOwns(node: UiNode, scope: UiModifierScope, property: 'background' | 'border-radius'): boolean {
  const modifiers = modifiersOf(node);
  if (modifiers === undefined) return false;
  return property === 'background'
    ? backgroundOf(modifiers) !== undefined
    : radiusOf(modifiers, scope) !== undefined;
}

export function planNodeModifiers(
  node: UiNode,
  scope: UiModifierScope,
  element: Element,
  localization: LocalizationTranslator,
  inputs: UiModifierInputs | undefined,
  parentDisabled: boolean,
): UiModifierPlan {
  const plan: UiModifierPlan = { styles: {}, attributes: {} };
  const modifiers = modifiersOf(node);
  const dimmed = modifiers?.[UiComponentModifiers.Disabled] === true && !parentDisabled;

  if (inputs?.opacity !== undefined || dimmed) {
    plan.styles['opacity'] = trimmed((inputs?.opacity ?? 1) * (dimmed ? UI_MODIFIER_DIM_OPACITY : 1));
  }

  const radius = modifiers === undefined ? undefined : radiusOf(modifiers, scope);
  if (radius !== undefined) plan.styles['border-radius'] = `${trimmed(radius)}px`;
  else if (inputs?.radius !== undefined && !scope.isTreeRoot) plan.styles['border-radius'] = inputs.radius;

  if (nodeDeclaresGesture(node)) plan.styles['touch-action'] = 'none';

  if (modifiers === undefined) return plan;

  const background = backgroundOf(modifiers);
  if (background !== undefined) plan.styles['background'] = background;

  const borderWidth = resolveLength(
    asUiLength(modifiers[UiComponentModifiers.BorderWidth]), scope.basis, scope.crossExtent);
  if (borderWidth !== undefined && borderWidth > 0) {
    const requested = modifiers[UiComponentModifiers.BorderLine];
    const line = typeof requested === 'string' && UI_COMPONENT_BORDER_LINES_WELL_KNOWN.includes(requested)
      ? requested
      : UiComponentBorderLines.Solid;
    const color = asHexColor(modifiers[UiComponentModifiers.BorderColor]) ?? 'currentColor';
    if (OVERLAY_BORDER_TYPES.includes(node.type)) {
      plan.border = { width: `${trimmed(borderWidth)}px`, line, color };
    } else {
      plan.styles['outline'] = `${trimmed(borderWidth)}px ${line} ${color}`;
      plan.styles['outline-offset'] = `${trimmed(-borderWidth)}px`;
    }
  }

  const label = textOf(modifiers[UiComponentModifiers.AccessibilityLabel], localization);
  const hint = textOf(modifiers[UiComponentModifiers.AccessibilityHint], localization);
  if (label !== undefined) plan.attributes['aria-label'] = label;
  if (hint !== undefined) plan.attributes['aria-description'] = hint;
  if ((label !== undefined || hint !== undefined) && !NATIVE_ROLE_TAGS.includes(element.tagName.toLowerCase())) {
    plan.attributes['role'] = nodeClaimsGesture(node) ? 'button' : 'group';
  }

  if (scope.isDisabled()) plan.attributes['aria-disabled'] = 'true';

  return plan;
}
