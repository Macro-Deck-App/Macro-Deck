import type { EditorView } from '@codemirror/view';

import {
  JsonEditorDeps,
  buildCombinedLintSource,
  buildSchemaCompletionSource,
} from './widget-json-editor.schema-support';

describe('WidgetJsonEditorComponent JSON schema glue', () => {
  async function buildDeps(): Promise<JsonEditorDeps> {
    const [{ syntaxTree }, { Draft07 }] = await Promise.all([
      import('@codemirror/language'),
      import('json-schema-library'),
    ]);
    return { syntaxTree, Draft07 };
  }

  describe('linting', () => {
    it('reports a syntax error at its actual line, not a hard-coded 0:0', async () => {
      const [{ EditorState }, { json, jsonParseLinter }, deps] = await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/lang-json'),
        buildDeps(),
      ]);

      const doc = '{\n  "a": 1,\n  "b": bogus\n}'; // 4 lines; the bad token is on line 3
      const state = EditorState.create({ doc, extensions: [json()] });

      let reported: { valid: boolean; message: string | null } | null = null;
      const source = buildCombinedLintSource(deps, jsonParseLinter, () => null, {
        onResult: (valid, message) => { reported = { valid, message }; },
      });

      const diagnostics = source({ state } as unknown as EditorView);

      expect(diagnostics.length).toBe(1);
      expect(state.doc.lineAt(diagnostics[0].from).number)
        .withContext('diagnostic line').toBe(3);
      expect(reported).toEqual(jasmine.objectContaining({ valid: false }));
    });

    it('validates against the schema, not just JSON.parse', async () => {
      const [{ EditorState }, { json, jsonParseLinter }, deps] = await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/lang-json'),
        buildDeps(),
      ]);

      const schema = {
        type: 'object',
        properties: {
          mode: { type: 'string', enum: ['a', 'b'] },
          count: { type: 'number' },
        },
      };

      function isValid(text: string): boolean {
        let valid = false;
        const state = EditorState.create({ doc: text, extensions: [json()] });
        const source = buildCombinedLintSource(deps, jsonParseLinter, () => schema, {
          onResult: v => { valid = v; },
        });
        source({ state } as unknown as EditorView);
        return valid;
      }

      expect(isValid('{"mode":"bogus","count":1}')).withContext('bad enum value').toBeFalse();
      expect(isValid('{"mode":"a","count":"nope"}')).withContext('wrong type').toBeFalse();
      expect(isValid('{"mode":"a","count":1}')).withContext('conforming document').toBeTrue();
    });
  });

  describe('completion', () => {
    it('offers property names and enum values from a schema the test invents, never a hard-coded list', async () => {
      const [{ EditorState }, { json }, { CompletionContext }, deps] = await Promise.all([
        import('@codemirror/state'),
        import('@codemirror/lang-json'),
        import('@codemirror/autocomplete'),
        buildDeps(),
      ]);

      const schema = {
        type: 'object',
        properties: {
          zorbLevel: { type: 'string', enum: ['low', 'medium', 'high'], description: 'Made-up level' },
          quuxColor: { type: 'string', description: 'Made-up color' },
        },
      };
      const source = buildSchemaCompletionSource(deps, () => schema);

      const propertyDoc = '{ "" }';
      const propertyState = EditorState.create({ doc: propertyDoc, extensions: [json()] });
      const propertyPos = propertyDoc.indexOf('""') + 1;
      const propertyResult = source(new CompletionContext(propertyState, propertyPos, true));
      const propertyLabels = (propertyResult?.options ?? []).map(o => o.label);

      expect(propertyLabels).toContain('zorbLevel');
      expect(propertyLabels).toContain('quuxColor');
      expect(propertyResult?.options.find(o => o.label === 'zorbLevel')?.info).toBe('Made-up level');

      const valueDoc = '{"zorbLevel": ""}';
      const valueState = EditorState.create({ doc: valueDoc, extensions: [json()] });
      const valuePos = valueDoc.lastIndexOf('""') + 1;
      const valueResult = source(new CompletionContext(valueState, valuePos, true));
      const valueLabels = (valueResult?.options ?? []).map(o => o.label);

      expect(valueLabels).toEqual(['low', 'medium', 'high']);
    });
  });
});
