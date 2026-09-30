import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService, VariableService } from '@shared';
import type { DiscoverCatalogVariablesRequest, Variable, VariableCatalogNode } from '@macro-deck/runtime';
import { VariableBindDialogComponent } from '../variables/variable-bind-dialog.component';
import { By } from '@angular/platform-browser';
import { SnippetInsertion } from './template-snippet-list.component';
import { TemplateVariableTreeComponent } from './template-variable-tree.component';

function variable(overrides: Partial<Variable>): Variable {
  return {
    id: overrides.id ?? 'id',
    name: 'name',
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: '',
    ...overrides,
  };
}

// Deliberately Spotify, not OBS: "spotify" appears in neither the identifier nor the display name,
// so a provider-blind search cannot pass this by accident.
const spotifyVar = variable({
  id: 's1', name: 'now_playing_uri', classification: 'integration', ownerIntegrationId: 'spotify',
  displayName: 'Track title',
});
const cpuVar = variable({
  id: 'c1', name: 'sysmon_c0', classification: 'integration', ownerIntegrationId: 'system',
  displayName: 'Processor load',
});
const macCpuVar = variable({
  id: 'c2', name: 'obs_mac_cpu_usage', classification: 'integration', ownerIntegrationId: 'obs',
});

describe('TemplateVariableTreeComponent', () => {
  let fixture: ComponentFixture<TemplateVariableTreeComponent>;
  let component: TemplateVariableTreeComponent;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables', 'getIntegrations', 'onNotification', 'getVariableCatalogProviders', 'discoverCatalogVariables',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.getVariableCatalogProviders.and.resolveTo({ providers: [] });
    apiSpy.discoverCatalogVariables.and.resolveTo({ nodes: [], hasMore: false, available: true } as never);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [TemplateVariableTreeComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(TemplateVariableTreeComponent);
    component = fixture.componentInstance;
  });

  async function setVariables(vars: Variable[]): Promise<void> {
    component.variables = vars;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function emptyStateHeading(): string | undefined {
    return fixture.nativeElement.querySelector('shared-empty-state')?.textContent ?? undefined;
  }

  describe('empty states', () => {
    it('shows the distinct "no variables" state for an empty list', async () => {
      await setVariables([]);

      expect(component.rows().length).toBe(0);
      expect(emptyStateHeading()).toContain(component.noVariablesHeading());
    });

    it('shows the distinct "no search results" state when a query matches nothing', async () => {
      await setVariables([spotifyVar]);
      component.search.set('no-such-thing');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.rows().length).toBe(0);
      expect(emptyStateHeading()).toContain(component.noResultsHeading());
    });
  });

  describe('search', () => {
    beforeEach(async () => {
      await setVariables([spotifyVar, cpuVar, macCpuVar]);
    });

    it('matches by identifier alone', async () => {
      component.search.set('uri');
      fixture.detectChanges();
      await fixture.whenStable();

      const rowVars = component.rows().filter(r => r.kind === 'variable').map(r => r.variable);
      expect(rowVars).toEqual([spotifyVar]);
    });

    it('matches by display name alone', async () => {
      component.search.set('processor');
      fixture.detectChanges();
      await fixture.whenStable();

      const rowVars = component.rows().filter(r => r.kind === 'variable').map(r => r.variable);
      expect(rowVars).toEqual([cpuVar]);
    });

    it('matches by provider alone', async () => {
      component.search.set('spotify');
      fixture.detectChanges();
      await fixture.whenStable();

      const rowVars = component.rows().filter(r => r.kind === 'variable').map(r => r.variable);
      expect(rowVars).toEqual([spotifyVar]);
    });

    it('is case-insensitive and substring, not prefix', async () => {
      for (const query of ['CPU', 'cpu', 'mac_cpu']) {
        component.search.set(query);
        fixture.detectChanges();
        await fixture.whenStable();

        const rowVars = component.rows().filter(r => r.kind === 'variable').map(r => r.variable);
        expect(rowVars).toEqual([macCpuVar], `query "${query}"`);
      }
    });
  });

  describe('insertion', () => {
    it('computes the canonical vars.<name> reference for a normal variable', () => {
      expect(component.referenceText(spotifyVar)).toBe('{{ vars.now_playing_uri }}');
    });

    it('counterexample: an event-origin variable inserts {{ event.<name> }}', () => {
      const eventVar = variable({ id: 'e1', name: 'volume', origin: 'event' });
      expect(component.referenceText(eventVar)).toBe('{{ event.volume }}');
    });

    it('counterexample: an input-origin variable still inserts {{ vars.<name> }}', () => {
      const inputVar = variable({ id: 'i1', name: 'threshold', origin: 'input' });
      expect(component.referenceText(inputVar)).toBe('{{ vars.threshold }}');
    });
  });

  describe('display names', () => {
    it('resolves a localized display name through the active language', async () => {
      const localized = variable({
        id: 'l1', name: 'obs_mac_cpu_usage', classification: 'integration', ownerIntegrationId: 'obs',
        displayName: { $localized: { scope: 'macrodeck.app', key: 'TemplateBuilder.Preview' } },
      });
      await setVariables([localized]);

      const row = component.rows().find(r => r.kind === 'variable')!;
      expect(row.primary).toBe('Preview');
      expect(row.secondary).toBe('vars.obs_mac_cpu_usage');
    });

    it('renders an unresolvable reference conspicuously rather than leaking the wire object', async () => {
      const unresolvable = variable({
        id: 'l2', name: 'obs_mac_cpu_usage', classification: 'integration', ownerIntegrationId: 'obs',
        displayName: { $localized: { scope: 'macrodeck.app', key: 'No.Such.Key' } },
      });
      await setVariables([unresolvable]);

      const row = component.rows().find(r => r.kind === 'variable')!;
      expect(row.primary).toBe('[[macrodeck.app:No.Such.Key]]');
      expect(row.primary).not.toContain('object Object');
    });
  });

  describe('collapse', () => {
    it('collapsing a group hides its variables; expanding restores them', async () => {
      await setVariables([spotifyVar]);
      const groupKey = component.rows().find(r => r.kind === 'group-header')!.key;

      component.toggleCollapse(groupKey);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(component.rows().some(r => r.kind === 'variable')).toBeFalse();

      component.toggleCollapse(groupKey);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(component.rows().some(r => r.kind === 'variable')).toBeTrue();
    });

    it('counterexample: a search hit inside a collapsed group is visible; clearing restores the collapse', async () => {
      await setVariables([spotifyVar]);
      const groupKey = component.rows().find(r => r.kind === 'group-header')!.key;
      component.toggleCollapse(groupKey);
      fixture.detectChanges();
      await fixture.whenStable();

      component.search.set('uri');
      fixture.detectChanges();
      await fixture.whenStable();
      expect(component.rows().some(r => r.kind === 'variable')).toBeTrue();

      component.search.set('');
      fixture.detectChanges();
      await fixture.whenStable();
      expect(component.rows().some(r => r.kind === 'variable')).toBeFalse();
    });
  });

  describe('value and type', () => {
    it('shows the live value of a variable', () => {
      const v = variable({ id: 'v', name: 'system_cpu_usage_percent', type: 'numeric', value: '42' });
      expect(component.valueOf(v)).toBe('42');
    });

    it('marks a value the provider currently cannot supply rather than showing it as empty', () => {
      const v = variable({ id: 'v', name: 'obs_current_scene', value: '', available: false });

      expect(component.valueOf(v)).toBeNull();
      expect(component.unavailableOf(v)).toBeTruthy();
      expect(component.unavailableOf(variable({ id: 'w', name: 'other', value: '1' }))).toBeNull();
    });

    it('labels each type distinctly', async () => {
      fixture.nativeElement.style.width = '260px';
      fixture.nativeElement.style.height = '400px';
      await setVariables((['text', 'numeric', 'boolean'] as const)
        .map(type => variable({ id: type, name: type, type })));
      fixture.detectChanges();
      await fixture.whenStable();

      const labels = Array.from(fixture.nativeElement.querySelectorAll('.vr-type'))
        .map(el => (el as HTMLElement).textContent?.trim() ?? '');

      expect(labels.length).toBe(3);
      expect(new Set(labels).size).toBe(3);
      expect(labels.every(l => l.length > 0)).toBeTrue();
    });
  });

  describe('scrollbar clearance', () => {
    // A hovered overlay scrollbar (macOS, both Chrome and Firefox) is about 15px wide and is drawn
    // on top of the content. Its width cannot be asserted - headless Chrome always reports 0 - but
    // the clearance the row leaves for it is plain layout, so that is what this pins.
    it('leaves the copy button clear of the scroll container edge', async () => {
      // The virtual scroll renders nothing into a zero-height host, so the pane gets a real size.
      fixture.nativeElement.style.width = '260px';
      fixture.nativeElement.style.height = '400px';
      await setVariables([spotifyVar]);
      fixture.detectChanges();
      await fixture.whenStable();

      const viewport = fixture.nativeElement.querySelector('cdk-virtual-scroll-viewport') as HTMLElement;
      const copy = fixture.nativeElement.querySelector('.tvt-var-copy') as HTMLElement;
      expect(copy).withContext('copy button rendered').not.toBeNull();

      // Measured against the scrollport, not the element's border box: the cdk content wrapper is
      // free to grow past the border box, and measuring against that reported a healthy clearance
      // while the button was in fact sitting under the scrollbar.
      const visibleRight = viewport.getBoundingClientRect().left + viewport.clientWidth;
      expect(visibleRight - copy.getBoundingClientRect().right).toBeGreaterThanOrEqual(15);
    });

    it('never widens the list past its scrollport, however long an identifier is', async () => {
      fixture.nativeElement.style.width = '260px';
      fixture.nativeElement.style.height = '400px';
      await setVariables([variable({
        id: 'long', name: 'obs_the_streaming_machine_recording_timecode_value',
        classification: 'integration', ownerIntegrationId: 'obs',
      })]);
      fixture.detectChanges();
      await fixture.whenStable();

      const viewport = fixture.nativeElement.querySelector('cdk-virtual-scroll-viewport') as HTMLElement;
      expect(viewport.scrollWidth).toBeLessThanOrEqual(viewport.clientWidth);
    });
  });

  describe('unbound catalog entries', () => {
    const entry: VariableCatalogNode = {
      id: 'entity/light.desk', name: 'light.desk', suggestedName: 'ha_light_desk', displayName: 'Desk lamp',
      hasChildren: false, type: 'text',
    } as VariableCatalogNode;
    const haVar = variable({
      id: 'h1', name: 'ha_connected', classification: 'integration', ownerIntegrationId: 'ha',
    });

    async function settle(): Promise<void> {
      for (let i = 0; i < 8; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await new Promise(resolve => setTimeout(resolve, 0));
      }
      fixture.detectChanges();
    }

    async function useCatalog(supportsSearch: boolean, nodes: VariableCatalogNode[] = [entry]): Promise<void> {
      apiSpy.getVariableCatalogProviders.and.resolveTo({
        providers: [{ integrationId: 'ha', name: 'Home', supportsSearch, supportsManualIds: false }],
      } as never);
      apiSpy.discoverCatalogVariables.and.callFake(async (request: DiscoverCatalogVariablesRequest) => ({
        nodes: request.search && !'desk lamp'.includes(request.search.toLowerCase()) ? [] : nodes,
        hasMore: false,
        available: true,
      }));
    }

    function kinds(): string[] {
      return component.rows().map(r => r.kind);
    }

    it('offers them in a closed group under their integration, ahead of its variables', async () => {
      await useCatalog(false);
      await setVariables([haVar]);
      await settle();

      expect(kinds()).toEqual(['group-header', 'unbound-header', 'variable']);

      component.toggleUnbound('ha');
      await settle();

      expect(kinds()).toEqual(['group-header', 'unbound-header', 'catalog-leaf', 'variable']);
    });

    it('lists an integration that has nothing bound yet instead of the empty state', async () => {
      await useCatalog(true);
      await setVariables([]);
      await settle();

      expect(kinds()).toEqual(['group-header', 'unbound-header']);
      expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeNull();
    });

    it('finds an entry that only the catalog knows', async () => {
      await useCatalog(true);
      await setVariables([spotifyVar]);
      await settle();

      component.search.set('desk');
      await new Promise(resolve => setTimeout(resolve, 600));
      await settle();

      expect(kinds()).toEqual(['group-header', 'unbound-header', 'catalog-leaf']);
      expect(fixture.nativeElement.querySelector('shared-empty-state')).toBeNull();
    });

    it('binds an entry in place, inserts its reference and lists it as a variable', async () => {
      fixture.nativeElement.style.width = '260px';
      fixture.nativeElement.style.height = '400px';
      await useCatalog(false);
      await setVariables([haVar]);
      await settle();
      component.toggleUnbound('ha');
      await settle();
      const inserted: SnippetInsertion[] = [];
      component.insert.subscribe(insertion => inserted.push(insertion));

      (fixture.nativeElement.querySelector('.tvt-catalog-leaf button') as HTMLButtonElement).click();
      await settle();
      const dialog = fixture.debugElement.query(By.directive(VariableBindDialogComponent));
      expect(dialog).withContext('the bind dialog opens from the pane').not.toBeNull();

      const bound = variable({
        id: 'b1', name: 'ha_light_desk', classification: 'integration', ownerIntegrationId: 'ha',
        dynamicResourceId: 'entity/light.desk',
      });
      TestBed.inject(VariableService).variables.set([bound]);
      dialog.triggerEventHandler('bound', bound);
      await settle();

      expect(inserted.map(i => i.text)).toEqual(['{{ vars.ha_light_desk }}']);
      expect(component.rows().filter(r => r.kind === 'variable').map(r => r.variable.id)).toEqual(['h1', 'b1']);
      expect(fixture.debugElement.query(By.directive(VariableBindDialogComponent))).toBeNull();
    });

    it('does not repeat a global that a variable of the same name already shadows', async () => {
      await useCatalog(false);
      const local = variable({ id: 'w1', name: 'ha_light_desk', scope: 'widget', scopeRefId: 'widget-1' });
      TestBed.inject(VariableService).variables.set([variable({
        id: 'b1', name: 'ha_light_desk', classification: 'integration', ownerIntegrationId: 'ha',
        dynamicResourceId: 'entity/light.desk',
      })]);
      await setVariables([local]);
      await settle();

      expect(component.rows().filter(r => r.kind === 'variable').map(r => r.variable.id)).toEqual(['w1']);
    });
  });
});
