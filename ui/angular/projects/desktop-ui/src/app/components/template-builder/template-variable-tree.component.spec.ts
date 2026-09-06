import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
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

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables', 'getIntegrations', 'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
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

  describe('empty states', () => {
    it('shows the distinct "no variables" state for an empty list', async () => {
      await setVariables([]);
      expect(component.hasAnyVariables()).toBeFalse();
      expect(component.hasSearchResults()).toBeFalse();
    });

    it('shows the distinct "no search results" state when a query matches nothing', async () => {
      await setVariables([spotifyVar]);
      component.search.set('no-such-thing');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(component.hasAnyVariables()).toBeTrue();
      expect(component.hasSearchResults()).toBeFalse();
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
      const unavailable = component.valueOf(v);

      expect(unavailable).not.toBe('');
      expect(unavailable).toBe(component.valueOf(variable({ id: 'w', name: 'other', available: false })));
    });

    it('labels each type distinctly', () => {
      const labels = (['text', 'numeric', 'boolean'] as const)
        .map(type => component.typeLabel(variable({ id: type, name: type, type })));

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
});
