import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionFlow, Script, ScriptInput, ScriptInputValue, ScriptUsage } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { ScriptService } from '../../../services/script.service';

import { ScriptsPageComponent } from './scripts-page.component';

interface PageAccess {
  selectedScriptId(): string | null;
  selectedScript(): Script | null;
  draftFlows(): ActionFlow[];
  draftInputs(): ScriptInput[];
  draftRunsOnWidget(): boolean;
  variables(): { name: string; origin?: string }[];
  pendingRun(): Script | null;
  onInputsChange(inputs: ScriptInput[]): void;
  onRunsOnWidgetChange(runsOnWidget: boolean): void;
  runWithInputs(inputs: Record<string, ScriptInputValue>): Promise<void>;
  isDirty(): boolean;
  canSave(): boolean;
  pendingSelection(): Script | null;
  pendingDelete(): Script | null;
  pendingDeleteUsages(): ScriptUsage[];
  deleteMessage(): string;
  select(script: Script): void;
  confirmDiscardAndSwitch(): void;
  onFlowsChange(flows: ActionFlow[]): void;
  onValidityChange(validity: { valid: boolean; errors: [] }): void;
  onInputsValidityChange(valid: boolean): void;
  save(): Promise<void>;
  discard(): void;
  run(): Promise<void>;
  requestDelete(): Promise<void>;
  confirmNavigation(): Promise<boolean> | boolean;
  confirmingLeave(): boolean;
  onDiscardAndLeave(): void;
  onKeepEditing(): void;
}

describe('ScriptsPageComponent', () => {
  let fixture: ComponentFixture<ScriptsPageComponent>;
  let page: PageAccess;
  let scripts: jasmine.SpyObj<ScriptService>;
  let toasts: jasmine.SpyObj<ToastService>;
  let stored: Script[];

  function script(
    id: string,
    name: string,
    children: unknown[] = [],
    inputs: ScriptInput[] = [],
    runsOnWidget = false,
  ): Script {
    return {
      id,
      name,
      description: '',
      flows: [{ triggerId: 'onRun', triggerType: 'onRun', children }] as ActionFlow[],
      inputs,
      runsOnWidget,
      createdAt: '2026-07-29T00:00:00Z',
      updatedAt: '2026-07-29T00:00:00Z',
    };
  }

  beforeEach(() => {
    stored = [script('a', 'Alpha'), script('b', 'Bravo')];

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());

    scripts = jasmine.createSpyObj<ScriptService>(
      'ScriptService',
      ['loadScripts', 'updateScript', 'deleteScript', 'duplicateScript', 'createScript', 'runScript', 'getUsages'],
      {
        scripts: (() => stored) as unknown as ScriptService['scripts'],
        sortedScripts: (() => stored) as unknown as ScriptService['sortedScripts'],
        isLoading: (() => false) as unknown as ScriptService['isLoading'],
        loadError: (() => null) as unknown as ScriptService['loadError'],
      },
    );
    scripts.loadScripts.and.resolveTo();
    scripts.updateScript.and.resolveTo({ success: true, data: stored[0] });
    scripts.deleteScript.and.resolveTo({ success: true, data: undefined });
    scripts.runScript.and.resolveTo({ success: true, data: undefined });
    scripts.getUsages.and.resolveTo([]);

    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      imports: [ScriptsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ScriptService, useValue: scripts },
        { provide: ToastService, useValue: toasts },
      ],
    })
      .overrideComponent(ScriptsPageComponent, { set: { template: '', imports: [] } });

    fixture = TestBed.createComponent(ScriptsPageComponent);
    page = fixture.componentInstance as unknown as PageAccess;
  });

  function selectFirst(): void {
    page.select(stored[0]);
  }

  it('copies the stored flow into the draft on selection', () => {
    selectFirst();

    expect(page.selectedScriptId()).toBe('a');
    expect(page.draftFlows().length).toBe(1);
    expect(page.isDirty()).toBeFalse();
  });

  it('asks before switching away from unsaved changes', () => {
    selectFirst();
    page.onFlowsChange([]);

    page.select(stored[1]);

    expect(page.pendingSelection()?.id).toBe('b');
    expect(page.selectedScriptId()).toBe('a');
  });

  it('switches once the discard is confirmed', () => {
    selectFirst();
    page.onFlowsChange([]);
    page.select(stored[1]);

    page.confirmDiscardAndSwitch();

    expect(page.selectedScriptId()).toBe('b');
    expect(page.isDirty()).toBeFalse();
  });

  it('switches straight away when nothing was edited', () => {
    selectFirst();

    page.select(stored[1]);

    expect(page.pendingSelection()).toBeNull();
    expect(page.selectedScriptId()).toBe('b');
  });

  it('does not allow saving an invalid flow', () => {
    selectFirst();
    page.onFlowsChange([]);
    page.onValidityChange({ valid: false, errors: [] });

    expect(page.canSave()).toBeFalse();
  });

  it('does not allow saving while an input declaration is unusable, and allows it once it is not', async () => {
    selectFirst();
    page.onFlowsChange([{ triggerId: 'onRun', triggerType: 'onRun', children: [] }] as ActionFlow[]);
    page.onInputsChange([{ name: '', type: 'text', required: false }]);
    page.onInputsValidityChange(false);

    expect(page.canSave()).toBeFalse();
    await page.save();
    expect(scripts.updateScript).not.toHaveBeenCalled();

    page.onInputsChange([{ name: 'scene', type: 'text', required: false }]);
    page.onInputsValidityChange(true);

    expect(page.canSave()).toBeTrue();
  });

  it('persists the draft flow on save', async () => {
    selectFirst();
    const edited = [{ triggerId: 'onRun', triggerType: 'onRun', children: [{ id: 'b1' }] }] as ActionFlow[];
    page.onFlowsChange(edited);

    await page.save();

    expect(scripts.updateScript).toHaveBeenCalledWith('a', { flows: edited, inputs: [], runsOnWidget: false });
    expect(page.isDirty()).toBeFalse();
  });

  it('restores the stored flow on discard', () => {
    selectFirst();
    page.onFlowsChange([]);

    page.discard();

    expect(page.draftFlows()).toEqual(stored[0].flows);
    expect(page.isDirty()).toBeFalse();
  });

  it('refuses to run while the draft differs from what the host would execute', async () => {
    selectFirst();
    page.onFlowsChange([]);

    await page.run();

    expect(scripts.runScript).not.toHaveBeenCalled();
    expect(toasts.show).toHaveBeenCalled();
  });

  it('runs the saved script when there is nothing pending', async () => {
    selectFirst();

    await page.run();

    expect(scripts.runScript).toHaveBeenCalledWith('a', undefined);
  });

  it('names what the delete breaks', async () => {
    scripts.getUsages.and.resolveTo([
      { kind: 'widget', location: 'Default Profile / Root', count: 2 },
      { kind: 'script', location: 'Bravo', count: 1 },
    ]);
    selectFirst();

    await page.requestDelete();

    expect(page.pendingDelete()?.id).toBe('a');
    expect(page.deleteMessage()).toContain('2 widgets');
    expect(page.deleteMessage()).toContain('1 other script');
    expect(page.deleteMessage()).toContain('Default Profile / Root');
  });

  it('states plainly that an unused script is simply gone', async () => {
    selectFirst();

    await page.requestDelete();

    expect(page.deleteMessage()).toContain('deleted permanently');
    expect(page.deleteMessage()).not.toContain('is run by');
  });

  describe('leaving the page', () => {
    it('lets the router through while the draft matches what is stored', () => {
      selectFirst();

      expect(page.confirmNavigation()).toBeTrue();
      expect(page.confirmingLeave()).toBeFalse();
    });

    it('holds the navigation and asks while the flow has unsaved edits', async () => {
      selectFirst();
      page.onFlowsChange([]);

      const leaving = page.confirmNavigation();
      expect(page.confirmingLeave()).toBeTrue();

      page.onDiscardAndLeave();
      await expectAsync(leaving as Promise<boolean>).toBeResolvedTo(true);
      expect(page.confirmingLeave()).toBeFalse();
    });

    it('holds the navigation and asks while the declarations have unsaved edits', async () => {
      selectFirst();
      page.onInputsChange([{ name: 'scene', type: 'text' }]);

      const leaving = page.confirmNavigation();
      expect(page.confirmingLeave()).toBeTrue();

      page.onKeepEditing();
      await expectAsync(leaving as Promise<boolean>).toBeResolvedTo(false);
      expect(page.selectedScriptId()).toBe('a');
    });
  });

  describe('inputs', () => {
    const SCENE: ScriptInput = { name: 'scene', type: 'text', required: true };

    it('copies the stored declarations into the draft on selection', () => {
      stored = [script('a', 'Alpha', [], [SCENE])];

      selectFirst();

      expect(page.draftInputs()).toEqual([SCENE]);
    });

    it('offers the declared inputs to the flow picker, marked as inputs', () => {
      stored = [script('a', 'Alpha', [], [SCENE])];
      selectFirst();

      const scene = page.variables().find(variable => variable.name === 'scene');
      expect(scene?.origin).toBe('input');
    });

    it('saves the declarations together with the flow', async () => {
      selectFirst();
      page.onInputsChange([SCENE]);

      await page.save();

      expect(scripts.updateScript).toHaveBeenCalledWith('a', jasmine.objectContaining({
        inputs: [SCENE],
      }));
    });

    it('marks the script unsaved when a declaration changes', () => {
      selectFirst();

      page.onInputsChange([SCENE]);

      expect(page.isDirty()).toBeTrue();
    });

    it('restores the stored declarations on discard', () => {
      stored = [script('a', 'Alpha', [], [SCENE])];
      selectFirst();
      page.onInputsChange([]);

      page.discard();

      expect(page.draftInputs()).toEqual([SCENE]);
      expect(page.isDirty()).toBeFalse();
    });

    it('asks for values before running a script that declares inputs', async () => {
      stored = [script('a', 'Alpha', [], [SCENE])];
      selectFirst();

      await page.run();

      expect(page.pendingRun()?.id).toBe('a');
      expect(scripts.runScript).not.toHaveBeenCalled();
    });

    it('runs straight away when the script declares nothing', async () => {
      selectFirst();

      await page.run();

      expect(page.pendingRun()).toBeNull();
      expect(scripts.runScript).toHaveBeenCalledWith('a', undefined);
    });

    it('sends the values the dialog collected', async () => {
      stored = [script('a', 'Alpha', [], [SCENE])];
      selectFirst();
      await page.run();

      await page.runWithInputs({ scene: 'Live' });

      expect(scripts.runScript).toHaveBeenCalledWith('a', { scene: 'Live' });
      expect(page.pendingRun()).toBeNull();
    });
  });

  describe('runs on a widget', () => {
    it('copies the stored flag into the draft on selection', () => {
      stored = [script('a', 'Alpha', [], [], true)];

      selectFirst();

      expect(page.draftRunsOnWidget()).toBeTrue();
    });

    it('marks the script unsaved when the flag changes', () => {
      selectFirst();

      page.onRunsOnWidgetChange(true);

      expect(page.draftRunsOnWidget()).toBeTrue();
      expect(page.isDirty()).toBeTrue();
    });

    it('saves the flag together with the flow', async () => {
      selectFirst();
      page.onRunsOnWidgetChange(true);

      await page.save();

      expect(scripts.updateScript).toHaveBeenCalledWith('a', jasmine.objectContaining({
        runsOnWidget: true,
      }));
    });

    it('restores the stored flag on discard', () => {
      stored = [script('a', 'Alpha', [], [], true)];
      selectFirst();
      page.onRunsOnWidgetChange(false);

      page.discard();

      expect(page.draftRunsOnWidget()).toBeTrue();
      expect(page.isDirty()).toBeFalse();
    });
  });
});
