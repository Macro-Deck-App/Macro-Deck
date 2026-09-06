import { UiNode } from './ui-node.interface';
import { UiComponentBox, UiIntrinsicMetrics } from './layout';
import { UiComponentRange } from './ui-capabilities';
import { UiRenderHost } from '../render/ui-render-host';
import { TextFit } from '../render/text-fit';
import { UI_CORE_COMPONENTS as CORE_DEFINITIONS } from '../ui-components/ui-core-components';
import { MACRO_DECK_COMPONENTS as MACRO_DECK_DEFINITIONS } from '../macrodeck-components/macrodeck-components';

const DEFAULT_COMPONENT_VERSION_RANGE: UiComponentRange = { minimum: 1, maximum: 1 };

export interface UiComponentContext<TState = unknown> {
  readonly element: HTMLElement | SVGElement;

  readonly host: UiRenderHost;

  readonly basis: number;

  readonly crossExtent: number | null;

  readonly box: UiComponentBox;

  readonly isTreeRoot: boolean;

  readonly registry: UiComponentRegistry;

  current(): UiNode;

  setStyle(element: HTMLElement | SVGElement, name: string, value: string | null): void;

  setClass(element: Element, name: string, on: boolean): void;

  setClassName(element: Element, value: string): void;

  setAttribute(element: Element, name: string, value: string | null): void;

  sizeTo(element: HTMLElement | SVGElement, size: UiComponentBox): void;

  part(name: string, tag: string, namespace?: string, parent?: Element): HTMLElement | SVGElement;

  dropPart(name: string): void;

  syncChildren(
    parent: Element,
    entries: Array<{ child: UiNode; box: UiComponentBox; crossExtent: number | null }>,
  ): void;

  keepFit(element: Element, fit: TextFit, signature: string): void;

  emit(node: UiNode, name: string, payload?: unknown): void;

  pressTint(node: UiNode): void;

  repaint(): void;

  state: TState;
}

export interface UiComponentDefinition<TState = unknown> {
  readonly type: string;

  readonly version?: UiComponentRange;

  readonly events?: readonly string[];

  create(doc: Document): HTMLElement | SVGElement;

  bind?(ctx: UiComponentContext<TState>): void;

  paint(node: UiNode, ctx: UiComponentContext<TState>): void;

  release?(ctx: UiComponentContext<TState>): void;

  intrinsicMainPx?(node: UiNode, m: UiIntrinsicMetrics): number;

  tickPeriodMs?(node: UiNode): number | null;

  createState?(): TState;
}

export interface UiComponentRegistry {
  register(d: UiComponentDefinition): UiComponentDefinition | undefined;

  get(type: string): UiComponentDefinition | undefined;

  capabilities(): Readonly<Record<string, UiComponentRange>>;
}

export function createUiComponentRegistry(...d: readonly UiComponentDefinition[]): UiComponentRegistry {
  const byType = new Map<string, UiComponentDefinition>();
  for (const definition of d) byType.set(definition.type, definition);

  // Invalidated on every register() - cheap to keep, and capabilities() is read on every single repaint
  // to resolve a fallback chain, so recomputing the frozen record from scratch each time would cost a
  // tree walk it never actually needed.
  let cached: Readonly<Record<string, UiComponentRange>> | null = null;

  return {
    register(definition: UiComponentDefinition): UiComponentDefinition | undefined {
      const previous = byType.get(definition.type);
      byType.set(definition.type, definition);
      cached = null;
      return previous;
    },

    get(type: string): UiComponentDefinition | undefined {
      return byType.get(type);
    },

    capabilities(): Readonly<Record<string, UiComponentRange>> {
      if (cached === null) {
        const built: Record<string, UiComponentRange> = {};
        for (const [type, definition] of byType) built[type] = definition.version ?? DEFAULT_COMPONENT_VERSION_RANGE;
        cached = Object.freeze(built);
      }
      return cached;
    },
  };
}

export const UI_CORE_COMPONENTS: readonly UiComponentDefinition[] = CORE_DEFINITIONS;

export const MACRO_DECK_COMPONENTS: readonly UiComponentDefinition[] = MACRO_DECK_DEFINITIONS;

export const DEFAULT_UI_COMPONENT_REGISTRY: UiComponentRegistry =
  createUiComponentRegistry(...UI_CORE_COMPONENTS, ...MACRO_DECK_COMPONENTS);
