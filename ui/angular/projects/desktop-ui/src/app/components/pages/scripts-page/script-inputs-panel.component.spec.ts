import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ScriptInput } from '@macro-deck/runtime';
import { ApiService } from '@shared';

import { ScriptInputsPanelComponent } from './script-inputs-panel.component';

interface InputForm {
  name: string;
  type: ScriptInput['type'];
  label: string;
  defaultValue: string;
  required: boolean;
}

interface PanelAccess {
  openCreate(): void;
  openEdit(index: number): void;
  closeCreate(): void;
  submitCreate(): void;
  form(): InputForm;
  editIndex(): number | null;
  setFormName(name: string): void;
  setFormType(type: ScriptInput['type']): void;
  setFormLabel(label: string): void;
  setFormDefault(value: string | number | null): void;
  setFormRequired(required: boolean): void;
  canCreate(): boolean;
  createNameTaken(): boolean;
  createShadowWarning(): string | null;
  requestDelete(index: number): void;
  confirmDelete(): void;
  cancelDelete(): void;
  deleteCandidate(): ScriptInput | null;
  nameError(index: number): string | null;
  shadowWarning(index: number): string | null;
}

function apiStub(): jasmine.SpyObj<ApiService> {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getVariables']);
  api.onNotification.and.callFake(() => new Subject<unknown>().asObservable() as Observable<never>);
  api.getVariables.and.resolveTo({ variables: [] });
  Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
  return api;
}

describe('ScriptInputsPanelComponent', () => {
  let fixture: ComponentFixture<ScriptInputsPanelComponent>;
  let panel: PanelAccess;
  let emitted: ScriptInput[];
  let valid: boolean | null;

  function seed(inputs: ScriptInput[], globalNames: string[] = []): void {
    fixture.componentRef.setInput('inputs', inputs);
    fixture.componentRef.setInput('globalNames', globalNames);
    fixture.detectChanges();
  }

  function settle(): void {
    fixture.componentRef.setInput('inputs', emitted);
    fixture.detectChanges();
  }

  beforeEach(() => {
    emitted = [];
    valid = null;

    // The real VariableService, not a stand-in for its name rules: a declaration is read as
    // `vars.<name>`, so the panel and the variable editor have to settle a name the same way.
    TestBed.configureTestingModule({
      imports: [ScriptInputsPanelComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiStub() },
      ],
    }).overrideComponent(ScriptInputsPanelComponent, { set: { template: '', imports: [] } });

    fixture = TestBed.createComponent(ScriptInputsPanelComponent);
    fixture.componentInstance.inputsChange.subscribe(inputs => (emitted = inputs));
    fixture.componentInstance.validityChange.subscribe(value => (valid = value));
    panel = fixture.componentInstance as unknown as PanelAccess;
  });

  it('declares the input the dialog was filled in with', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('scene');
    panel.submitCreate();

    expect(emitted).toEqual([{ name: 'scene', type: 'text', required: false }]);
  });

  it('carries every field of a declaration through the one dialog', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('scene');
    panel.setFormLabel('Scene name');
    panel.setFormDefault('Live');
    panel.setFormRequired(true);
    panel.submitCreate();

    expect(emitted).toEqual([
      { name: 'scene', type: 'text', required: true, label: 'Scene name', defaultValue: 'Live' },
    ]);
  });

  it('settles the name the dialog was given, so a pasted one is usable', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('My Scene');
    panel.submitCreate();

    expect(emitted[0].name).toBe('my_scene');
  });

  it('settles a name given in its `vars.` reading form', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('vars.scene');
    panel.submitCreate();

    expect(emitted[0].name).toBe('scene');
  });

  it('seeds the fields a type needs to be usable at all', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('muted');
    panel.setFormType('boolean');
    panel.submitCreate();

    expect(emitted[0])
      .toEqual({ name: 'muted', type: 'boolean', required: false, defaultValue: 'false' });
  });

  it('drops a default the new type cannot express when the type changes', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('volume');
    panel.setFormDefault('loud');
    panel.setFormType('numeric');
    panel.submitCreate();

    expect(emitted[0]).toEqual({ name: 'volume', type: 'numeric', required: false });
  });

  it('refuses a name the script already declares, and says so', () => {
    seed([{ name: 'scene', type: 'text' }]);

    panel.openCreate();
    panel.setFormName('scene');

    expect(panel.createNameTaken()).toBeTrue();
    expect(panel.canCreate()).toBeFalse();

    panel.submitCreate();
    expect(emitted).toEqual([]);
  });

  it('refuses a name that cannot be reduced to a variable name', () => {
    seed([]);

    panel.openCreate();
    panel.setFormName('!!!');

    expect(panel.canCreate()).toBeFalse();
  });

  it('warns rather than refuses when a name shadows a global', () => {
    seed([], ['scene', 'streaming']);

    panel.openCreate();
    panel.setFormName('scene');

    expect(panel.canCreate()).toBeTrue();
    expect(panel.createShadowWarning()).toContain('vars.scene');
  });

  it('says nothing about a name that shadows nothing', () => {
    seed([{ name: 'scene', type: 'text' }], ['streaming']);

    expect(panel.shadowWarning(0)).toBeNull();
  });

  it('reports a stored name that cannot be reduced to a variable name as an error', () => {
    seed([{ name: '!!!', type: 'text' }]);

    expect(panel.nameError(0)).toContain('lowercase');
    expect(panel.shadowWarning(0)).toBeNull();
  });

  it('rejects a name the script already declares', () => {
    seed([{ name: 'scene', type: 'text' }, { name: 'scene', type: 'numeric' }]);

    expect(panel.nameError(1)).toContain('already declares');
  });

  it('reports itself invalid while a stored declaration is unusable, and valid once it is gone', () => {
    seed([{ name: '!!!', type: 'text' }]);

    panel.openCreate();
    panel.setFormName('scene');
    panel.submitCreate();
    settle();
    expect(valid).toBeFalse();

    panel.requestDelete(0);
    panel.confirmDelete();
    settle();
    expect(valid).toBeTrue();
  });

  describe('editing a declaration', () => {
    const rows: ScriptInput[] = [
      { name: 'scene', type: 'text', label: 'Scene name', defaultValue: 'Live', required: true },
      { name: 'volume', type: 'numeric', defaultValue: '5' },
    ];

    it('opens the dialog carrying what the row currently declares', () => {
      seed(rows);

      panel.openEdit(0);

      expect(panel.editIndex()).toBe(0);
      expect(panel.form()).toEqual({
        name: 'scene',
        type: 'text',
        label: 'Scene name',
        defaultValue: 'Live',
        required: true,
      });
    });

    it('replaces the edited declaration and leaves its neighbour alone', () => {
      seed(rows);

      panel.openEdit(1);
      panel.setFormName('gain');
      panel.setFormDefault(9);
      panel.submitCreate();

      expect(emitted).toEqual([
        rows[0],
        { name: 'gain', type: 'numeric', required: false, defaultValue: '9' },
      ]);
    });

    it('allows a declaration to keep its own name', () => {
      seed(rows);

      panel.openEdit(0);
      panel.setFormLabel('Scene');

      expect(panel.createNameTaken()).toBeFalse();
      expect(panel.canCreate()).toBeTrue();

      panel.submitCreate();
      expect(emitted[0].name).toBe('scene');
      expect(emitted[0].label).toBe('Scene');
    });

    it('refuses a rename onto a name another declaration already uses', () => {
      seed(rows);

      panel.openEdit(1);
      panel.setFormName('scene');

      expect(panel.createNameTaken()).toBeTrue();
      expect(panel.canCreate()).toBeFalse();

      panel.submitCreate();
      expect(emitted).toEqual([]);
    });
  });

  describe('deleting a declaration', () => {
    const rows: ScriptInput[] = [
      { name: 'a', type: 'text' },
      { name: 'b', type: 'text' },
      { name: 'c', type: 'text' },
    ];

    it('asks before dropping a declaration callers already fill in', () => {
      seed(rows);

      panel.requestDelete(1);

      expect(panel.deleteCandidate()?.name).toBe('b');
      expect(emitted).toEqual([]);
    });

    it('keeps the declaration when the confirmation is dismissed', () => {
      seed(rows);

      panel.requestDelete(1);
      panel.cancelDelete();

      expect(panel.deleteCandidate()).toBeNull();
      expect(emitted).toEqual([]);
    });

    it('removes the declaration the user confirmed, not its neighbour', () => {
      seed(rows);

      panel.requestDelete(1);
      panel.confirmDelete();
      settle();

      expect(emitted.map(input => input.name)).toEqual(['a', 'c']);
    });
  });
});

describe('ScriptInputsPanelComponent list', () => {
  let fixture: ComponentFixture<ScriptInputsPanelComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ScriptInputsPanelComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiStub() },
      ],
    });

    fixture = TestBed.createComponent(ScriptInputsPanelComponent);
    fixture.componentRef.setInput('inputs', [
      { name: 'scene', type: 'text', label: 'Scene name', defaultValue: 'Live', required: true },
    ] satisfies ScriptInput[]);
    fixture.detectChanges();
  });

  it('presents a declaration instead of offering it for editing in place', () => {
    const row: HTMLElement = fixture.nativeElement.querySelector('.input-row');

    expect(row.textContent).toContain('vars.scene');
    expect(row.textContent).toContain('Scene name');
    expect(row.textContent).toContain('Live');
    expect(row.querySelectorAll('input, select, textarea').length).toBe(0);
  });
});

describe('ScriptInputsPanelComponent runs-on-widget toggle', () => {
  let fixture: ComponentFixture<ScriptInputsPanelComponent>;
  let emitted: boolean[];

  beforeEach(() => {
    emitted = [];
    TestBed.configureTestingModule({
      imports: [ScriptInputsPanelComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiStub() },
      ],
    });

    fixture = TestBed.createComponent(ScriptInputsPanelComponent);
    fixture.componentInstance.runsOnWidgetChange.subscribe(value => emitted.push(value));
    fixture.componentRef.setInput('inputs', []);
    fixture.detectChanges();
  });

  it('offers exactly text, numeric and boolean as declarable input types', () => {
    // The type select lives in the create/edit dialog, closed by default; open it, then open the
    // select's own listbox popup, so the rendered options actually exist in the DOM.
    (fixture.componentInstance as unknown as { openCreate(): void }).openCreate();
    fixture.detectChanges();

    const typeSelect: HTMLElement = fixture.nativeElement.querySelector('shared-select[name="type"]');
    typeSelect.querySelector<HTMLButtonElement>('button.control')!.click();
    fixture.detectChanges();

    // Queried from the document, not from the select: the popup renders in an overlay that is
    // lifted out of the dialog, because a dialog ancestor would otherwise clip it.
    const rendered = Array.from(
      document.querySelectorAll<HTMLButtonElement>('.sel-option'),
    ).map(option => option.textContent!.trim());

    expect(rendered).toEqual(['Text', 'Numeric', 'Boolean']);
  });

  it('reflects the input as unchecked by default', () => {
    const toggle: HTMLInputElement = fixture.nativeElement.querySelector('shared-toggle-switch input.ts-input');
    expect(toggle).not.toBeNull();
    expect(toggle.checked).toBeFalse();
  });

  it('emits the flipped value when switched on', () => {
    (fixture.componentInstance as unknown as { setRunsOnWidget(v: boolean): void }).setRunsOnWidget(true);

    expect(emitted).toEqual([true]);
    expect(fixture.componentInstance.runsOnWidget).toBeTrue();
  });

  it('emits false when switched back off', () => {
    fixture.componentRef.setInput('runsOnWidget', true);
    fixture.detectChanges();

    (fixture.componentInstance as unknown as { setRunsOnWidget(v: boolean): void }).setRunsOnWidget(false);

    expect(emitted).toEqual([false]);
  });
});
