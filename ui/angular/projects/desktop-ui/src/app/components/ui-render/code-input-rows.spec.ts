import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { UiNode } from '@macro-deck/runtime';
import { CodeEditorComponent } from '../forms/code-editor/code-editor.component';
import { renderTree } from './ui-render-test-support';

async function editorFor(properties: Record<string, unknown>): Promise<{ editor: CodeEditorComponent; host: HTMLElement }> {
  const root: UiNode = { id: 'n', type: 'code', properties: { language: 'autohotkey', value: '', ...properties } };
  const rendered = await renderTree(root);
  const debug = rendered.fixture.debugElement.query(By.directive(CodeEditorComponent));
  return { editor: debug.componentInstance as CodeEditorComponent, host: debug.nativeElement as HTMLElement };
}

describe('shared-ui-node code input rows', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the number of lines the node asks for, in the editor and its fallback text area', async () => {
    const { editor, host } = await editorFor({ rows: 22 });

    expect(editor.rows).toBe(22);
    expect((host.querySelector('.ce-cm-host') as HTMLElement).style.getPropertyValue('--ce-rows')).toBe('22');
    expect((host.querySelector('textarea') as HTMLTextAreaElement).rows).toBe(22);
  });

  it('keeps the default height when the node names no rows', async () => {
    const { editor } = await editorFor({});

    expect(editor.rows).toBe(CodeEditorComponent.defaultRows);
  });

  it('clamps rows to a range it can draw', async () => {
    expect((await editorFor({ rows: 1 })).editor.rows).toBe(3);
    TestBed.resetTestingModule();
    expect((await editorFor({ rows: 500 })).editor.rows).toBe(60);
  });

  it('applies rows to an editor whose text is a content resource', async () => {
    const { editor } = await editorFor({
      rows: 12,
      contentResource: { resourceId: 'p.script', contentHash: 'sha256:00', mediaType: 'text/plain' },
    });

    expect(editor.rows).toBe(12);
  });
});
