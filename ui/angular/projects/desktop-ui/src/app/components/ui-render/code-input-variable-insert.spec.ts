import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { UiNode, Variable } from '@macro-deck/runtime';
import { VariableService } from '@shared';
import { CodeEditorComponent } from '../forms/code-editor/code-editor.component';
import { VariablePickerComponent } from '../variable-picker/variable-picker.component';
import { renderTree, tick } from './ui-render-test-support';

function makeVariable(name: string): Variable {
  return { id: name, name, type: 'text', scope: 'global', classification: 'user', value: '', canWrite: true };
}

describe('shared-ui-node code input variable insertion', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('offers the variable picker for a code input with an insert format and inserts the formatted reference', async () => {
    const root: UiNode = {
      id: 'n',
      type: 'code',
      properties: {
        language: 'autohotkey',
        variableInsertFormat: 'MacroDeck.GetVar("{name}")',
        value: '',
        events: ['change'],
      },
    };
    const rendered = await renderTree(root, null, [
      { provide: VariableService, useValue: { variables: signal([makeVariable('obs_scene')]) } },
    ]);

    const editor = rendered.fixture.debugElement.query(By.directive(CodeEditorComponent))
      .componentInstance as CodeEditorComponent;
    expect(editor.language).toBe('autohotkey');

    const picker = rendered.fixture.debugElement.query(By.directive(VariablePickerComponent))
      .componentInstance as VariablePickerComponent;
    expect(picker.variables.map(variable => variable.name)).toEqual(['obs_scene']);

    picker.pick.emit('obs_scene');
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'MacroDeck.GetVar("obs_scene")' }]);
  });

  it('shows no variable picker for a code input without an insert format', async () => {
    const root: UiNode = { id: 'n', type: 'code', properties: { language: 'json', value: '{}' } };
    const rendered = await renderTree(root);

    expect(rendered.fixture.debugElement.query(By.directive(CodeEditorComponent))).not.toBeNull();
    expect(rendered.fixture.debugElement.query(By.directive(VariablePickerComponent))).toBeNull();
  });
});
