import type { Text } from '@codemirror/state';
import type { EditorView, Tooltip } from '@codemirror/view';
import type { Diagnostic } from '@codemirror/lint';
import type { CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import type { syntaxTree as syntaxTreeFn } from '@codemirror/language';
import type { SyntaxNode, Tree } from '@lezer/common';
import type { compileSchema, draft07 } from 'json-schema-library';

type SchemaNode = ReturnType<typeof compileSchema>;

export interface JsonEditorDeps {
  syntaxTree: typeof syntaxTreeFn;
  compileSchema: typeof compileSchema;
  draft07: typeof draft07;
}

const VALUE_NODE_NAMES = new Set(['Object', 'Array', 'String', 'Number', 'True', 'False', 'Null']);

function unescapePointerSegment(segment: string): string {
  return segment.replace(/~1/g, '/').replace(/~0/g, '~');
}

function escapePointerSegment(segment: string): string {
  return segment.replace(/~/g, '~0').replace(/\//g, '~1');
}

function parseJsonLiteral(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return undefined;
  }
}

function stepInto(node: SyntaxNode, key: string, doc: Text): SyntaxNode | null {
  if (node.name === 'Object') {
    for (let child = node.firstChild; child; child = child.nextSibling) {
      if (child.name !== 'Property') continue;
      const nameNode = child.getChild('PropertyName');
      if (!nameNode || parseJsonLiteral(doc.sliceString(nameNode.from, nameNode.to)) !== key) continue;
      const valueNode = child.lastChild;
      return valueNode && VALUE_NODE_NAMES.has(valueNode.name) ? valueNode : null;
    }
    return null;
  }

  if (node.name === 'Array') {
    const index = Number(key);
    if (!Number.isInteger(index) || index < 0) return null;
    let i = 0;
    for (let child = node.firstChild; child; child = child.nextSibling) {
      if (!VALUE_NODE_NAMES.has(child.name)) continue;
      if (i === index) return child;
      i++;
    }
    return null;
  }

  return null;
}

export function resolveJsonPointerRange(tree: Tree, doc: Text, pointer: string): { from: number; to: number } | null {
  const root = tree.topNode.firstChild;
  if (!root) return null;

  const segments = pointer.replace(/^#/, '').split('/').filter(s => s.length > 0).map(unescapePointerSegment);

  let node = root;
  for (const segment of segments) {
    const next = stepInto(node, segment, doc);
    if (!next) return null;
    node = next;
  }
  return { from: node.from, to: node.to };
}

function ancestorPath(node: SyntaxNode, doc: Text): string[] | null {
  const segments: string[] = [];
  let child: SyntaxNode = node;
  let parent = node.parent;
  while (parent) {
    if (parent.name === 'Property') {
      const nameNode = parent.getChild('PropertyName');
      if (!nameNode) return null;
      if (child !== nameNode) {
        const key = parseJsonLiteral(doc.sliceString(nameNode.from, nameNode.to));
        if (typeof key !== 'string') return null;
        segments.unshift(key);
      }
    } else if (parent.name === 'Array') {
      let index = 0;
      for (let sib = parent.firstChild; sib && sib !== child; sib = sib.nextSibling) {
        if (VALUE_NODE_NAMES.has(sib.name)) index++;
      }
      segments.unshift(String(index));
    }
    child = parent;
    parent = parent.parent;
  }
  return segments;
}

function toDataPointer(path: string[]): string {
  return path.length ? '#/' + path.map(escapePointerSegment).join('/') : '';
}

const draftCache = new WeakMap<object, SchemaNode>();

function draftFor(deps: JsonEditorDeps, schema: object): SchemaNode {
  let draft = draftCache.get(schema);
  if (!draft) {
    draft = deps.compileSchema(schema, { drafts: [deps.draft07] });
    draftCache.set(schema, draft);
  }
  return draft;
}

function schemaAt(deps: JsonEditorDeps, schema: object, path: string[], data: unknown): unknown {
  return draftFor(deps, schema).getNode(toDataPointer(path), data).node?.schema;
}

function safeParseDoc(doc: Text): unknown {
  try {
    return JSON.parse(doc.toString());
  } catch {
    return {};
  }
}

function isSchemaObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object';
}

export interface LintObservers {
  onResult(valid: boolean, message: string | null): void;
}

export function buildCombinedLintSource(
  deps: JsonEditorDeps,
  jsonParseLinter: () => (view: EditorView) => Diagnostic[],
  getSchema: () => object | null,
  observers: LintObservers,
): (view: EditorView) => Diagnostic[] {
  const parseLint = jsonParseLinter();

  return (view) => {
    const syntaxDiagnostics = parseLint(view);
    if (syntaxDiagnostics.length > 0) {
      const diagnostics = [improveSyntaxDiagnosticPosition(deps, view, syntaxDiagnostics[0])];
      observers.onResult(false, diagnostics[0].message);
      return diagnostics;
    }

    const schema = getSchema();
    if (!schema) {
      observers.onResult(true, null);
      return [];
    }

    const data = JSON.parse(view.state.doc.toString());
    const { errors } = draftFor(deps, schema).validate(data);
    if (errors.length === 0) {
      observers.onResult(true, null);
      return [];
    }

    const tree = deps.syntaxTree(view.state);
    const diagnostics: Diagnostic[] = errors.map(error => {
      const range = resolveJsonPointerRange(tree, view.state.doc, error.data.pointer)
        ?? { from: 0, to: view.state.doc.length };
      return { from: range.from, to: Math.max(range.to, range.from), severity: 'error', message: error.message };
    });

    observers.onResult(false, diagnostics[0].message);
    return diagnostics;
  };
}

function improveSyntaxDiagnosticPosition(deps: JsonEditorDeps, view: EditorView, diagnostic: Diagnostic): Diagnostic {
  const tree = deps.syntaxTree(view.state);
  let errorNode: { from: number; to: number } | null = null;
  tree.iterate({
    enter: (node) => {
      if (errorNode) return false;
      if (node.type.isError) {
        errorNode = { from: node.from, to: node.to };
        return false;
      }
      return true;
    },
  });

  if (!errorNode) return diagnostic;
  // TypeScript cannot see that the closure above is what assigns `errorNode`, and narrows it to
  // `never` here without this - a known limitation, not a real possibility of `errorNode` being null.
  const found = errorNode as { from: number; to: number };
  const from = found.from;
  const to = Math.min(Math.max(found.to, from + 1), view.state.doc.length);
  return { ...diagnostic, from, to: Math.max(to, from) };
}

function matchPropertyNamePosition(tree: Tree, pos: number): { from: number; to: number; containerNode: SyntaxNode } | null {
  const node = tree.resolveInner(pos, -1);
  if (node.name === 'PropertyName') {
    const property = node.parent;
    const container = property?.parent;
    if (!container || container.name !== 'Object') return null;
    return { from: node.from + 1, to: node.to - 1, containerNode: container };
  }
  if (node.name === 'Object') {
    return { from: pos, to: pos, containerNode: node };
  }
  return null;
}

function matchValuePosition(tree: Tree, pos: number): { from: number; to: number; valueNode: SyntaxNode } | null {
  const node = tree.resolveInner(pos, -1);
  if (node.name === 'String' && node.parent?.name === 'Property') {
    return { from: node.from + 1, to: node.to - 1, valueNode: node };
  }
  return null;
}

function existingKeys(objectNode: SyntaxNode, doc: Text): Set<string> {
  const keys = new Set<string>();
  for (let child = objectNode.firstChild; child; child = child.nextSibling) {
    if (child.name !== 'Property') continue;
    const nameNode = child.getChild('PropertyName');
    if (!nameNode) continue;
    const key = parseJsonLiteral(doc.sliceString(nameNode.from, nameNode.to));
    if (typeof key === 'string') keys.add(key);
  }
  return keys;
}

export function buildSchemaCompletionSource(
  deps: JsonEditorDeps,
  getSchema: () => object | null,
): (context: CompletionContext) => CompletionResult | null {
  return (context) => {
    const schema = getSchema();
    if (!schema) return null;

    const tree = deps.syntaxTree(context.state);

    const propertyMatch = matchPropertyNamePosition(tree, context.pos);
    if (propertyMatch) {
      const path = ancestorPath(propertyMatch.containerNode, context.state.doc);
      if (!path) return null;
      const containerSchema = schemaAt(deps, schema, path, safeParseDoc(context.state.doc));
      if (!isSchemaObject(containerSchema)) return null;
      const properties = containerSchema['properties'] as Record<string, Record<string, unknown>> | undefined;
      if (!properties) return null;

      const taken = existingKeys(propertyMatch.containerNode, context.state.doc);
      const options = Object.keys(properties)
        .filter(key => !taken.has(key))
        .map(key => {
          const description = properties[key]?.['description'];
          const info = typeof description === 'string' ? description : undefined;
          return { label: key, detail: info, info };
        });
      if (options.length === 0) return null;
      return { from: propertyMatch.from, to: propertyMatch.to, options, validFor: /^[^"]*$/ };
    }

    const valueMatch = matchValuePosition(tree, context.pos);
    if (valueMatch) {
      const path = ancestorPath(valueMatch.valueNode, context.state.doc);
      if (!path) return null;
      const valueSchema = schemaAt(deps, schema, path, safeParseDoc(context.state.doc));
      if (!isSchemaObject(valueSchema)) return null;
      const enumValues = valueSchema['enum'];
      if (!Array.isArray(enumValues)) return null;

      const options = enumValues
        .filter((value): value is string => typeof value === 'string')
        .map(value => ({ label: value }));
      if (options.length === 0) return null;
      return { from: valueMatch.from, to: valueMatch.to, options, validFor: /^[^"]*$/ };
    }

    return null;
  };
}

function findEnclosingProperty(node: SyntaxNode): SyntaxNode | null {
  let current: SyntaxNode | null = node;
  while (current && current.name !== 'Property') current = current.parent;
  return current;
}

export function buildSchemaHoverSource(
  deps: JsonEditorDeps,
  getSchema: () => object | null,
): (view: EditorView, pos: number) => Tooltip | null {
  return (view, pos) => {
    const schema = getSchema();
    if (!schema) return null;

    const tree = deps.syntaxTree(view.state);
    const property = findEnclosingProperty(tree.resolveInner(pos, -1));
    const nameNode = property?.getChild('PropertyName');
    const valueNode = property?.lastChild;
    if (!property || !nameNode || !valueNode) return null;

    const path = ancestorPath(valueNode, view.state.doc);
    if (!path) return null;

    const resolved = schemaAt(deps, schema, path, safeParseDoc(view.state.doc));
    const description = isSchemaObject(resolved) && typeof resolved['description'] === 'string'
      ? resolved['description'] as string
      : undefined;
    if (!description) return null;

    return {
      pos: nameNode.from,
      end: nameNode.to,
      above: true,
      create: () => {
        const dom = document.createElement('div');
        dom.className = 'cm-json-schema-hover';
        dom.textContent = description;
        return { dom };
      },
    };
  };
}
