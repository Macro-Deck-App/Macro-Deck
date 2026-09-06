export interface UiNode {
  id: string;
  type: string;
  requiredComponentVersion?: number;
  properties?: Record<string, unknown>;
  children?: UiNode[];
  fallback?: UiNode;
}

export interface UiNodeEvent {
  nodeId: string;
  name: string;
  data?: unknown;
}
