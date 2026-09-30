import { CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import type {
  DiscoverCatalogVariablesRequest,
  Variable,
  VariableCatalogNode,
  VariableCatalogProvider,
  VariableClassification,
} from '@macro-deck/runtime';
import { VariableCatalogService } from '../../services/variable-catalog.service';
import { VariableCatalogIdInputComponent } from './variable-catalog-id-input.component';
import { VariablesManagerComponent } from './variables-manager.component';

function variable(
  id: string,
  classification: VariableClassification,
  ownerIntegrationId?: string,
): Variable {
  return {
    id,
    name: `var_${id}`,
    scope: 'global',
    type: 'text',
    classification,
    ownerIntegrationId,
    value: '',
  };
}

function dynamicVariable(id: string, ownerIntegrationId: string): Variable {
  return {
    ...variable(id, 'integration', ownerIntegrationId),
    dynamicResourceId: `resource-${id}`,
  };
}

function buttonVariable(id: string, scopeRefId: string): Variable {
  return {
    ...variable(id, 'user'),
    scope: 'widget',
    scopeRefId,
  };
}

function flatVariables(count: number): Variable[] {
  return Array.from({ length: count }, (_, i) => variable(String(i), 'user'));
}

function mixedSourceVariables(count: number): Variable[] {
  const list: Variable[] = [];
  for (let i = 0; i < 10; i++) list.push(variable(`u${i}`, 'user'));
  const integrations = ['int-a', 'int-b', 'int-c'];
  for (let i = 0; i < count; i++) {
    list.push(variable(`i${i}`, 'integration', integrations[i % integrations.length]));
  }
  return list;
}

describe('VariablesManagerComponent', () => {
  let fixture: ComponentFixture<VariablesManagerComponent>;
  let component: VariablesManagerComponent;
  let createVariableSpy: jasmine.Spy;
  let unbindCatalogVariableSpy: jasmine.Spy;
  let setVariableValueSpy: jasmine.Spy;
  let updateVariableSpy: jasmine.Spy;
  let setVariableSharedSpy: jasmine.Spy;

  const variables: Variable[] = [
    variable('1', 'user'),
    variable('2', 'integration', 'app.macro-deck.system'),
    variable('3', 'integration', 'spotify'),
    variable('4', 'integration', 'obs'),
  ];

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'createVariable',
      'updateVariable',
      'setVariableValue',
      'setVariableShared',
      'unbindCatalogVariable',
      'getVariableCatalogProviders',
      'discoverCatalogVariables',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.createVariable.and.resolveTo({ success: true, variable: variable('new', 'user') });
    apiSpy.setVariableValue.and.resolveTo({ success: true, variable: variable('1', 'user') });
    apiSpy.unbindCatalogVariable.and.resolveTo({ success: true });
    apiSpy.getVariableCatalogProviders.and.resolveTo({
      providers: [{ integrationId: 'obs', name: 'OBS Studio', supportsSearch: false, supportsManualIds: false }],
    });
    apiSpy.discoverCatalogVariables.and.resolveTo({
      nodes: [
        { id: 'input/mic/volume', name: 'volume', displayName: null, hasChildren: false, type: 'numeric', canWrite: true },
        { id: 'input/mic/muted', name: 'muted', displayName: null, hasChildren: false, type: 'boolean', boundVariableId: '4' },
      ],
      hasMore: false,
      available: true,
    } as never);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    createVariableSpy = apiSpy.createVariable;
    unbindCatalogVariableSpy = apiSpy.unbindCatalogVariable;
    setVariableValueSpy = apiSpy.setVariableValue;
    updateVariableSpy = apiSpy.updateVariable;
    setVariableSharedSpy = apiSpy.setVariableShared;

    TestBed.configureTestingModule({
      imports: [VariablesManagerComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(VariablesManagerComponent);
    component = fixture.componentInstance;
    // The component's own :host carries no display/size rule of its own - both real host pages
    // (the Variables page, the variable browser modal) supply `display: flex` and a definite height
    // externally, so the bare unit test has to stand in for that for the virtualized list to render
    // any rows at all.
    fixture.nativeElement.style.display = 'flex';
    fixture.nativeElement.style.flexDirection = 'column';
    fixture.nativeElement.style.width = '640px';
    fixture.nativeElement.style.height = '400px';
    fixture.detectChanges();
    await fixture.whenStable();
    await flushViewport();
  });

  async function flushViewport(): Promise<void> {
    const viewportDebug = fixture.debugElement.query(By.directive(CdkVirtualScrollViewport));
    if (!viewportDebug) return;
    const viewport = viewportDebug.componentInstance as CdkVirtualScrollViewport;
    viewport.checkViewportSize();
    await new Promise(resolve => setTimeout(resolve, 20));
    fixture.detectChanges();
  }

  it('shows every global variable without filters', () => {
    expect(component.listed().length).toBe(4);
    expect(component.canCreate()).toBeTrue();
    expect(component.hasSourceFilter()).toBeFalse();
  });

  it('groups the all view by source with locals first and integrations alphabetical', () => {
    fixture.componentRef.setInput('variables', [
      ...variables,
      buttonVariable('b1', 'w1'),
    ]);
    fixture.componentRef.setInput('scopeLabel', 'This button');

    expect(component.showGroupHeaders()).toBeTrue();
    expect(component.groups().map(g => g.key)).toEqual([
      'scope',
      'user',
      'integration:app.macro-deck.system',
      'integration:obs',
      'integration:spotify',
    ]);
    expect(component.groups()[0].label).toBe('This button');
  });

  it('filters by owning integration without group headers', () => {
    fixture.componentRef.setInput('source', { kind: 'integration', integrationId: 'spotify' });

    expect(component.listed().map(v => v.id)).toEqual(['3']);
    expect(component.showGroupHeaders()).toBeFalse();
    expect(component.canCreate()).toBeFalse();
    expect(component.hasSourceFilter()).toBeTrue();
  });

  it('filters to user-created globals and keeps creation available', () => {
    fixture.componentRef.setInput('source', { kind: 'user' });

    expect(component.listed().map(v => v.id)).toEqual(['1']);
    expect(component.canCreate()).toBeTrue();
  });

  it('filters to scope-locals with the scope source', () => {
    fixture.componentRef.setInput('variables', [...variables, buttonVariable('b1', 'w1')]);
    fixture.componentRef.setInput('source', { kind: 'scope' });

    expect(component.listed().map(v => v.id)).toEqual(['b1']);
  });

  it('only allows creating in the scope source when a scopeRefId is present', () => {
    fixture.componentRef.setInput('source', { kind: 'scope' });
    expect(component.canCreate()).toBeFalse();

    fixture.componentRef.setInput('scopeRefId', 'w1');
    expect(component.canCreate()).toBeTrue();
  });

  it('creates a scope-local variable while the scope source is selected', async () => {
    fixture.componentRef.setInput('source', { kind: 'scope' });
    fixture.componentRef.setInput('scopeRefId', 'w1');
    component.openCreate();
    await component.onNameInput('my_var');

    await component.submitCreate();

    expect(createVariableSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      name: 'my_var',
      scope: 'widget',
      scopeRefId: 'w1',
    }));
  });

  it('defaults to the widget whenever it was opened from one', async () => {
    // The browser opens on the "all" source, so keying this off the selected source would default
    // to global for everyone arriving from a widget editor - which is the whole reason to be here.
    fixture.componentRef.setInput('scopeRefId', 'w1');
    component.openCreate();
    await component.onNameInput('my_var');

    await component.submitCreate();

    expect(createVariableSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      name: 'my_var',
      scope: 'widget',
      scopeRefId: 'w1',
    }));
  });

  it('defaults to global with no widget to scope to', async () => {
    component.openCreate();
    await component.onNameInput('my_var');

    await component.submitCreate();

    expect(createVariableSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      name: 'my_var',
      scope: 'global',
      scopeRefId: undefined,
    }));
  });

  it('creates in the scope the form chose, not the one the source implied', async () => {
    // The source only seeds the default now. Both directions matter: picking global while browsing
    // the widget's own variables has to produce a global, and picking the widget from anywhere else
    // has to produce a widget-scoped one.
    fixture.componentRef.setInput('source', { kind: 'scope' });
    fixture.componentRef.setInput('scopeRefId', 'w1');
    component.openCreate();
    await component.onNameInput('my_var');
    component.setFormScope('global');

    await component.submitCreate();

    expect(createVariableSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      scope: 'global',
      scopeRefId: undefined,
    }));

    createVariableSpy.calls.reset();
    fixture.componentRef.setInput('source', { kind: 'all' });
    component.openCreate();
    await component.onNameInput('my_var');
    component.setFormScope('widget');

    await component.submitCreate();

    expect(createVariableSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      scope: 'widget',
      scopeRefId: 'w1',
    }));
  });

  it('offers the scope choice only where there is a widget to scope to', () => {
    expect(component.canChooseScope()).toBeFalse();

    fixture.componentRef.setInput('scopeRefId', 'w1');
    expect(component.canChooseScope()).toBeTrue();
  });

  it('clears the source back to all when set to null', () => {
    fixture.componentRef.setInput('source', { kind: 'integration', integrationId: 'spotify' });
    fixture.componentRef.setInput('source', null);

    expect(component.listed().length).toBe(4);
    expect(component.hasSourceFilter()).toBeFalse();
  });

  it('narrows the list with the search box', () => {
    component.search.set('var_3');

    expect(component.listed().map(v => v.id)).toEqual(['3']);
    expect(component.hasSourceFilter()).toBeTrue();
  });

  it('matches the public vars. prefix in searches', () => {
    component.search.set('vars.var_1');

    expect(component.listed().map(v => v.id)).toEqual(['1']);
  });

  it('prefers the variables override over the service cache', () => {
    fixture.componentRef.setInput('variables', [variable('override', 'user')]);

    expect(component.listed().map(v => v.id)).toEqual(['override']);
  });

  it('shows the header create button by default', () => {
    expect(fixture.nativeElement.querySelector('.vars-header shared-button')).toBeTruthy();
  });

  it('hides the header create button when the hosting page renders its own', async () => {
    fixture.componentRef.setInput('showCreateButton', false);
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.vars-header shared-button')).toBeNull();
  });

  it('exposes the full name and value on hover when the row truncates them', async () => {
    const long: Variable = {
      ...variable('long', 'integration', 'spotify'),
      name: 'spotify_currently_playing_track_album_artist_name',
      value: 'A value long enough that the row has to truncate it as well',
    };
    fixture.componentRef.setInput('variables', [long]);
    await fixture.whenStable();
    await flushViewport();

    const name = fixture.nativeElement.querySelector('.vars-row .vr-primary') as HTMLElement;
    const value = fixture.nativeElement.querySelector('.vars-row .vr-value') as HTMLElement;

    expect(name.title).toBe('vars.spotify_currently_playing_track_album_artist_name');
    expect(value.title).toBe('A value long enough that the row has to truncate it as well');
  });

  describe('what gates each affordance', () => {
    // Writability and ownership answer two different questions. A provider variable whose owner
    // accepts writes is editable but never the user's to delete; a user variable is always both.
    const writableIntegration: Variable = {
      ...variable('w1', 'integration', 'obs'),
      type: 'numeric',
      canWrite: true,
    };
    const readOnlyIntegration = variable('r1', 'integration', 'spotify');

    it('offers value editing for a writable integration variable and none for a read-only one', () => {
      expect(component.canEditValue(writableIntegration)).toBeTrue();
      expect(component.canEditValue(readOnlyIntegration)).toBeFalse();
      expect(component.canEditValue(variable('5', 'widget'))).toBeFalse();
    });

    it('treats a user variable as writable even though the host never sends canWrite for pending rows', () => {
      expect(component.canEditValue({ ...variable('1', 'user'), canWrite: true })).toBeTrue();
    });

    it('never offers deletion for an integration variable, writable or not', () => {
      expect(component.canManage(writableIntegration)).toBeFalse();
      expect(component.canManage(readOnlyIntegration)).toBeFalse();
      expect(component.canManage(variable('1', 'user'))).toBeTrue();
    });

    it('shows the lock only where the value cannot be written', async () => {
      fixture.componentRef.setInput('variables', [writableIntegration, readOnlyIntegration]);
      await fixture.whenStable();
      await flushViewport();

      const rows = Array.from(fixture.nativeElement.querySelectorAll('.vars-row')) as HTMLElement[];
      const writableRow = rows.find(row => row.textContent?.includes('var_w1'));
      const readOnlyRow = rows.find(row => row.textContent?.includes('var_r1'));

      expect(writableRow?.querySelector('.read-only-icon')).toBeNull();
      expect(readOnlyRow?.querySelector('.read-only-icon')).not.toBeNull();
    });

    it('refuses a delete request for an integration variable however writable it is', () => {
      component.requestDelete(writableIntegration);

      expect(component.deleteCandidate()).toBeNull();
    });
  });

  describe('writing a value', () => {
    const writable: Variable = {
      ...variable('w1', 'integration', 'obs'),
      type: 'numeric',
      canWrite: true,
      value: '20',
    };

    it('surfaces a refused write rather than swallowing it', async () => {
      setVariableValueSpy.and.resolveTo({ success: false, error: { code: 'NotWritable', message: 'no' } });
      fixture.componentRef.setInput('variables', [writable]);
      await fixture.whenStable();

      component.startEdit(writable);
      component.editingValue.set('50');
      await component.commitEdit(writable);
      await fixture.whenStable();

      expect(component.writeFailed()).toBeTrue();
      expect(fixture.nativeElement.querySelector('shared-error-banner')).not.toBeNull();
    });

    it('leaves the listed value alone while an owner-dispatched write is still pending', async () => {
      setVariableValueSpy.and.resolveTo({ success: true, pending: true, variable: { ...writable, value: '20' } });
      fixture.componentRef.setInput('variables', null);
      await fixture.whenStable();

      component.startEdit(writable);
      component.editingValue.set('50');
      await component.commitEdit(writable);
      await fixture.whenStable();

      expect(component.writeFailed()).toBeFalse();
      expect(component.listed().find(v => v.id === 'w1')).toBeUndefined();
    });
  });

  it('appends the owner-declared unit to the rendered value', () => {
    expect(component.formatValue({ ...variable('u', 'integration', 'obs'), value: '42', unit: '%' }))
      .toBe('42 %');
    expect(component.formatValue({ ...variable('u', 'integration', 'obs'), value: '42' })).toBe('42');
  });

  it('prefers the owner-reported step over the one derived from decimal places', () => {
    expect(component.numericStep({ ...variable('n', 'integration', 'obs'), type: 'numeric', step: 0.1 }))
      .toBe(0.1);
    expect(component.numericStep({ ...variable('n', 'user'), type: 'numeric', decimalPlaces: 2 }))
      .toBeCloseTo(0.01, 10);
    expect(component.numericStep({ ...variable('n', 'user'), type: 'numeric' })).toBe(1);
  });

  describe('unbinding a catalog variable', () => {
    beforeEach(async () => {
      fixture.componentRef.setInput('variables', [
        ...variables,
        dynamicVariable('d1', 'home-assistant'),
        variable('imported', 'integration', 'app.macro-deck.delegate'),
      ]);
      await fixture.whenStable();
      await flushViewport();
    });

    it('offers Unbind only on a row bound through the variable catalog browser', () => {
      expect(component.isBoundDynamic(dynamicVariable('d1', 'home-assistant'))).toBeTrue();
      expect(component.isBoundDynamic(variable('3', 'integration', 'spotify'))).toBeFalse();
    });

    it('renders an active menu (not the disabled read-only button) for the catalog-bound row', () => {
      const rows = Array.from(fixture.nativeElement.querySelectorAll('.vars-row')) as HTMLElement[];
      const dynamicRow = rows.find(row => row.textContent?.includes('var_d1'));
      const nothingToOfferRow = rows.find(row => row.textContent?.includes('var_imported'));

      expect(dynamicRow?.querySelector('.vars-dots-btn:disabled')).toBeNull();
      expect(nothingToOfferRow?.querySelector('.vars-dots-btn:disabled')).not.toBeNull();
    });

    it('asks for confirmation before unbinding, and only unbinds on confirm', async () => {
      component.requestUnbind(dynamicVariable('d1', 'home-assistant'));
      expect(component.unbindCandidate()).not.toBeNull();
      expect(unbindCatalogVariableSpy).not.toHaveBeenCalled();

      await component.confirmUnbind();

      expect(unbindCatalogVariableSpy).toHaveBeenCalledWith({ variableId: 'd1' });
      expect(component.unbindCandidate()).toBeNull();
    });

    it('does nothing when the confirmation is cancelled', () => {
      component.requestUnbind(dynamicVariable('d1', 'home-assistant'));
      component.cancelUnbind();

      expect(component.unbindCandidate()).toBeNull();
      expect(unbindCatalogVariableSpy).not.toHaveBeenCalled();
    });

    it('never offers Unbind on an ordinary integration row', () => {
      component.requestUnbind(variable('3', 'integration', 'spotify'));
      expect(component.unbindCandidate()).toBeNull();
    });

    it('surfaces (rather than swallows) a failed unbind, keeping the confirmation open', async () => {
      unbindCatalogVariableSpy.and.rejectWith(new Error('network error'));
      component.requestUnbind(dynamicVariable('d1', 'home-assistant'));

      await component.confirmUnbind();

      expect(component.unbindFailed()).toBeTrue();
      expect(component.unbindCandidate()).not.toBeNull();
    });

    it('groups a bound catalog variable under its owning integration in the all-sources view', async () => {
      fixture.componentRef.setInput('source', { kind: 'all' });
      await fixture.whenStable();
      await flushViewport();

      expect(component.groups().find(g => g.key === 'integration:home-assistant')?.variables.map(v => v.id))
        .toEqual(['d1']);
      expect(component.groups().find(g => g.key === 'dynamic')).toBeUndefined();
    });
  });

  describe('sharing a variable', () => {
    const shared: Variable = { ...variable('s1', 'integration', 'obs'), shared: true };

    it('offers sharing for global variables but not for widget or imported ones', () => {
      expect(component.canShare(variable('1', 'user'))).toBeTrue();
      expect(component.canShare(variable('3', 'integration', 'spotify'))).toBeTrue();
      expect(component.canShare(buttonVariable('b1', 'widget-1'))).toBeFalse();
      expect(component.canShare(variable('i1', 'integration', 'app.macro-deck.delegate'))).toBeFalse();
    });

    it('turns sharing on for an unshared variable and off for a shared one', async () => {
      setVariableSharedSpy.and.resolveTo({ success: true, variable: shared });

      await component.toggleShared(variable('1', 'user'));
      await component.toggleShared(shared);

      expect(setVariableSharedSpy.calls.argsFor(0)).toEqual([{ id: '1', shared: true }]);
      expect(setVariableSharedSpy.calls.argsFor(1)).toEqual([{ id: 's1', shared: false }]);
      expect(component.shareFailed()).toBeFalse();
    });

    it('surfaces a refused share rather than swallowing it', async () => {
      setVariableSharedSpy.and.resolveTo({ success: false, error: { code: 'InternalError', message: 'no' } });
      fixture.componentRef.setInput('variables', [variable('1', 'user')]);
      await fixture.whenStable();

      await component.toggleShared(variable('1', 'user'));
      await fixture.whenStable();

      expect(component.shareFailed()).toBeTrue();
      expect(fixture.nativeElement.querySelector('shared-error-banner')).not.toBeNull();
    });

    it('marks a shared variable in its row', async () => {
      fixture.componentRef.setInput('variables', [shared, variable('1', 'user')]);
      await fixture.whenStable();
      await flushViewport();

      const rows = Array.from(fixture.nativeElement.querySelectorAll('.vars-row')) as HTMLElement[];

      expect(rows.find(row => row.textContent?.includes('var_s1'))?.querySelector('.vars-shared-badge'))
        .not.toBeNull();
      expect(rows.find(row => row.textContent?.includes('var_1'))?.querySelector('.vars-shared-badge'))
        .toBeNull();
    });
  });

  describe('pick mode', () => {
    beforeEach(async () => {
      fixture.componentRef.setInput('mode', 'pick');
      await fixture.whenStable();
      await flushViewport();
    });

    it('hides creation and row actions', () => {
      expect(component.canCreate()).toBeFalse();
      expect(fixture.nativeElement.querySelector('.vars-actions')).toBeNull();
    });

    it('emits the picked variable when a row is clicked', () => {
      const picked: Variable[] = [];
      component.pick.subscribe(v => picked.push(v));

      const row = fixture.nativeElement.querySelector('.vars-row-pick button') as HTMLButtonElement;
      row.click();

      expect(picked.map(v => v.id)).toEqual(['1']);
    });
  });

  describe('virtualized large lists', () => {
    it('renders far fewer than 5000 rows, and the subtitle/listed() still report the true total', async () => {
      fixture.componentRef.setInput('variables', flatVariables(5000));
      fixture.componentRef.setInput('source', { kind: 'user' });
      await fixture.whenStable();
      await flushViewport();

      expect(component.listed().length).toBe(5000);
      const rendered = fixture.nativeElement.querySelectorAll('.vars-row').length;
      expect(rendered).toBeGreaterThan(0);
      expect(rendered).toBeLessThan(200);
      expect(fixture.nativeElement.querySelector('.vars-subtitle')?.textContent?.trim())
        .toBe('5000 variables');

      component.search.set('var_4999');
      await fixture.whenStable();
      await flushViewport();

      expect(component.listed().length).toBe(1);
      const names = Array.from(fixture.nativeElement.querySelectorAll('.vars-row .vr-primary'))
        .map(el => (el as HTMLElement).textContent?.trim());
      expect(names).toContain('vars.var_4999');
    });

    it('groups a mixed 5000-variable source list and still keeps the DOM small', async () => {
      fixture.componentRef.setInput('variables', mixedSourceVariables(4990));
      fixture.componentRef.setInput('source', { kind: 'all' });
      await fixture.whenStable();
      await flushViewport();

      expect(component.listed().length).toBe(5000);
      expect(component.showGroupHeaders()).toBeTrue();
      expect(component.groups().map(g => g.key)).toEqual([
        'user',
        'integration:int-a',
        'integration:int-b',
        'integration:int-c',
      ]);
      expect(fixture.nativeElement.querySelector('shared-variable-group-header')).not.toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.vars-row').length).toBeLessThan(200);
    });

    // Guards against a "virtualization" that just slices to the first N rendered and reports N as
    // the total - which is why every assertion in this file goes through listed()/the subtitle
    // rather than counting DOM nodes as a proxy for the data.
    it('binds a pick row to its variable rather than its position after scrolling', async () => {
      fixture.componentRef.setInput('variables', flatVariables(5000));
      fixture.componentRef.setInput('source', { kind: 'user' });
      fixture.componentRef.setInput('mode', 'pick');
      await fixture.whenStable();
      await flushViewport();

      const viewport = fixture.debugElement
        .query(By.directive(CdkVirtualScrollViewport))
        .componentInstance as CdkVirtualScrollViewport;
      viewport.scrollToIndex(2500);
      await flushViewport();

      const picked: Variable[] = [];
      component.pick.subscribe(v => picked.push(v));
      const row = fixture.nativeElement.querySelector('.vars-row-pick button') as HTMLButtonElement;
      const renderedName = row.querySelector('.vr-primary')?.textContent?.trim();
      row.click();

      expect(renderedName).toBeTruthy();
      expect(picked.length).toBe(1);
      expect(component.publicName(picked[0])).toBe(renderedName!);
    });

    it('shows the empty state and zero rows for an empty list', async () => {
      // A source with no catalog behind it, so "nothing to show" really is nothing: under a source
      // that offers one, an empty variable list is not an empty list - the catalog is still there.
      component.source = { kind: 'user' };
      fixture.componentRef.setInput('variables', []);
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('shared-empty-state')).not.toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.vars-row').length).toBe(0);
      expect(fixture.nativeElement.querySelector('.vars-subtitle')?.textContent?.trim())
        .toBe('0 variables');
    });
  });

  interface CatalogRowView {
    kind: string;
    depth?: number;
    note?: string;
    count?: number | null;
    expanded?: boolean;
    integrationId?: string;
    variable?: Variable;
    node?: VariableCatalogNode;
  }

  function catalogRows(): CatalogRowView[] {
    return component.rows() as unknown as CatalogRowView[];
  }

  function api(): jasmine.SpyObj<ApiService> {
    return TestBed.inject(ApiService) as unknown as jasmine.SpyObj<ApiService>;
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 8; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve, 0));
    }
    fixture.detectChanges();
  }

  async function useProvider(provider: Partial<VariableCatalogProvider> & { integrationId: string }): Promise<void> {
    api().getVariableCatalogProviders.and.resolveTo({
      providers: [{ name: provider.integrationId, supportsSearch: false, supportsManualIds: false, ...provider }],
    } as never);
    await TestBed.inject(VariableCatalogService).loadProviders();
  }

  async function openUnbound(integrationId: string): Promise<void> {
    component.toggleUnbound(integrationId);
    await settle();
  }

  function unboundHeader(integrationId: string): CatalogRowView | undefined {
    return catalogRows().find(r => r.kind === 'unbound-header' && r.integrationId === integrationId);
  }

  async function afterSearchDebounce(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve, 600));
  }

  function discoverCalls(): DiscoverCatalogVariablesRequest[] {
    return api().discoverCatalogVariables.calls.allArgs().map(args => args[0] as DiscoverCatalogVariablesRequest);
  }

  describe('an integration catalog', () => {
    beforeEach(async () => {
      component.source = { kind: 'integration', integrationId: 'obs' };
      await settle();
    });

    it('keeps its unbound entries out of the list until their group is opened', async () => {
      expect(unboundHeader('obs')?.expanded).toBeFalse();
      expect(catalogRows().some(r => r.kind === 'catalog-leaf')).toBeFalse();

      await openUnbound('obs');

      const leaf = catalogRows().find(r => r.kind === 'catalog-leaf');
      expect(leaf?.node?.name).toBe('volume');
    });

    it('leaves the bound variables in view under one collapsed header', () => {
      const kinds = catalogRows().map(r => r.kind);

      expect(kinds).toEqual(['unbound-header', 'row']);
      expect(fixture.nativeElement.querySelector('.vars-unbound-header button')?.getAttribute('aria-expanded'))
        .toBe('false');
    });

    it('opens and closes the group from its header', async () => {
      const toggle = fixture.nativeElement.querySelector('.vars-unbound-header button') as HTMLButtonElement;

      toggle.click();
      await settle();
      expect(fixture.nativeElement.querySelector('.vars-catalog-leaf')).not.toBeNull();

      (fixture.nativeElement.querySelector('.vars-unbound-header button') as HTMLButtonElement).click();
      await settle();
      expect(fixture.nativeElement.querySelector('.vars-catalog-leaf')).toBeNull();
    });

    it('counts a fully loaded catalog in the header and leaves the subtitle to the variables', () => {
      expect(unboundHeader('obs')?.count).toBe(1);
      expect(fixture.nativeElement.querySelector('.vars-subtitle')?.textContent?.trim()).toBe('1 variable');
    });

    it('shows no count while a type or writable filter narrows what is listed', async () => {
      component.mode = 'pick';
      component.acceptedTypes = ['numeric'];
      await settle();

      expect(unboundHeader('obs')).toBeTruthy();
      expect(unboundHeader('obs')?.count).toBeNull();
    });

    it('does not list an entry that is already bound, which the list shows as a real variable', async () => {
      await openUnbound('obs');

      const names = catalogRows().filter(r => r.kind === 'catalog-leaf').map(r => r.node!.name);

      expect(names).not.toContain('muted');
    });

    it('asks for a bind when an unbound entry is activated', async () => {
      await openUnbound('obs');
      const requested = spyOn(component.catalogBindRequested, 'emit');

      (fixture.nativeElement.querySelector('.vars-catalog-leaf button') as HTMLButtonElement).click();

      expect(requested).toHaveBeenCalledWith(jasmine.objectContaining({ integrationId: 'obs' }));
    });

    it('starts opened where the host asks for it', async () => {
      fixture.componentRef.setInput('unboundExpanded', true);
      await settle();

      expect(catalogRows().some(r => r.kind === 'catalog-leaf')).toBeTrue();
    });

    it('drops the group when everything the catalog offers is bound', async () => {
      api().discoverCatalogVariables.and.resolveTo({
        nodes: [{ id: 'input/mic/muted', name: 'muted', displayName: null, hasChildren: false, type: 'boolean', boundVariableId: '4' }],
        hasMore: false,
        available: true,
      } as never);
      TestBed.inject(VariableCatalogService).invalidateIntegration('obs');
      await settle();

      expect(unboundHeader('obs')).toBeUndefined();
    });

    it('brings an entry back once it is unbound, even if the provider reported none left', async () => {
      await useProvider({ integrationId: 'obs', unboundCount: 0 });
      api().discoverCatalogVariables.and.resolveTo({ nodes: [], hasMore: false, available: true } as never);
      TestBed.inject(VariableCatalogService).invalidateIntegration('obs');
      await settle();
      expect(catalogRows().some(r => r.kind === 'catalog-leaf')).toBeFalse();

      api().discoverCatalogVariables.and.resolveTo({
        nodes: [{ id: 'input/mic/muted', name: 'muted', displayName: null, hasChildren: false, type: 'boolean' }],
        hasMore: false,
        available: true,
      } as never);
      TestBed.inject(VariableCatalogService).invalidateIntegration('obs');
      await settle();

      expect(unboundHeader('obs')?.count).withContext('what is listed wins over a stale report').toBe(1);
      await openUnbound('obs');
      expect(catalogRows().filter(r => r.kind === 'catalog-leaf').map(r => r.node!.name)).toEqual(['muted']);
    });

    it('says so when the integration cannot be reached', async () => {
      api().discoverCatalogVariables.and.resolveTo({ nodes: [], hasMore: false, available: false } as never);
      TestBed.inject(VariableCatalogService).invalidateIntegration('obs');
      await settle();
      await openUnbound('obs');
      await flushViewport();

      const state = fixture.nativeElement.querySelector('.vars-state-row') as HTMLElement;
      expect(state.textContent).toContain('obs');
      expect(state.querySelector('shared-button')).not.toBeNull();
    });

    describe('while searching', () => {
      it('lists matching variables before matching unbound entries', async () => {
        component.search.set('v');
        await afterSearchDebounce();
        await settle();

        expect(catalogRows().map(r => r.kind)).toEqual(['row', 'unbound-header', 'catalog-leaf']);
      });

      it('shows the group only when it has a match', async () => {
        component.search.set('var_4');
        await afterSearchDebounce();
        await settle();

        expect(catalogRows().map(r => r.kind)).toEqual(['row']);
      });

      it('falls back to the empty state when nothing matches anywhere', async () => {
        component.search.set('nothing-matches-this');
        await afterSearchDebounce();
        await settle();

        expect(component.rows().length).toBe(0);
        expect(fixture.nativeElement.querySelector('shared-empty-state')).not.toBeNull();
      });
    });
  });

  describe('a searchable catalog of containers', () => {
    const entityCount = 500;

    function entity(i: number, extra: Partial<VariableCatalogNode> = {}): VariableCatalogNode {
      return {
        id: `entity/light.e${i}`,
        name: `ha_light_e${i}`,
        suggestedName: `ha_light_e${i}`,
        displayName: null,
        hasChildren: true,
        ...extra,
      } as VariableCatalogNode;
    }

    function children(parentId: string): VariableCatalogNode[] {
      return [
        { id: `${parentId}/state`, name: 'state', suggestedName: `${parentId}_state`, displayName: null, hasChildren: false, type: 'text' },
        { id: `${parentId}/brightness`, name: 'brightness', suggestedName: `${parentId}_brightness`, displayName: null, hasChildren: false, type: 'numeric', canWrite: false },
      ] as VariableCatalogNode[];
    }

    let roots: VariableCatalogNode[];
    let searchResults: Record<string, VariableCatalogNode[]>;

    beforeEach(async () => {
      roots = Array.from({ length: entityCount }, (_, i) => entity(i));
      searchResults = {};
      api().discoverCatalogVariables.and.callFake(async (request: DiscoverCatalogVariablesRequest) => {
        if (request.parentId) {
          return { nodes: children(request.parentId), hasMore: false, available: true };
        }
        const list = request.search ? (searchResults[request.search] ?? []) : roots;
        const start = request.cursor ? Number(request.cursor) : 0;
        const nodes = list.slice(start, start + 200);
        const hasMore = start + 200 < list.length;
        return { nodes, hasMore, nextCursor: hasMore ? String(start + 200) : undefined, available: true };
      });
      await useProvider({ integrationId: 'ha', supportsSearch: true, supportsManualIds: true });
      api().discoverCatalogVariables.calls.reset();
      component.source = { kind: 'integration', integrationId: 'ha' };
      await settle();
    });

    async function open(): Promise<void> {
      await openUnbound('ha');
    }

    function expand(nodeId: string): void {
      const row = catalogRows().find(r => r.kind === 'catalog-branch' && r.node?.id === nodeId);
      expect(row).withContext(`${nodeId} is listed as a container`).toBeTruthy();
      component.toggleCatalogBranch('ha', row!.node!);
    }

    it('asks the host for nothing while the group is closed', () => {
      expect(unboundHeader('ha')).toBeTruthy();
      expect(discoverCalls().length).toBe(0);
    });

    it('never shows the empty state while a search that only the catalog can answer is on its way', async () => {
      searchResults['Kitchen'] = [entity(499)];

      component.search.set('Kitchen');
      fixture.detectChanges();
      expect(component.rows().length).withContext('before the debounce').toBeGreaterThan(0);

      await afterSearchDebounce();
      await settle();
      expect(catalogRows().filter(r => r.kind === 'catalog-branch').map(r => r.node!.id)).toEqual(['entity/light.e499']);
    });

    it('asks only for the first root page and lists the containers without opening them', async () => {
      await open();
      const calls = discoverCalls();

      expect(calls.length).toBeGreaterThan(0);
      expect(calls.every(call => call.parentId === undefined)).toBeTrue();
      expect(calls.filter(call => !call.cursor).length).toBe(1);
      expect(catalogRows().some(r => r.kind === 'catalog-branch')).toBeTrue();
      expect(catalogRows().some(r => r.kind === 'catalog-leaf')).toBeFalse();
    });

    it('fetches a container\'s children once when it is opened and lists them beneath it', async () => {
      await open();
      expand('entity/light.e0');
      await settle();

      const childCalls = discoverCalls().filter(call => call.parentId === 'entity/light.e0');
      const leaves = catalogRows().filter(r => r.kind === 'catalog-leaf');

      expect(childCalls.length).toBe(1);
      expect(childCalls[0].search).toBeUndefined();
      expect(leaves.map(r => r.node!.id)).toEqual(['entity/light.e0/state', 'entity/light.e0/brightness']);
      expect(leaves.every(r => r.depth === 1)).toBeTrue();
    });

    it('sends the search to the host without the vars. prefix and lists what the host matched', async () => {
      searchResults['Kitchen'] = [entity(499)];

      component.search.set('vars.Kitchen');
      await afterSearchDebounce();
      await settle();

      const searched = discoverCalls().filter(call => call.search !== undefined);
      const branches = catalogRows().filter(r => r.kind === 'catalog-branch');

      expect(searched.map(call => call.search)).toContain('Kitchen');
      expect(discoverCalls().some(call => call.parentId !== undefined)).toBeFalse();
      expect(branches.map(r => r.node!.id)).toEqual(['entity/light.e499']);
    });

    it('opens a search result with a plain child request, not another search', async () => {
      searchResults['e499'] = [entity(499)];
      component.search.set('e499');
      await afterSearchDebounce();
      await settle();

      expand('entity/light.e499');
      await settle();

      const childCall = discoverCalls().find(call => call.parentId === 'entity/light.e499');
      expect(childCall).toBeTruthy();
      expect(childCall!.search).toBeUndefined();
      expect(catalogRows().filter(r => r.kind === 'catalog-leaf').length).toBe(2);
    });

    it('lists both a leaf and a container a search returned from different depths', async () => {
      searchResults['mix'] = [
        { id: 'entity/light.e3/brightness', name: 'brightness', suggestedName: 'ha_light_e3_brightness', displayName: null, hasChildren: false, type: 'numeric' } as VariableCatalogNode,
        entity(4),
      ];
      component.search.set('mix');
      await afterSearchDebounce();
      await settle();

      expect(catalogRows().find(r => r.kind === 'catalog-leaf')?.node?.id).toBe('entity/light.e3/brightness');
      expect(catalogRows().find(r => r.kind === 'catalog-branch')?.node?.id).toBe('entity/light.e4');
    });

    it('still offers a container that is bindable in its own right', async () => {
      roots = [entity(0, { type: 'numeric' })];
      TestBed.inject(VariableCatalogService).invalidateIntegration('ha');
      component.source = { kind: 'all' };
      await settle();
      component.source = { kind: 'integration', integrationId: 'ha' };
      await settle();
      await open();
      await flushViewport();

      const requested = spyOn(component.catalogBindRequested, 'emit');
      const bind = fixture.debugElement.query(By.css('.vars-catalog-bind'));
      expect(bind).withContext('the container row carries a bind action').not.toBeNull();
      bind.triggerEventHandler('click', new MouseEvent('click'));

      expect(requested).toHaveBeenCalledWith(jasmine.objectContaining({ integrationId: 'ha' }));
    });

    it('says an opened container has nothing usable when the filter removes all of its children', async () => {
      component.writableOnly = true;
      await open();
      expand('entity/light.e0');
      await settle();

      const notes = catalogRows().filter(r => r.kind === 'catalog-note');
      expect(notes.length).toBe(1);
      expect(notes[0].note).toBe('not-bindable');
    });

    it('lists the children of the last container within the budget', async () => {
      await open();
      const branches = catalogRows().filter(r => r.kind === 'catalog-branch');
      const last = branches[branches.length - 1];
      expand(last.node!.id);
      await settle();

      expect(catalogRows().filter(r => r.kind === 'catalog-leaf' && r.depth === 1).length).toBe(2);
    });

    it('shows no unbound count for a searchable provider that reports none, and the reported one when it does', async () => {
      await open();
      expect(unboundHeader('ha')?.count).toBeNull();

      await useProvider({ integrationId: 'ha', supportsSearch: true, supportsManualIds: true, unboundCount: 42 });
      await settle();
      expect(unboundHeader('ha')?.count).toBe(42);
    });

    it('offers manual id entry with the picker\'s filters and picks what gets bound through it', async () => {
      component.mode = 'pick';
      component.writableOnly = true;
      component.acceptedTypes = ['numeric'];
      await settle();

      const input = fixture.debugElement.query(By.directive(VariableCatalogIdInputComponent));
      expect(input).withContext('manual id entry is offered for a catalog source').not.toBeNull();
      const instance = input.componentInstance as VariableCatalogIdInputComponent;
      expect(instance.writableOnly()).toBeTrue();
      expect(instance.acceptedTypes()).toEqual(['numeric']);

      const picked = spyOn(component.pick, 'emit');
      const bound = variable('bound-1', 'integration', 'ha');
      input.triggerEventHandler('bound', bound);

      expect(picked).toHaveBeenCalledWith(bound);
    });
  });

  describe('a catalog without search, nested like OBS', () => {
    function leaf(id: string, name: string): VariableCatalogNode {
      return { id, name, suggestedName: name, displayName: null, hasChildren: false, type: 'numeric' } as VariableCatalogNode;
    }

    function container(id: string): VariableCatalogNode {
      return { id, name: id, displayName: null, hasChildren: true } as VariableCatalogNode;
    }

    let tree: Record<string, VariableCatalogNode[]>;

    beforeEach(async () => {
      tree = {
        root: [container('conn')],
        conn: [container('conn/input')],
        'conn/input': [leaf('conn/input/volume', 'obs_mic_volume'), leaf('conn/input/balance', 'obs_mic_balance')],
      };
      api().discoverCatalogVariables.and.callFake(async (request: DiscoverCatalogVariablesRequest) => ({
        nodes: tree[request.parentId ?? 'root'] ?? [],
        hasMore: false,
        available: true,
      }));
      await useProvider({ integrationId: 'obs2', supportsSearch: false, supportsManualIds: false });
      api().discoverCatalogVariables.calls.reset();
      component.source = { kind: 'integration', integrationId: 'obs2' };
      await settle();
    });

    it('lists leaves below nested containers inline, without opening anything', async () => {
      await openUnbound('obs2');
      const leaves = catalogRows().filter(r => r.kind === 'catalog-leaf').map(r => r.node!.id);

      expect(leaves).toEqual(['conn/input/volume', 'conn/input/balance']);
      expect(catalogRows().some(r => r.kind === 'catalog-branch')).toBeFalse();
    });

    it('narrows the nested leaves by the search box on the client', async () => {
      component.search.set('balance');
      await afterSearchDebounce();
      await settle();

      expect(catalogRows().filter(r => r.kind === 'catalog-leaf').map(r => r.node!.id)).toEqual(['conn/input/balance']);
      expect(discoverCalls().every(call => call.search === undefined)).toBeTrue();
    });

    it('gives no count while more of the catalog is still to be loaded', async () => {
      const many = Array.from({ length: 300 }, (_, i) => leaf(`conn/input/l${i}`, `obs_l${i}`));
      tree = { root: many };
      await useProvider({ integrationId: 'obs4', supportsSearch: false, supportsManualIds: false });
      component.source = { kind: 'integration', integrationId: 'obs4' };
      await settle();

      expect(unboundHeader('obs4')).toBeTruthy();
      expect(unboundHeader('obs4')?.count).toBeNull();
    });

    it('reaches the rest of a long catalog through a load more row, whatever the window height', async () => {
      const many = Array.from({ length: 150 }, (_, i) => leaf(`conn/input/l${i}`, `obs_l${i}`));
      tree = { root: many };
      await useProvider({ integrationId: 'obs5', supportsSearch: false, supportsManualIds: false });
      component.source = { kind: 'integration', integrationId: 'obs5' };
      await settle();
      await openUnbound('obs5');
      expect(catalogRows().filter(r => r.kind === 'catalog-leaf').length).toBe(100);
      expect(catalogRows()[catalogRows().length - 1].kind).toBe('catalog-grow');

      component.growCatalog('obs5');
      await settle();

      expect(catalogRows().filter(r => r.kind === 'catalog-leaf').length).toBe(150);
      expect(catalogRows().some(r => r.kind === 'catalog-grow')).toBeFalse();
    });

    it('stops walking nested containers once the budget is full', async () => {
      const many = (prefix: string) => Array.from({ length: 1000 }, (_, i) => leaf(`${prefix}/l${i}`, `${prefix}_l${i}`));
      tree = { root: [container('c1'), container('c2'), container('c3')], c1: many('c1'), c2: many('c2'), c3: many('c3') };
      await useProvider({ integrationId: 'obs3', supportsSearch: false, supportsManualIds: false });
      api().discoverCatalogVariables.calls.reset();
      component.source = { kind: 'integration', integrationId: 'obs3' };
      await settle();
      await openUnbound('obs3');

      const leaves = catalogRows().filter(r => r.kind === 'catalog-leaf');
      expect(leaves.length).toBeGreaterThan(0);
      expect(leaves.length).toBeLessThanOrEqual(1000);
      expect(discoverCalls().some(call => call.parentId === 'c2' || call.parentId === 'c3')).toBeFalse();
    });
  });

  describe('two catalogs side by side', () => {
    it('loads the one that is opened although a closed one already holds a full page of entries', async () => {
      const big = Array.from({ length: 150 }, (_, i) => ({
        id: `a/${i}`, name: `a_${i}`, suggestedName: `a_${i}`, displayName: null, hasChildren: false, type: 'text',
      })) as VariableCatalogNode[];
      const entity = { id: 'entity/light.one', name: 'light_one', suggestedName: 'ha_light_one', displayName: null, hasChildren: true } as VariableCatalogNode;
      api().discoverCatalogVariables.and.callFake(async (request: DiscoverCatalogVariablesRequest) => ({
        nodes: request.integrationId === 'aaa' ? big : request.parentId ? [] : [entity],
        hasMore: false,
        available: true,
      }));
      api().getVariableCatalogProviders.and.resolveTo({
        providers: [
          { integrationId: 'aaa', name: 'aaa', supportsSearch: false, supportsManualIds: false },
          { integrationId: 'zzz', name: 'zzz', supportsSearch: true, supportsManualIds: false },
        ],
      } as never);
      await TestBed.inject(VariableCatalogService).loadProviders();
      component.source = { kind: 'all' };
      await settle();
      expect(discoverCalls().some(call => call.integrationId === 'zzz')).toBeFalse();

      await openUnbound('zzz');

      expect(catalogRows().filter(r => r.kind === 'catalog-branch').map(r => r.node!.id)).toEqual(['entity/light.one']);
    });
  });

  describe('a variable that reads from a file', () => {
    const readOnlyFile: Variable = {
      ...variable('f1', 'user'),
      value: 'from the file',
      canWrite: false,
      available: false,
      fileSource: { path: '/home/me/now-playing.txt', allowWriteBack: false },
    };

    it('creates with the chosen file and write-back instead of an initial value', async () => {
      component.openCreate();
      await component.onNameInput('now_playing');
      component.setFormSource('file');
      component.setFormInitialValue('ignored');
      expect(component.canSubmitCreate()).toBeFalse();

      component.setFormFilePath(' /home/me/now-playing.txt ');
      component.setFormAllowWriteBack(true);
      await component.submitCreate();

      const request = createVariableSpy.calls.mostRecent().args[0];
      expect(request.fileSource).toEqual({ path: '/home/me/now-playing.txt', allowWriteBack: true });
      expect(request.initialValue).toBeUndefined();
    });

    it('keeps the form open and says why when the host refuses the path', async () => {
      createVariableSpy.and.resolveTo({ success: false, error: { code: 'InvalidFilePath', message: '' } });
      component.openCreate();
      await component.onNameInput('now_playing');
      component.setFormSource('file');
      component.setFormFilePath('now-playing.txt');

      await component.submitCreate();

      expect(component.showCreateModal()).toBeTrue();
      expect(component.createError()).toBeTruthy();
    });

    it('marks the row as reading from a file, locks its value and explains a missing value', async () => {
      fixture.componentRef.setInput('variables', [readOnlyFile]);
      await fixture.whenStable();
      await flushViewport();

      const row = (Array.from(fixture.nativeElement.querySelectorAll('.vars-row')) as HTMLElement[])
        .find(candidate => candidate.textContent?.includes('var_f1'));

      expect(row?.querySelector('.vars-file-badge')?.getAttribute('title')).toBe('/home/me/now-playing.txt');
      expect(row?.querySelector('.read-only-icon')).not.toBeNull();
      expect(component.unavailableTooltip(readOnlyFile)).toBe(component.fileUnavailableLabel());
    });

    it('saves changed file settings through an update of the file source', async () => {
      updateVariableSpy.and.resolveTo({ success: true, variable: readOnlyFile });
      component.openFileSettings(readOnlyFile);
      component.setFileSettingsPath('/home/me/other.txt');
      component.setFileSettingsWriteBack(true);

      await component.saveFileSettings();

      expect(updateVariableSpy).toHaveBeenCalledWith({
        id: 'f1',
        fileSource: { path: '/home/me/other.txt', allowWriteBack: true },
      });
    });
  });
});
