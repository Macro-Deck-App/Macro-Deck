import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ActionBlock, ActionBlockParameter, EventDefinition, GetActionParameterOptionsResponse, WIDGET_TARGET_SELF } from '@macro-deck/runtime';
import { ApiService, IconImageService, VariableService } from '@shared';
import { ActionOptionsService, ResolvedActionParameterOption } from '../../../../services/action-options.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { WidgetTargetPickerComponent } from '../../../forms/widget-target-picker/widget-target-picker.component';
import { ParamRowComponent } from './param-row.component';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

describe('ParamRowComponent host-backed option labels', () => {
  let component: ParamRowComponent;
  let store: jasmine.SpyObj<ActionFlowStore>;
  let loadLabeledOptions: jasmine.Spy;

  function makeParam(): ActionBlockParameter {
    return { name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123' };
  }

  beforeEach(() => {
    store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore', ['updateParam', 'updateEventParamOperator', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);
    loadLabeledOptions = jasmine.createSpy('loadLabeledOptions').and.resolveTo({ options: [] });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: { loadLabeledOptions: (...args: unknown[]) => loadLabeledOptions(...args) } },
      ],
    });

    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.param = makeParam();
  });

  it('captures the picked option label alongside the value', () => {
    component.dynOptions.set([{ value: 'guid-123', label: 'Living Room' }]);

    component.onOptionChange('guid-123');

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('folderId');
    expect(args[2] as string).toBe('guid-123');
    expect(args[3]).toBe('Living Room');
  });

  it('caches no label for a value that matches no option (clearing any stale one)', () => {
    component.dynOptions.set([{ value: 'guid-123', label: 'Living Room' }]);

    component.onOptionChange('typed-custom-value');

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[2] as string).toBe('typed-custom-value');
    expect(args[3]).toBeUndefined();
  });

  it('seeds the collapsed dynamic-choice trigger with the cached label', () => {
    component.param = { ...makeParam(), valueLabel: 'Living Room' };

    expect(component.dynamicChoiceOptions).toEqual([{ value: 'guid-123', label: 'Living Room' }]);
  });

  it('opens the autocomplete search unfiltered for a picked (labelled) value', () => {
    component.param = {
      name: 'trackUri', type: 'autocomplete', label: 'Track',
      value: 'spotify:track:xyz', valueLabel: 'Bohemian Rhapsody',
    };

    expect(component.autocompleteOpenFilter).toBe('');
  });

  it('clearing an autocomplete drops the cached label with the value (issue #136)', () => {
    component.param = {
      name: 'trackUri', type: 'autocomplete', label: 'Track',
      value: 'spotify:track:xyz', valueLabel: 'Bohemian Rhapsody',
    };
    component.dynOptions.set([{ value: 'spotify:track:xyz', label: 'Bohemian Rhapsody' }]);

    component.onOptionChange('');

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[2] as string).toBe('');
    expect(args[3]).toBeUndefined();
  });

  it('seeds the autocomplete search with the value itself when it is free text (no label)', () => {
    component.param = { name: 'process', type: 'autocomplete', label: 'Process', value: 'notepad' };

    expect(component.autocompleteOpenFilter).toBe('notepad');
  });

  it('names an autocomplete value from the loaded options when no label was cached (issue #142)', () => {
    component.param = {
      name: 'device', type: 'autocomplete', label: 'Device',
      value: '58d7bba576c581707fe3033420c480850a24a7e5',
    };
    component.dynOptions.set([
      { value: '58d7bba576c581707fe3033420c480850a24a7e5', label: 'MacBook Pro von Manuel (Computer)' },
    ]);

    expect(component.autocompleteDisplayLabel).toBe('MacBook Pro von Manuel (Computer)');
    expect(component.autocompleteOpenFilter).toBe('');
  });

  it('keeps free text searchable when the loaded options do not name it', () => {
    component.param = { name: 'process', type: 'autocomplete', label: 'Process', value: 'notepad' };
    component.dynOptions.set([{ value: 'chrome.exe', label: 'Google Chrome' }]);

    expect(component.autocompleteDisplayLabel).toBe('');
    expect(component.autocompleteOpenFilter).toBe('notepad');
  });

  it('loads the options for an autocomplete whose value carries no label', () => {
    component.param = { name: 'device', type: 'autocomplete', label: 'Device', value: 'dev-1' };
    component.param.dynamicOptions = true;
    component.block = { integrationId: 'app.test', actionId: 'play-on-device' } as never;
    spyOn(component, 'loadDynamicOptions');

    component.ngOnInit();

    expect(component.loadDynamicOptions).toHaveBeenCalled();
  });

  it('does not refetch an autocomplete that already knows its label', () => {
    component.param = {
      name: 'device', type: 'autocomplete', label: 'Device', value: 'dev-1', valueLabel: 'Kitchen',
    };
    component.param.dynamicOptions = true;
    component.block = { integrationId: 'app.test', actionId: 'play-on-device' } as never;
    spyOn(component, 'loadDynamicOptions');

    component.ngOnInit();

    expect(component.loadDynamicOptions).not.toHaveBeenCalled();
  });

  it('refetches a required persisted configuration and marks a deleted selection unavailable', async () => {
    component.param = {
      name: 'configuration', type: 'dynamic-choice', label: 'Configuration', value: 'guid-deleted',
      valueLabel: 'Studio OBS', required: true, dynamicOptions: true,
    };
    component.block = {
      integrationId: 'app.macro-deck.obs',
      actionId: 'toggle-recording',
      parameters: [component.param],
    } as ActionBlock;

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(loadLabeledOptions).toHaveBeenCalled();
    expect(component.dynamicChoiceOptions).toEqual([
      jasmine.objectContaining({
        value: 'guid-deleted',
        label: jasmine.stringMatching(/no longer available/i),
        disabled: true,
      }),
    ]);
  });

  it('stores what a number box was given as a number', async () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'delay', type: 'number', label: 'Delay', value: 10,
    } satisfies ActionBlockParameter);
    fixture.detectChanges();
    await fixture.whenStable();

    const box = fixture.nativeElement.querySelector('input[type="number"]') as HTMLInputElement;
    box.value = '25';
    box.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(store.updateParam.calls.mostRecent().args[2] as unknown).toBe(25);
  });

  it('lets an optional number parameter be emptied', async () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'delay', type: 'number', label: 'Delay', value: 10,
    } satisfies ActionBlockParameter);
    fixture.detectChanges();
    await fixture.whenStable();

    const box = fixture.nativeElement.querySelector('input[type="number"]') as HTMLInputElement;
    box.value = '';
    box.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(store.updateParam.calls.mostRecent().args[2] as unknown).toBe('');
  });

  it('renders what an empty pick list means, as the provider named it', () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'instance', type: 'dynamic-choice', label: 'Player', value: '',
      placeholder: 'First available',
    } satisfies ActionBlockParameter);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.sel-placeholder')?.textContent?.trim())
      .toBe('First available');
  });

  it('falls back to a generic prompt for a pick list that names nothing', () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'instance', type: 'dynamic-choice', label: 'Player', value: '',
    } satisfies ActionBlockParameter);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.sel-placeholder')?.textContent?.trim()).toBe('Select…');
  });

  it('clears an optional pick list back to empty (issue #475)', async () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'sceneName', type: 'choice', label: 'Scene', value: 'Live',
      placeholder: 'Any scene',
      options: [{ value: 'Live', label: 'Live' }, { value: 'Intro', label: 'Intro' }],
    } satisfies ActionBlockParameter);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.sel-clear').click();
    fixture.detectChanges();

    expect(store.updateParam.calls.mostRecent().args[2] as string).toBe('');
    expect(fixture.nativeElement.querySelector('.sel-label')?.textContent?.trim()).toBe('Any scene');
  });

  it('drops the cached label when a dynamic pick list is cleared', async () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'deviceId', type: 'dynamic-choice', label: 'Device', value: 'dev-1',
      valueLabel: 'Kitchen', placeholder: 'Any device',
    } satisfies ActionBlockParameter);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.sel-clear').click();
    fixture.detectChanges();

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[2] as string).toBe('');
    expect(args[3]).toBeUndefined();
  });

  it('offers no clear button for a required pick list or an already empty one', async () => {
    const render = async (param: ActionBlockParameter): Promise<HTMLElement> => {
      const fixture = TestBed.createComponent(ParamRowComponent);
      fixture.componentRef.setInput('blockId', 'block-1');
      fixture.componentRef.setInput('param', param);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture.nativeElement;
    };

    expect((await render({
      name: 'variable', type: 'choice', label: 'Variable', value: 'counter', required: true,
      options: [{ value: 'counter', label: 'counter' }],
    })).querySelector('.sel-clear')).toBeNull();

    expect((await render({
      name: 'sceneName', type: 'choice', label: 'Scene', value: '', options: [],
    })).querySelector('.sel-clear')).toBeNull();
  });

  // A literal-only Boolean cannot be variable-bound, so it must
  // render the plain switch and offer no variable picker, without changing how an ordinary boolean
  // parameter renders elsewhere (the guard the second case below is for).
  it('renders a literal-only boolean as a toggle switch with no variable picker', () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'autoStart', type: 'boolean', label: 'Auto start', value: false, literalOnly: true,
    } satisfies ActionBlockParameter);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-toggle-switch')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('shared-checkbox')).toBeNull();
    expect(fixture.nativeElement.querySelector('shared-variable-picker')).toBeNull();
  });

  it('keeps rendering an ordinary boolean as a checkbox with its variable picker', () => {
    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', {
      name: 'enabled', type: 'boolean', label: 'Enabled', value: false,
    } satisfies ActionBlockParameter);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-checkbox')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('shared-toggle-switch')).toBeNull();
    expect(fixture.nativeElement.querySelector('shared-variable-picker')).not.toBeNull();
  });

  it('offers the external variable picker only for scalar primitive types', () => {
    const withType = (type: ActionBlockParameter['type']): boolean => {
      component.param = { name: 'p', type, label: 'P', value: '' };
      return component.showsExternalVariablePicker;
    };

    for (const type of ['number', 'duration', 'boolean'] as const) {
      expect(withType(type))
        .withContext(`external picker should show for ${type}`)
        .toBeTrue();
    }

    for (const type of ['choice', 'dynamic-choice', 'color', 'datetime', 'file', 'folder'] as const) {
      expect(withType(type))
        .withContext(`external picker should be hidden for ${type}`)
        .toBeFalse();
    }
  });

  it('hides the external variable picker when references are disabled', () => {
    component.param = { name: 'p', type: 'number', label: 'P', value: 0 };
    component.allowReferences = false;

    expect(component.showsExternalVariablePicker).toBeFalse();
  });

  it('offers the reset sentinel for a colour parameter that supports reset', () => {
    component.param = { name: 'color', type: 'color', label: 'Color', value: '', supportsReset: true };

    expect(component.colorResetValue).toBe('$reset');
  });

  it('offers no reset for a colour parameter that does not support it', () => {
    component.param = { name: 'color', type: 'color', label: 'Color', value: '' };

    expect(component.colorResetValue).toBeUndefined();
  });

  it('hides the external variable picker for a literal-only boolean', () => {
    component.param = { name: 'p', type: 'boolean', label: 'P', value: false, literalOnly: true };

    expect(component.showsExternalVariablePicker).toBeFalse();
  });

  it('adds https to a configured URL field when it loses focus without a protocol', () => {
    component.param = {
      name: 'url', type: 'url', label: 'Website URL', value: 'macro-deck.app', autoPrefixHttps: true,
    };

    component.onUrlBlur('macro-deck.app');

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('url');
    expect(args[2] as string).toBe('https://macro-deck.app');
  });

  it('does not rewrite URL fields that have not opted into HTTPS prefixing', () => {
    component.param = { name: 'url', type: 'url', label: 'URL', value: 'bot.local:8087' };

    component.onUrlBlur('bot.local:8087');

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('url');
    expect(args[2] as string).toBe('bot.local:8087');
  });
});

// Not `jasmine.createSpyObj<ActionFlowStore>` (or a `Pick` of it): typing the spy against
// `cacheParamLabel`'s signature runs a plain `toHaveBeenCalledTimes()`/`.not.toHaveBeenCalled()`
// into "Type instantiation is excessively deep" here - `ParameterValue`'s self-referential array
// case is enough on its own. The untyped overload returns the same runtime spy with no such issue.
interface RecoveredLabelStoreSpy {
  updateParam: jasmine.Spy;
  errorsFor: jasmine.Spy;
  pickerVariables: jasmine.Spy;
  cacheParamLabel: jasmine.Spy;
}

describe('ParamRowComponent recovered option label', () => {
  let component: ParamRowComponent;
  let store: RecoveredLabelStoreSpy;
  let loadLabeledOptions: jasmine.Spy;

  function makeComponent(param: ActionBlockParameter): void {
    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.param = param;
    component.block = { integrationId: 'app.test', actionId: 'change-folder' } as ActionBlock;
  }

  beforeEach(() => {
    store = jasmine.createSpyObj('ActionFlowStore', ['updateParam', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);
    loadLabeledOptions = jasmine.createSpy('loadLabeledOptions').and.resolveTo({ options: [] });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: { loadLabeledOptions: (...args: unknown[]) => loadLabeledOptions(...args) } },
      ],
    });
  });

  it('caches the resolved name', async () => {
    makeComponent({ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', dynamicOptions: true });
    loadLabeledOptions.and.resolveTo({
      options: [{ value: 'guid-000', label: 'Bedroom' }, { value: 'guid-123', label: 'Living Room' }],
    });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).toHaveBeenCalledTimes(1);
    const args = store.cacheParamLabel.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('folderId');
    expect(args[2] as string).toBe('guid-123');
    expect(args[3]).toBe('Living Room');
  });

  it('a value the list does not name is not labelled', async () => {
    makeComponent({ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', dynamicOptions: true });
    loadLabeledOptions.and.resolveTo({ options: [{ value: 'guid-000', label: 'Bedroom' }] });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).not.toHaveBeenCalled();
    expect(component.dynOptions()).not.toBeNull();
  });

  it('a label identical to the value is not a name', async () => {
    makeComponent({ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', dynamicOptions: true });
    loadLabeledOptions.and.resolveTo({ options: [{ value: 'guid-123', label: 'guid-123' }] });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).not.toHaveBeenCalled();
  });

  it('an already-labelled parameter is left alone', async () => {
    makeComponent({
      name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123',
      valueLabel: 'Living Room', dynamicOptions: true,
    });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(loadLabeledOptions).not.toHaveBeenCalled();
    expect(store.cacheParamLabel).not.toHaveBeenCalled();
  });

  it('a nested row never caches', async () => {
    makeComponent({ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', dynamicOptions: true });
    component.nested = true;
    loadLabeledOptions.and.resolveTo({
      options: [{ value: 'guid-000', label: 'Bedroom' }, { value: 'guid-123', label: 'Living Room' }],
    });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).not.toHaveBeenCalled();
  });

  it('a stale resolution is discarded', async () => {
    makeComponent({ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', dynamicOptions: true });
    let resolveOptions!: (value: { options: { value: string; label?: string }[] }) => void;
    loadLabeledOptions.and.returnValue(new Promise(r => { resolveOptions = r; }));

    component.ngOnInit();
    component.param = { ...component.param, value: 'guid-999' };
    resolveOptions({ options: [{ value: 'guid-123', label: 'Living Room' }] });
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).not.toHaveBeenCalled();
  });

  it('the current value is still cached after a change', async () => {
    makeComponent({ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', dynamicOptions: true });
    let resolveOptions!: (value: { options: { value: string; label?: string }[] }) => void;
    loadLabeledOptions.and.returnValue(new Promise(r => { resolveOptions = r; }));

    component.ngOnInit();
    component.param = { ...component.param, value: 'guid-999' };
    resolveOptions({ options: [{ value: 'guid-999', label: 'Kitchen' }] });
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).toHaveBeenCalledTimes(1);
    const args = store.cacheParamLabel.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('folderId');
    expect(args[2] as string).toBe('guid-999');
    expect(args[3]).toBe('Kitchen');
  });

  it('the "no longer available" sentence is never cached', async () => {
    makeComponent({
      name: 'configuration', type: 'dynamic-choice', label: 'Configuration', value: 'guid-deleted',
      required: true, dynamicOptions: true,
    });
    loadLabeledOptions.and.resolveTo({ options: [{ value: 'guid-live', label: 'Studio OBS' }] });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(component.dynamicChoiceOptions[0]).toEqual(jasmine.objectContaining({
      value: 'guid-deleted',
      label: jasmine.stringMatching(/no longer available/i),
      disabled: true,
    }));
    expect(store.cacheParamLabel).not.toHaveBeenCalled();
  });

  it('a required value that is still available is cached normally', async () => {
    makeComponent({
      name: 'configuration', type: 'dynamic-choice', label: 'Configuration', value: 'guid-live',
      required: true, dynamicOptions: true,
    });
    loadLabeledOptions.and.resolveTo({ options: [{ value: 'guid-live', label: 'Studio OBS' }] });

    component.ngOnInit();
    await Promise.resolve();
    await Promise.resolve();

    expect(store.cacheParamLabel).toHaveBeenCalledTimes(1);
    const args = store.cacheParamLabel.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('configuration');
    expect(args[2] as string).toBe('guid-live');
    expect(args[3]).toBe('Studio OBS');
    expect(component.dynamicChoiceOptions.some(o => o.disabled)).toBeFalse();
  });
});

describe('ParamRowComponent multiselect saved values', () => {
  let store: jasmine.SpyObj<ActionFlowStore>;
  let loadLabeledOptions: jasmine.Spy;
  let fixture: ComponentFixture<ParamRowComponent>;

  function macrotask(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, 0));
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await macrotask();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function render(param: ActionBlockParameter): HTMLElement {
    fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('block', { integrationId: 'app.test', actionId: 'set-scenes' } as ActionBlock);
    fixture.componentRef.setInput('param', param);
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore', ['updateParam', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);
    loadLabeledOptions = jasmine.createSpy('loadLabeledOptions').and.resolveTo({ options: [] });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: { loadLabeledOptions: (...args: unknown[]) => loadLabeledOptions(...args) } },
      ],
    });
  });

  it('renders names without opening the dropdown', async () => {
    loadLabeledOptions.and.resolveTo({
      options: [
        { value: 'guid-1', label: 'Intro' }, { value: 'guid-2', label: 'Outro' }, { value: 'guid-3', label: 'Break' },
      ],
    });
    const root = render({
      name: 'scenes', type: 'multiselect', label: 'Scenes', value: ['guid-1', 'guid-2'], dynamicOptions: true,
    });

    await settle();

    expect(root.querySelector('.ms-summary')?.textContent?.trim()).toBe('Intro, Outro');
    expect(root.querySelector('.ms-placeholder')).toBeNull();
    expect(root.querySelectorAll('.ms-option').length).toBe(0);
  });

  it('an empty selection asks the host nothing', async () => {
    const root = render({
      name: 'scenes', type: 'multiselect', label: 'Scenes', value: [], dynamicOptions: true,
    });

    await settle();

    expect(loadLabeledOptions).not.toHaveBeenCalled();
    expect(root.querySelector('.ms-placeholder')).not.toBeNull();
  });

  it('a statically-optioned multiselect stays local', async () => {
    const root = render({
      name: 'scenes', type: 'multiselect', label: 'Scenes', value: ['a'],
      options: [{ value: 'a', label: 'Intro' }, { value: 'b', label: 'Outro' }],
    });

    await settle();

    expect(root.querySelector('.ms-summary')?.textContent?.trim()).toBe('Intro');
    expect(loadLabeledOptions).not.toHaveBeenCalled();
  });

  it('an unnamed value stays visible as its id', async () => {
    loadLabeledOptions.and.resolveTo({ options: [{ value: 'guid-1', label: 'Intro' }] });
    const root = render({
      name: 'scenes', type: 'multiselect', label: 'Scenes', value: ['guid-1', 'guid-gone'], dynamicOptions: true,
    });

    await settle();

    expect(root.querySelector('.ms-summary')?.textContent?.trim()).toBe('Intro, guid-gone');
  });
});

describe('ParamRowComponent multiline string parameter (issue #234)', () => {
  function render(param: ActionBlockParameter): HTMLElement {
    TestBed.resetTestingModule();
    const store = jasmine.createSpyObj<ActionFlowStore>('ActionFlowStore', ['updateParam', 'errorsFor', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    (store as unknown as Record<string, unknown>)['pickerVariables'] = () => [];
    (store as unknown as Record<string, unknown>)['previewScope'] = () => 'global';
    (store as unknown as Record<string, unknown>)['previewScopeRefId'] = () => undefined;

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: {} },
        { provide: VariableService, useValue: { variables: () => [] } },
      ],
    });

    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', param);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders a multiline string parameter with the editor label field row count', () => {
    const root = render({ name: 'label', type: 'string', label: 'Label', value: '', multiline: true });

    const editor = root.querySelector<HTMLElement>('.vti-editor');
    expect(editor?.style.getPropertyValue('--vti-rows').trim()).toBe('5');
  });

  it('leaves a single-line string parameter single-line', () => {
    const root = render({ name: 'title', type: 'string', label: 'Title', value: '' });

    const editor = root.querySelector<HTMLElement>('.vti-editor');
    expect(editor?.classList.contains('vti-multiline')).toBeFalse();
    expect(editor?.style.getPropertyValue('--vti-rows')).toBe('');
  });
});

describe('ParamRowComponent widget target', () => {
  function setUp(
    scope: 'global' | 'widget',
    scopeRefId?: string,
    runsOnWidget = false): { component: ParamRowComponent; fixture: ComponentFixture<ParamRowComponent> } {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        ActionFlowStore,
        { provide: ActionOptionsService, useValue: { loadLabeledOptions: () => Promise.resolve({ options: [] }) } },
      ],
    });

    const store = TestBed.inject(ActionFlowStore);
    store.previewScope.set(scope);
    store.previewScopeRefId.set(scopeRefId);
    store.scriptRunsOnWidget.set(runsOnWidget);

    const fixture = TestBed.createComponent(ParamRowComponent);
    const component = fixture.componentInstance;
    component.blockId = 'block-1';
    component.param = { name: 'widget', type: 'widget-target', label: 'Widget', value: '$self' };
    return { component, fixture };
  }

  it('recognises a widget flow by its preview scope', () => {
    expect(setUp('widget', 'widget-1').component.hasOwnerWidget).toBeTrue();
  });

  it('recognises the Scripts and Automations pages as having no owning widget', () => {
    expect(setUp('global').component.hasOwnerWidget).toBeFalse();
  });

  it('offers this widget inside a script that runs on a widget', () => {
    const { component, fixture } = setUp('global', undefined, true);
    fixture.detectChanges();

    expect(component.hasOwnerWidget).toBeTrue();
    const picker = fixture.debugElement
      .query(By.directive(WidgetTargetPickerComponent))?.componentInstance as WidgetTargetPickerComponent;
    expect(picker).toBeTruthy();
    expect(picker.isSelf).toBeTrue();
  });

  it('does not offer this widget inside a script that does not run on one', () => {
    expect(setUp('global', undefined, false).component.hasOwnerWidget).toBeFalse();
  });

  it('does not claim an owning widget without an id for it', () => {
    expect(setUp('widget').component.hasOwnerWidget).toBeFalse();
  });

  it('does not offer the variable picker for a widget target', () => {
    expect(setUp('widget', 'widget-1').component.showsExternalVariablePicker).toBeFalse();
  });
});

describe('ParamRowComponent widget-target option fetch', () => {
  let component: ParamRowComponent;
  let options: jasmine.SpyObj<ActionOptionsService>;

  function setUp(param: ActionBlockParameter, block?: ActionBlock): void {
    const store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore', ['updateParam', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);

    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['loadLabeledOptions']);
    options.loadLabeledOptions.and.resolveTo({ options: [] });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: options },
      ],
    });

    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.param = param;
    component.block = block;
  }

  it('resolves the Run Script widget row by source id even though its block has an owning action', async () => {
    setUp(
      { name: 'widget', type: 'widget-target', label: 'Widget', value: '', optionsSourceId: 'macrodeck.widgets' },
      {
        id: 'block-1',
        type: 'action',
        blockType: 'app.macro-deck.scripts.run-script',
        label: 'Run Script',
        color: 'var(--color-accent)',
        integrationId: 'app.macro-deck.scripts',
        actionId: 'run-script',
        parameters: [],
      },
    );

    component.loadDynamicOptions();
    await Promise.resolve();

    expect(options.loadLabeledOptions).toHaveBeenCalledWith(jasmine.objectContaining({
      integrationId: '',
      actionId: '',
      optionsSourceId: 'macrodeck.widgets',
    }), jasmine.anything());
  });

  it('resolves the Run Remote Script widget row by source id too', async () => {
    setUp(
      { name: 'widget', type: 'widget-target', label: 'Widget', value: '', optionsSourceId: 'macrodeck.widgets' },
      {
        id: 'block-1',
        type: 'action',
        blockType: 'app.macro-deck.delegate.run-remote-script',
        label: 'Run Remote Script',
        color: 'var(--color-accent)',
        integrationId: 'app.macro-deck.delegate',
        actionId: 'run-remote-script',
        parameters: [],
      },
    );

    component.loadDynamicOptions();
    await Promise.resolve();

    expect(options.loadLabeledOptions).toHaveBeenCalledWith(jasmine.objectContaining({
      integrationId: '',
      actionId: '',
      optionsSourceId: 'macrodeck.widgets',
    }), jasmine.anything());
  });

  it('routes a plugin-declared widget target through its owning action, not by source id', async () => {
    setUp(
      { name: 'widget', type: 'widget-target', label: 'Widget', value: '', optionsSourceId: 'macrodeck.widgets', widgetTypes: ['clock'] },
      {
        id: 'block-1',
        type: 'action',
        blockType: 'plugin.example.setLabel',
        label: 'Set Label',
        color: 'var(--color-accent)',
        integrationId: 'plugin.example',
        actionId: 'setLabel',
        parameters: [],
      },
    );

    component.loadDynamicOptions();
    await Promise.resolve();

    expect(options.loadLabeledOptions).toHaveBeenCalledWith(jasmine.objectContaining({
      integrationId: 'plugin.example',
      actionId: 'setLabel',
      widgetTypes: ['clock'],
    }), jasmine.anything());
  });
});

describe('ParamRowComponent widget-action state options (issue #718)', () => {
  let component: ParamRowComponent;
  let options: jasmine.SpyObj<ActionOptionsService>;
  let store: ActionFlowStore;

  function setUp(
    widgetParamValue: string,
    actionId = 'set-state',
    hostOptions: ResolvedActionParameterOption[] = [],
  ): void {
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['loadLabeledOptions']);
    options.loadLabeledOptions.and.resolveTo({ options: hostOptions });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        ActionFlowStore,
        { provide: ActionOptionsService, useValue: options },
      ],
    });

    store = TestBed.inject(ActionFlowStore);
    store.previewScopeRefId.set('w-1');

    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.block = {
      id: 'block-1',
      type: 'action',
      blockType: `app.macro-deck.widget.${actionId}`,
      label: 'Set state',
      color: '',
      integrationId: 'app.macro-deck.widget',
      actionId,
      parameters: [{ name: 'widget', type: 'widget-target', label: 'Widget', value: widgetParamValue }],
    };
    component.param = { name: 'state', type: 'string', label: 'State', value: '', dynamicOptions: true };
  }

  // Test 4: today the row sends the literal `$self` sentinel, which the host cannot resolve to any
  // particular widget - it has to be resolved client-side to the flow's owner, the same way
  // `ParamListComponent.targetValue()` resolves it for the appearance-capability lookup.
  it('resolves a $self widget target to the flow owner before asking the host for state options', async () => {
    setUp(WIDGET_TARGET_SELF);

    component.loadDynamicOptions();
    await Promise.resolve();

    expect(options.loadLabeledOptions).toHaveBeenCalledWith(jasmine.objectContaining({
      currentParameters: jasmine.objectContaining({ widget: 'w-1' }),
    }), jasmine.anything());
  });

  // Test 5: the host's options endpoint answers from the widget's persisted data, so a state just
  // added in the open editor is invisible to it until Save - the picker must merge in the editor's
  // own draft list instead of the host's stale concrete states when the resolved target is the
  // widget currently being edited. `set-state` (its own resolver returns no sentinels) ends up with
  // just the draft list, matching its old all-concrete shape.
  it('offers a draft state not yet saved when the target is the widget being edited', async () => {
    setUp(WIDGET_TARGET_SELF, 'set-state', [{ value: 'off', label: 'Off' }]);
    store.previewScopeStates.set([{ id: 'off', label: 'Off' }, { id: 'draft-state', label: 'Draft State' }]);

    component.loadDynamicOptions();
    await Promise.resolve();

    expect(options.loadLabeledOptions).toHaveBeenCalled();
    expect(component.dynOptions()?.map(o => o.value)).toContain('draft-state');
  });

  // Test 6 (finding 1 regression): `state` is also the parameter name on every widget appearance
  // action (set-icon, set-label, ...), which legitimately offers the `current`/`both` sentinels and
  // defaults to `current`. Replacing the host's whole answer with the draft list (as the old
  // draft-states shortcut did, by returning before the host was ever asked) drops those sentinels -
  // `current` is not even in its own option list any more. This must be red against that code: the
  // fix has to merge the host's sentinel options with the draft states instead of replacing wholesale.
  it('keeps the current sentinel when merging draft states into a Set Icon action target at $self', async () => {
    setUp(WIDGET_TARGET_SELF, 'set-icon', [
      { value: 'current', label: 'Current' },
      { value: 'off', label: 'Off' },
    ]);
    store.previewScopeStates.set([{ id: 'off', label: 'Off' }, { id: 'draft-state', label: 'Draft State' }]);

    component.loadDynamicOptions();
    await Promise.resolve();

    const values = component.dynOptions()?.map(o => o.value);
    expect(values).toContain('current');
    expect(values).toContain('draft-state');
  });
});

describe('ParamRowComponent variable references', () => {
  let component: ParamRowComponent;

  function setUp(param: ActionBlockParameter): void {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: jasmine.createSpyObj<ActionFlowStore>('ActionFlowStore', ['updateParam', 'cacheParamLabel']) },
        { provide: ActionOptionsService, useValue: {} },
      ],
    });
    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.param = param;
  }

  it('renders a reference on a text field inline rather than swapping the whole row', () => {
    setUp({ name: 'text', type: 'string', label: 'Text', value: { $var: 'greeting' } });

    expect(component.isVariable).toBeFalse();
    expect(component.stringValue).toBe('{{ vars.greeting }}');
  });

  it('renders an event reference on a text field inline too', () => {
    setUp({ name: 'text', type: 'url', label: 'Url', value: { $event: 'target' } });

    expect(component.isVariable).toBeFalse();
    expect(component.stringValue).toBe('{{ event.target }}');
  });

  it('still swaps the whole row for a scalar field, which has no text editor', () => {
    setUp({ name: 'delay', type: 'number', label: 'Delay', value: { $var: 'wait' } });

    expect(component.isVariable).toBeTrue();
    expect(component.variableLabel).toBe('{{ vars.wait }}');
  });

  it('labels an event reference on a scalar field, which used to render as blank', () => {
    setUp({ name: 'delay', type: 'duration', label: 'Delay', value: { $event: 'wait' } });

    expect(component.isVariable).toBeTrue();
    expect(component.variableLabel).toBe('{{ event.wait }}');
  });

  it('keeps a literal-only field literal', () => {
    setUp({ name: 'text', type: 'string', label: 'Text', value: { $var: 'greeting' } });
    component.allowReferences = false;

    expect(component.isVariable).toBeTrue();
    expect(component.stringValue).toBe('');
  });
});

describe('ParamRowComponent icon parameter (issue #296)', () => {
  let store: jasmine.SpyObj<ActionFlowStore>;

  function render(value: string): HTMLElement {
    store = jasmine.createSpyObj<ActionFlowStore>('ActionFlowStore', ['updateParam', 'errorsFor', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: {} },
        { provide: IconImageService, useValue: { getIconUrl: () => null } },
      ],
    });

    const fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', { name: 'buttonIcon', type: 'icon', label: 'Icon', value } satisfies ActionBlockParameter);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the shared icon control instead of the deleted picker', () => {
    const root = render('icon-guid');

    expect(root.querySelector('shared-widget-icon-control')).not.toBeNull();
    expect(root.querySelector('shared-icon-picker')).toBeNull();
  });

  it('does not show the raw icon id as text, and offers Change icon', () => {
    const root = render('icon-guid');

    expect(root.textContent).not.toContain('icon-guid');
    expect(root.textContent).toContain('Change icon');
  });

  it('clearing the icon writes an empty value through the store', () => {
    const root = render('icon-guid');
    const removeButton = root.querySelector<HTMLButtonElement>(
      'shared-widget-icon-control shared-button[variant="ghost"] button');

    removeButton?.click();

    const args = store.updateParam.calls.mostRecent().args;
    expect(args[0]).toBe('block-1');
    expect(args[1]).toBe('buttonIcon');
    expect(args[2] as string).toBe('');
  });

  it('an empty icon value shows Choose icon and no Remove button', () => {
    const root = render('');

    expect(root.textContent).toContain('Choose icon');
    expect(root.querySelector('shared-widget-icon-control shared-button[variant="ghost"]')).toBeNull();
  });
});

describe('ParamRowComponent stale dynamic options', () => {
  let component: ParamRowComponent;
  let options: jasmine.SpyObj<ActionOptionsService>;

  function block(target: string): { id: string; integrationId: string; actionId: string;
    parameters: ActionBlockParameter[]; } {
    return {
      id: 'block-1',
      integrationId: 'app.macro-deck.widget',
      actionId: 'set-label',
      parameters: [
        { name: 'widget', type: 'widget-target', label: 'Widget', value: target },
        { name: 'state', type: 'dynamic-choice', label: 'State', value: 'current' },
      ],
    };
  }

  beforeEach(() => {
    const store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore', ['updateParam', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);

    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['loadLabeledOptions']);
    options.loadLabeledOptions.and.resolveTo({ options: [] });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: options },
      ],
    });

    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.param = {
      name: 'state', type: 'dynamic-choice', label: 'State', value: 'current', dynamicOptions: true,
    };
    component.block = block('a-clock') as never;
  });

  it('refetches an already-loaded list when a sibling parameter changes', async () => {
    component.loadDynamicOptions();
    await Promise.resolve();
    expect(options.loadLabeledOptions).toHaveBeenCalledTimes(1);

    component.block = block('a-toggle-button') as never;
    component.ngOnChanges();

    expect(options.loadLabeledOptions).toHaveBeenCalledTimes(2);
  });

  it('does not refetch when nothing a sibling holds actually changed', async () => {
    component.loadDynamicOptions();
    await Promise.resolve();

    component.block = block('a-clock') as never;
    component.ngOnChanges();

    expect(options.loadLabeledOptions).toHaveBeenCalledTimes(1);
  });

  it('does not fetch for a list that was never loaded', () => {
    component.block = block('a-toggle-button') as never;
    component.ngOnChanges();

    expect(options.loadLabeledOptions).not.toHaveBeenCalled();
  });
});

describe('ParamRowComponent dynamic-choice label preloading', () => {
  let options: jasmine.SpyObj<ActionOptionsService>;

  function create(param: ActionBlockParameter): ParamRowComponent {
    const component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'block-1';
    component.param = param;
    component.block = { id: 'block-1', integrationId: 'app.macro-deck.widget', actionId: 'set-label' } as never;
    component.ngOnInit();
    return component;
  }

  beforeEach(() => {
    const store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore', ['updateParam', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);

    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['loadLabeledOptions']);
    options.loadLabeledOptions.and.resolveTo({ options: [] });

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: options },
      ],
    });
  });

  it('loads the options for a value that has no cached label yet', () => {
    create({ name: 'state', type: 'dynamic-choice', label: 'State', value: 'current', dynamicOptions: true });

    expect(options.loadLabeledOptions).toHaveBeenCalled();
  });

  it('does not load when the label is already cached', () => {
    create({
      name: 'state', type: 'dynamic-choice', label: 'State', value: 'current',
      valueLabel: 'Current state', dynamicOptions: true,
    });

    expect(options.loadLabeledOptions).not.toHaveBeenCalled();
  });

  it('does not load for a value that is not set', () => {
    create({ name: 'state', type: 'dynamic-choice', label: 'State', value: '', dynamicOptions: true });

    expect(options.loadLabeledOptions).not.toHaveBeenCalled();
  });
});

describe('ParamRowComponent event filter operator', () => {
  let component: ParamRowComponent;
  let store: jasmine.SpyObj<ActionFlowStore>;

  const definition: EventDefinition = {
    id: 'obs::scene-changed',
    providerId: 'obs',
    providerName: 'OBS',
    isIntegration: true,
    name: 'Scene Changed',
    deliveryKind: 'push',
    configurationParameters: [
      { name: 'sceneName', type: 'string', label: 'Scene' },
      { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds' },
    ],
    payloadParameters: [
      { name: 'sceneName', type: 'string', label: 'Scene' },
      { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds' },
    ],
  };

  beforeEach(() => {
    store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore',
      ['updateParam', 'updateEventParam', 'updateEventParamOperator', 'errorsFor', 'pickerVariables', 'cacheParamLabel'],
    );
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: {} },
      ],
    });

    component = TestBed.createComponent(ParamRowComponent).componentInstance;
    component.blockId = 'trigger-1';
    component.eventId = definition.id;
    component.eventDefinition = definition;
  });

  it('shows the operator control for an eligible numeric filter', () => {
    component.param = { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 0 };

    expect(component.showsFilterOperator).toBeTrue();
  });

  it('shows it for a string parameter too - availability is a question every type can answer', () => {
    component.param = { name: 'sceneName', type: 'string', label: 'Scene', value: '' };

    expect(component.showsFilterOperator).toBeTrue();
  });

  it('shows it for a choice parameter too - availability is a question every type can answer', () => {
    component.param = {
      name: 'elapsedSeconds', type: 'choice', label: 'Elapsed seconds', value: '', options: [],
    };

    expect(component.showsFilterOperator).toBeTrue();
  });

  it('hides it when eventId is unset', () => {
    component.eventId = undefined;
    component.param = { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 0 };

    expect(component.showsFilterOperator).toBeFalse();
  });

  it('labels the button "is" when no operator is stored', () => {
    component.param = { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 0 };

    expect(component.filterOperatorLabel).toBe('is');
  });

  it('labels the button from the stored operator', () => {
    component.param = {
      name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 0, operator: '>',
    };

    expect(component.filterOperatorLabel).toBe('greater than');
  });

  it('writes the picked operator through the store', () => {
    component.param = { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 0 };

    component.pickFilterOperator('>');

    expect(store.updateEventParamOperator).toHaveBeenCalledWith('trigger-1', 'elapsedSeconds', '>');
  });

  it('keeps showing a stored operator on a parameter that no longer qualifies', () => {
    component.param = {
      name: 'elapsedSeconds', type: 'choice', label: 'Elapsed seconds', value: '', options: [], operator: '>',
    };

    expect(component.showsFilterOperator).toBeTrue();
  });

  it('hides it inside an object field or array item', () => {
    component.nested = true;
    component.param = { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 0 };

    expect(component.showsFilterOperator).toBeFalse();
  });
});

describe('ParamRowComponent event filter operator rendering', () => {
  let fixture: ComponentFixture<ParamRowComponent>;
  let store: jasmine.SpyObj<ActionFlowStore>;

  const definition: EventDefinition = {
    id: 'macro-deck::variable-changed',
    providerId: 'macro-deck',
    providerName: 'Macro Deck',
    isIntegration: false,
    name: 'Variable Changed',
    deliveryKind: 'push',
    configurationParameters: [{ name: 'value', type: 'string', label: 'Changed to' }],
    payloadParameters: [{ name: 'value', type: 'string', label: 'Changed to' }],
  };

  function render(param: ActionBlockParameter): HTMLElement {
    fixture.componentInstance.blockId = 'trigger-1';
    fixture.componentInstance.eventId = definition.id;
    fixture.componentInstance.eventDefinition = definition;
    fixture.componentInstance.param = param;
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    store = jasmine.createSpyObj<ActionFlowStore>(
      'ActionFlowStore',
      ['updateParam', 'updateEventParam', 'updateEventParamOperator', 'errorsFor', 'pickerVariables', 'cacheParamLabel'],
    );
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService() },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: {} },
      ],
    });

    fixture = TestBed.createComponent(ParamRowComponent);
  });

  it('renders the selector on the label line of a numeric filter', () => {
    const host = render({ name: 'value', type: 'number', label: 'Changed to', value: 50 });

    const button = host.querySelector<HTMLButtonElement>('.param-label-line .param-operator-btn');
    expect(button).not.toBeNull();
    expect(button!.textContent?.trim()).toBe('is');
  });

  it('still renders a selector for a boolean-narrowed choice filter - it can ask about availability even without ordering', () => {
    const host = render({
      name: 'value', type: 'choice', label: 'Changed to', value: 'true',
      options: [{ label: 'true', value: 'true' }, { label: 'false', value: 'false' }],
    });

    expect(host.querySelector('.param-operator-btn')).not.toBeNull();
  });

  it('offers only equality and the state operators for a boolean-narrowed choice filter - no ordering', () => {
    const host = render({
      name: 'value', type: 'choice', label: 'Changed to', value: 'true',
      options: [{ label: 'true', value: 'true' }, { label: 'false', value: 'false' }],
    });
    host.querySelector<HTMLButtonElement>('.param-operator-btn')!.click();
    fixture.detectChanges();

    const options = Array.from(host.querySelectorAll<HTMLButtonElement>('.param-operator-option'));
    expect(options.map(o => o.textContent?.trim()))
      .toEqual(['is', 'is not', 'is empty', 'is not empty', 'is available', 'is not available']);
  });

  it('offers exactly the ten comparison operators for a number filter and writes the picked one', () => {
    const host = render({ name: 'value', type: 'number', label: 'Changed to', value: 50, operator: '>' });
    host.querySelector<HTMLButtonElement>('.param-operator-btn')!.click();
    fixture.detectChanges();

    const options = Array.from(host.querySelectorAll<HTMLButtonElement>('.param-operator-option'));
    expect(options.map(o => o.textContent?.trim())).toEqual([
      'is', 'is not', 'greater than', 'less than', 'greater than or equal to', 'less than or equal to',
      'is empty', 'is not empty', 'is available', 'is not available',
    ]);

    options[3].click();

    expect(store.updateEventParamOperator).toHaveBeenCalledWith('trigger-1', 'value', '<');
  });
});

describe('ParamRowComponent options reload (issue #806)', () => {
  let fixture: ComponentFixture<ParamRowComponent>;
  let api: jasmine.SpyObj<ApiService>;

  function block(): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'app.test.pick-scene',
      label: 'Pick Scene',
      color: '',
      integrationId: 'app.test',
      actionId: 'pick-scene',
      // A cached valueLabel means ngOnInit does not preemptively fetch (issue #142's own guard),
      // so only the interactions under test (opening the dropdown, clicking reload) trigger a request.
      parameters: [{
        name: 'scene', type: 'dynamic-choice', label: 'Scene', value: 'a', valueLabel: 'Scene A', dynamicOptions: true,
      }],
    };
  }

  function optionsResponse(entries: { value: string; label: string }[]): GetActionParameterOptionsResponse {
    return { options: entries, allowsCustomValue: false, cacheSeconds: 300 };
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function optionLabels(): string[] {
    return Array.from(host().querySelectorAll('.sel-option') as NodeListOf<HTMLElement>)
      .map(el => el.textContent?.trim() ?? '');
  }

  function macrotask(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, 0));
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await macrotask();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function openDropdown(): Promise<void> {
    host().querySelector<HTMLButtonElement>('.control')!.click();
    await settle();
  }

  async function clickReload(): Promise<void> {
    host().querySelector<HTMLElement>('.icon-refresh')!.closest('button')!.click();
    await settle();
  }

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getActionParameterOptions', 'onNotification']);
    api.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });

    const store = jasmine.createSpyObj<ActionFlowStore>('ActionFlowStore', ['updateParam', 'errorsFor', 'pickerVariables', 'cacheParamLabel']);
    store.errorsFor.and.returnValue([]);
    store.pickerVariables.and.returnValue([]);

    TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ActionFlowStore, useValue: store },
        ActionOptionsService,
      ],
    });

    fixture = TestBed.createComponent(ParamRowComponent);
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('block', block());
    fixture.componentRef.setInput('param', block().parameters![0]);
  });

  it('re-fetches and re-renders on reload, without a cache-honouring implementation short-circuiting it', async () => {
    api.getActionParameterOptions.and.resolveTo(optionsResponse([{ value: 'a', label: 'Scene A' }]));
    fixture.detectChanges();
    await openDropdown();
    expect(optionLabels()).toEqual(['Scene A']);

    api.getActionParameterOptions.and.resolveTo(optionsResponse([
      { value: 'a', label: 'Scene A' }, { value: 'b', label: 'Scene B' },
    ]));
    const instanceBefore = fixture.componentInstance;
    await clickReload();

    expect(optionLabels()).toEqual(['Scene A', 'Scene B']);
    expect((fixture.componentInstance.param as ActionBlockParameter).value as string).toBe('a');
    expect(fixture.componentInstance).toBe(instanceBefore);
  });

  it('a second reload still re-fetches, ruling out "works once because a flag flipped"', async () => {
    api.getActionParameterOptions.and.resolveTo(optionsResponse([{ value: 'a', label: 'Scene A' }]));
    fixture.detectChanges();
    await openDropdown();

    api.getActionParameterOptions.and.resolveTo(optionsResponse([
      { value: 'a', label: 'Scene A' }, { value: 'b', label: 'Scene B' },
    ]));
    await clickReload();

    api.getActionParameterOptions.and.resolveTo(optionsResponse([
      { value: 'a', label: 'Scene A' }, { value: 'b', label: 'Scene B' }, { value: 'c', label: 'Scene C' },
    ]));
    await clickReload();

    expect(optionLabels()).toEqual(['Scene A', 'Scene B', 'Scene C']);
  });

  it('still honours the cache on an ordinary re-open - the counterexample for "fix reload by deleting the cache"', async () => {
    api.getActionParameterOptions.and.resolveTo(optionsResponse([{ value: 'a', label: 'Scene A' }]));
    fixture.detectChanges();
    await openDropdown();
    expect(api.getActionParameterOptions).toHaveBeenCalledTimes(1);

    await openDropdown();

    expect(api.getActionParameterOptions).toHaveBeenCalledTimes(1);
  });
});
