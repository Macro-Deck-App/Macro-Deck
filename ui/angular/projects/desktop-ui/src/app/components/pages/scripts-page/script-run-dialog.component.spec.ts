import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ParameterValue, ScriptInput, ScriptInputValue } from '@macro-deck/runtime';

import { ScriptRunDialogComponent } from './script-run-dialog.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

interface DialogAccess {
  setValue(name: string, value: ParameterValue): void;
  submit(): void;
  canRun(): boolean;
}

describe('ScriptRunDialogComponent', () => {
  let fixture: ComponentFixture<ScriptRunDialogComponent>;
  let dialog: DialogAccess;
  let supplied: Record<string, ScriptInputValue> | null;

  function open(inputs: ScriptInput[]): void {
    fixture.componentRef.setInput('scriptName', 'Go Live');
    fixture.componentRef.setInput('inputs', inputs);
    fixture.detectChanges();
  }

  beforeEach(() => {
    supplied = null;

    TestBed.configureTestingModule({
      imports: [ScriptRunDialogComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).overrideComponent(ScriptRunDialogComponent, { set: { template: '', imports: [] } });

    fixture = TestBed.createComponent(ScriptRunDialogComponent);
    fixture.componentInstance.run.subscribe(values => (supplied = values));
    dialog = fixture.componentInstance as unknown as DialogAccess;
  });

  it('supplies nothing for the fields the user left alone, so the declared defaults stand', () => {
    open([
      { name: 'scene', type: 'text', defaultValue: 'Starting Soon' },
      { name: 'note', type: 'text' },
      { name: 'volume', type: 'numeric' },
      { name: 'muted', type: 'boolean', defaultValue: 'true' },
    ]);

    dialog.submit();

    expect(supplied).toEqual({});
  });

  it('supplies an emptied text field, which a field that was never touched must not look like', () => {
    open([
      { name: 'scene', type: 'text', defaultValue: 'Starting Soon' },
      { name: 'note', type: 'text', defaultValue: 'unchanged' },
    ]);

    dialog.setValue('scene', '');
    dialog.submit();

    expect(supplied).toEqual({ scene: '' });
  });

  it('supplies a filled field as the type its declaration asks for', () => {
    open([
      { name: 'scene', type: 'text' },
      { name: 'volume', type: 'numeric' },
      { name: 'muted', type: 'boolean' },
    ]);

    dialog.setValue('scene', 'Live');
    dialog.setValue('volume', '42');
    dialog.setValue('muted', true);
    dialog.submit();

    expect(supplied).toEqual({ scene: 'Live', volume: 42, muted: true });
  });

  it('supplies a required input that has no default even when its control was never touched', () => {
    open([{ name: 'muted', type: 'boolean', required: true }]);

    dialog.submit();

    expect(supplied).toEqual({ muted: false });
  });

  it('will not run while a required input without a default is unanswered', () => {
    open([{ name: 'scene', type: 'text', required: true }]);

    expect(dialog.canRun()).toBeFalse();

    dialog.submit();

    expect(supplied).toBeNull();
  });
});
