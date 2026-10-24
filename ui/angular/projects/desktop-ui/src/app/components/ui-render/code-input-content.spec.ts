import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { EMPTY } from 'rxjs';

import { EditorView } from '@codemirror/view';

import { UiNode, UiResource } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { CodeEditorComponent } from '../forms/code-editor/code-editor.component';
import { RenderedTree, el, renderTree, tick, updateTree } from './ui-render-test-support';

async function until(condition: () => boolean, timeoutMs = 5000): Promise<void> {
  const started = Date.now();
  while (!condition()) {
    if (Date.now() - started > timeoutMs) throw new Error('condition not met in time');
    await new Promise(resolve => setTimeout(resolve, 10));
  }
}

function page(resource: UiResource): UiNode {
  return {
    id: 'root',
    type: 'stack',
    children: [
      {
        id: 'source',
        type: 'code',
        properties: {
          language: 'autohotkey',
          contentResource: resource,
          events: ['change', 'content-uploaded', 'content-upload-failed'],
        },
      },
      { id: 'save', type: 'button', properties: { label: 'Save', events: ['activate'] } },
    ],
  };
}

describe('shared-ui-node code input in content mode', () => {
  let resourceText: Record<string, string>;
  let getUiResourceText: jasmine.Spy;
  let log: string[];
  let uploadResult: { uploadId: string; length: number; contentHash: string } | null;

  beforeEach(() => {
    resourceText = { 'sha256:one': 'MsgBox "one"', 'sha256:other': 'MsgBox "other"' };
    getUiResourceText = jasmine.createSpy('getUiResourceText')
      .and.callFake(async (resource: UiResource) => resourceText[resource.contentHash ?? '']);
    log = [];
    uploadResult = { uploadId: 'u1', length: 12, contentHash: 'sha256:mine' };
  });

  afterEach(() => TestBed.resetTestingModule());

  async function render(resource: UiResource): Promise<RenderedTree> {
    const rendered = await renderTree(page(resource), null, [{
      provide: ApiService,
      useValue: {
        getUiResourceText,
        getLocalization: async () => ({ culture: 'en', fallbackCulture: 'en', translations: {}, availableCultures: ['en'] }),
        onNotification: () => EMPTY,
        connectionStateSignal: signal('connected'),
      },
    }]);
    rendered.fixture.componentRef.setInput('contentUploader', async (nodeId: string, text: string) => {
      log.push(`upload ${nodeId}: ${text}`);
      return uploadResult;
    });
    rendered.fixture.componentInstance.nodeEvent.subscribe(event => log.push(`${event.name} ${event.nodeId}`));
    await until(() => editor(rendered).contentState() === 'ready');
    await tick(rendered);
    return rendered;
  }

  function editor(rendered: RenderedTree): CodeEditorComponent {
    return rendered.fixture.debugElement.query(By.directive(CodeEditorComponent)).componentInstance as CodeEditorComponent;
  }

  function saveButton(rendered: RenderedTree): HTMLButtonElement {
    return [...el(rendered).querySelectorAll('button')].find(button => button.textContent?.trim() === 'Save')!;
  }

  it('loads the text from the resource and uploads edits after a pause instead of sending change events', async () => {
    const rendered = await render({ resourceId: 'script-1', contentHash: 'sha256:one' });
    expect(editor(rendered).value).toBe('MsgBox "one"');

    editor(rendered).onInput('MsgBox "two"');
    await new Promise(resolve => setTimeout(resolve, 300));
    expect(log).toEqual([]);

    await until(() => log.length > 0, 2000);
    expect(log).toEqual(['upload source: MsgBox "two"']);
    expect(rendered.events.some(event => event.name === 'change')).toBeFalse();
  });

  it('starts the pending upload before a button click is raised, without waiting for the pause', async () => {
    const rendered = await render({ resourceId: 'script-1', contentHash: 'sha256:one' });

    editor(rendered).onInput('MsgBox "typed"');
    saveButton(rendered).click();
    await tick(rendered);

    expect(log).toEqual(['upload source: MsgBox "typed"', 'activate save']);
  });

  it('shows the upload failure and keeps the typed text when the upload fails', async () => {
    uploadResult = null;
    const rendered = await render({ resourceId: 'script-1', contentHash: 'sha256:one' });

    editor(rendered).onInput('MsgBox "lost?"');
    editor(rendered).flushContent();
    await until(() => editor(rendered).uploadFailed());
    await tick(rendered);

    expect(el(rendered).querySelector('.ce-error[role="alert"]')).not.toBeNull();
    expect(editor(rendered).value).toBe('MsgBox "lost?"');
  });

  it('uploads an edited CRLF text with CRLF line endings', async () => {
    resourceText['sha256:crlf'] = 'MsgBox "a"\r\nMsgBox "b"';
    const rendered = await render({ resourceId: 'script-1', contentHash: 'sha256:crlf' });
    await until(() => editor(rendered).ready());
    const view = EditorView.findFromDOM(el(rendered).querySelector('.cm-content') as HTMLElement)!;

    view.dispatch({ changes: { from: view.state.doc.length, insert: '\nMsgBox "c"' } });
    editor(rendered).flushContent();
    await until(() => log.length > 0);

    expect(log).toEqual(['upload source: MsgBox "a"\r\nMsgBox "b"\r\nMsgBox "c"']);
  });

  it('shows a loading indicator in place of the editor until the text is there, again for another resource', async () => {
    const gates: Record<string, (text: string) => void> = {};
    getUiResourceText.and.callFake((resource: UiResource) =>
      new Promise<string>(resolve => { gates[resource.contentHash ?? ''] = resolve; }));
    const loading = () => document.querySelector('shared-code-editor shared-loading-state');
    const editorHidden = () => [...document.querySelectorAll('shared-code-editor .ce-textarea, shared-code-editor .ce-cm-host')]
      .every(element => getComputedStyle(element).display === 'none');

    const rendering = render({ resourceId: 'script-1', contentHash: 'sha256:one' });
    await until(() => loading() !== null && !!gates['sha256:one']);
    expect(editorHidden()).toBeTrue();
    gates['sha256:one']('MsgBox "one"');
    const rendered = await rendering;
    expect(loading()).toBeNull();
    expect(editorHidden()).toBeFalse();

    await updateTree(rendered, { root: page({ resourceId: 'script-2', contentHash: 'sha256:other' }) });
    await until(() => !!gates['sha256:other']);
    await tick(rendered);
    expect(loading()).not.toBeNull();
    expect(editorHidden()).toBeTrue();

    gates['sha256:other']('MsgBox "other"');
    await until(() => editor(rendered).contentState() === 'ready');
    await tick(rendered);
    expect(loading()).toBeNull();
    expect(editor(rendered).value).toBe('MsgBox "other"');
  });

  it('keeps the editor when the provider registers what this editor uploaded, and reloads other content', async () => {
    const rendered = await render({ resourceId: 'script-1', contentHash: 'sha256:one' });
    editor(rendered).onInput('MsgBox "mine"');
    editor(rendered).flushContent();
    await until(() => log.length > 0);
    await tick(rendered);

    await updateTree(rendered, { root: page({ resourceId: 'script-1', contentHash: 'sha256:mine' }) });
    await tick(rendered);
    expect(getUiResourceText).toHaveBeenCalledTimes(1);
    expect(editor(rendered).value).toBe('MsgBox "mine"');

    await updateTree(rendered, { root: page({ resourceId: 'script-1', contentHash: 'sha256:other' }) });
    await until(() => editor(rendered).value === 'MsgBox "other"');
    expect(getUiResourceText).toHaveBeenCalledTimes(2);
  });
});
