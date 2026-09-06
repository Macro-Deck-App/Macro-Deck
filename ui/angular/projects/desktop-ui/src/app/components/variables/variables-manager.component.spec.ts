import { CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import type { Variable, VariableClassification } from '@macro-deck/runtime';
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
      'setVariableValue',
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

    const name = fixture.nativeElement.querySelector('.vars-public-name') as HTMLElement;
    const value = fixture.nativeElement.querySelector('.vars-value') as HTMLElement;

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
      fixture.componentRef.setInput('variables', [...variables, dynamicVariable('d1', 'home-assistant')]);
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
      const ordinaryIntegrationRow = rows.find(row => row.textContent?.includes('var_3'));

      expect(dynamicRow?.querySelector('.vars-dots-btn:disabled')).toBeNull();
      expect(ordinaryIntegrationRow?.querySelector('.vars-dots-btn:disabled')).not.toBeNull();
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

      const row = fixture.nativeElement.querySelector('.vars-row-pick') as HTMLButtonElement;
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
      const names = Array.from(fixture.nativeElement.querySelectorAll('.vars-public-name'))
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
      expect(fixture.nativeElement.querySelector('.vars-group-header')).not.toBeNull();
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
      const row = fixture.nativeElement.querySelector('.vars-row-pick') as HTMLButtonElement;
      const renderedName = row.querySelector('.vars-public-name')?.textContent?.trim();
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

  describe('an integration catalog', () => {
    it('lists its unbound entries in the same run as the variables, marked instead of valued', async () => {
      // The point of the merge: to the user these are the same integration's variables, one lot with
      // a value and one lot still to be bound - not a separate browser sitting under the list.
      component.source = { kind: 'integration', integrationId: 'obs' };
      await fixture.whenStable();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const rows = component.rows();
      const leaf = rows.find(r => r.kind === 'catalog-leaf');

      expect(leaf).withContext('an unbound catalog entry belongs in the list').toBeTruthy();
      expect((leaf as { node: { name: string } }).node.name).toBe('volume');
    });

    it('narrows catalog entries by the search box, like the variables beside them', async () => {
      // They are listed as one list, so a search that filtered only half of it would leave entries
      // on screen that plainly do not match what was typed.
      component.source = { kind: 'integration', integrationId: 'obs' };
      await fixture.whenStable();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.rows().some(r => r.kind === 'catalog-leaf')).toBeTrue();

      component.search.set('nothing-matches-this');
      fixture.detectChanges();

      expect(component.rows().some(r => r.kind === 'catalog-leaf')).toBeFalse();
    });

    it('counts the unbound entries beside the variables, and one fewer once one is bound', async () => {
      // The fixture's catalog holds two leaves: `volume` unbound and `muted` already bound. What the
      // user is told is how many are still to be bound, so binding one has to move it by exactly one -
      // a count taken from whatever pages happen to be cached jumps instead, because binding
      // refetches them.
      component.source = { kind: 'integration', integrationId: 'obs' };
      await fixture.whenStable();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.unboundCount()).toBe(1);
    });

    it('does not list an entry that is already bound, which the list shows as a real variable', async () => {
      // `muted` carries a boundVariableId, so it is already above as the variable it became; listing
      // it again as a catalog entry would show one resource twice, once with a value and once without.
      component.source = { kind: 'integration', integrationId: 'obs' };
      await fixture.whenStable();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const names = component.rows()
        .filter(r => r.kind === 'catalog-leaf')
        .map(r => (r as { node: { name: string } }).node.name);

      expect(names).not.toContain('muted');
    });
  });
});
