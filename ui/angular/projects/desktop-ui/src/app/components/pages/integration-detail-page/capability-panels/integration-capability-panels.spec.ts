import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';

import { GetIntegrationBatteriesResponse, IntegrationCapabilityDataChangedEvent, IpcIntegrationBattery } from '@macro-deck/runtime';
import { ApiService, UiSessionHandle, UiSessionOpenRequest, UiSessionOpenRequestSource, UiSessionService, WidgetTypeCatalogService, WidgetTypeInfo } from '@shared';
import { IntegrationBatteriesPanelComponent } from './integration-batteries-panel.component';
import { IntegrationWidgetsPanelComponent } from './integration-widgets-panel.component';

@Component({
  standalone: true,
  imports: [IntegrationBatteriesPanelComponent, IntegrationWidgetsPanelComponent],
  template: `
    @if (panel() === 'batteries') {
      <app-integration-batteries-panel [integrationId]="integrationId" />
    } @else {
      <app-integration-widgets-panel [integrationId]="integrationId" [available]="available()" />
    }
  `,
})
class HostComponent {
  readonly panel = signal<'batteries' | 'widgets'>('batteries');
  readonly available = signal(true);
  readonly integrationId = 'com.example.devices';
}

describe('Integration capability panels', () => {
  let dataEvents: Subject<IntegrationCapabilityDataChangedEvent>;
  let getIntegrationBatteries: jasmine.Spy;
  let opens: UiSessionOpenRequest[];
  let declined: Set<string>;
  let widgetTypes: WidgetTypeInfo[];

  function battery(overrides: Partial<IpcIntegrationBattery> = {}): IpcIntegrationBattery {
    return {
      id: 'com.example.devices:pad',
      name: 'Game pad',
      kind: 'controller',
      levelPercent: 64,
      chargeState: 'Discharging',
      isConnected: true,
      timeRemainingSeconds: null,
      timeToFullSeconds: null,
      health: 'Unknown',
      updatedAt: '2026-10-22T10:00:00Z',
      ...overrides,
    };
  }

  function batteries(overrides: Partial<GetIntegrationBatteriesResponse> = {}): GetIntegrationBatteriesResponse {
    return { found: true, available: true, batteries: [], ...overrides };
  }

  function widgetType(id: string, providerId: string, name: string): WidgetTypeInfo {
    return { id, providerId, isBuiltIn: false, name, description: `${name} description`, defaultData: {}, supportsConfigUi: true, configUiModelVersion: 1 };
  }

  async function settle(fixture: ComponentFixture<HostComponent>): Promise<void> {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function render(panel: 'batteries' | 'widgets', available = true): Promise<ComponentFixture<HostComponent>> {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.panel.set(panel);
    fixture.componentInstance.available.set(available);
    await settle(fixture);
    return fixture;
  }

  function text(fixture: ComponentFixture<HostComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  beforeEach(() => {
    localStorage.clear();
    dataEvents = new Subject<IntegrationCapabilityDataChangedEvent>();
    getIntegrationBatteries = jasmine.createSpy('getIntegrationBatteries').and.resolveTo(batteries());
    opens = [];
    declined = new Set();
    widgetTypes = [];

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            getIntegrationBatteries: (...args: unknown[]) => getIntegrationBatteries(...args),
            getLocalization: async () => ({ culture: 'en', fallbackCulture: 'en', translations: {}, availableCultures: ['en'] }),
            onNotification: (name: string) =>
              name === 'IntegrationCapabilityDataChangedEvent' ? dataEvents.asObservable() : EMPTY,
            connectionStateSignal: signal('connected'),
          },
        },
        {
          provide: UiSessionService,
          useValue: {
            open: (source: UiSessionOpenRequestSource): UiSessionHandle => {
              const request = typeof source === 'function' ? source() : source;
              opens.push(request);
              const refused = declined.has((request as { widgetType?: string }).widgetType ?? '');
              return {
                root: () => null,
                revision: () => 0,
                rejection: () => refused ? { code: 'declined', message: 'no preview' } : null,
                send: () => undefined,
                close: () => undefined,
              } as unknown as UiSessionHandle;
            },
          },
        },
        {
          provide: WidgetTypeCatalogService,
          useValue: { types: () => widgetTypes, load: () => Promise.resolve() },
        },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  describe('batteries', () => {
    it('lists each battery of the integration with its level and charge state', async () => {
      getIntegrationBatteries.and.resolveTo(batteries({
        batteries: [
          battery(),
          battery({ id: 'com.example.devices:mouse', name: 'Mouse', kind: 'mouse', levelPercent: 8, chargeState: 'Charging' }),
        ],
      }));

      const fixture = await render('batteries');

      const rows = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.battery-row'));
      expect(getIntegrationBatteries).toHaveBeenCalledWith('com.example.devices');
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('Game pad');
      expect(rows[0].textContent).toContain('64%');
      expect(rows[0].textContent).toContain('Discharging');
      expect(rows[1].textContent).toContain('Charging');
      expect(rows[1].textContent).toContain('8%');
    });

    it('says the data is unavailable, not that there are no batteries, while the integration is not running', async () => {
      getIntegrationBatteries.and.resolveTo(batteries({ available: false }));

      const fixture = await render('batteries');

      expect(text(fixture)).toContain('Not available right now');
      expect(text(fixture)).not.toContain('No batteries');
    });

    it('says there are no batteries when a running integration reports none', async () => {
      const fixture = await render('batteries');

      expect(text(fixture)).toContain('No batteries');
    });

    it('shows an error when the batteries cannot be loaded', async () => {
      getIntegrationBatteries.and.rejectWith(new Error('boom'));

      const fixture = await render('batteries');

      expect(fixture.nativeElement.querySelector('shared-error-banner')).not.toBeNull();
    });

    it('reloads when the host reports changed batteries for this integration only', async () => {
      const fixture = await render('batteries');
      getIntegrationBatteries.and.resolveTo(batteries({ batteries: [battery({ levelPercent: 30 })] }));

      dataEvents.next({ kind: 'battery-provider', integrationIds: ['com.example.other'] });
      dataEvents.next({ kind: 'calendar', integrationIds: ['com.example.devices'] });
      await new Promise(resolve => setTimeout(resolve, 250));
      expect(getIntegrationBatteries).toHaveBeenCalledTimes(1);

      dataEvents.next({ kind: 'battery-provider', integrationIds: ['com.example.devices'] });
      await new Promise(resolve => setTimeout(resolve, 250));
      await settle(fixture);

      expect(getIntegrationBatteries).toHaveBeenCalledTimes(2);
      expect(text(fixture)).toContain('30%');
    });
  });

  describe('widgets', () => {
    beforeEach(() => {
      widgetTypes = [
        widgetType('com.example.devices::gauge', 'com.example.devices', 'Gauge'),
        widgetType('com.example.devices::meter', 'com.example.devices', 'Meter'),
        widgetType('com.example.other::clock', 'com.example.other', 'Other clock'),
      ];
    });

    it('lists only the widget types of this integration and asks each for its sample preview', async () => {
      const fixture = await render('widgets');

      const names = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.widget-card-name'))
        .map(name => name.textContent?.trim());
      expect(names).toEqual(['Gauge', 'Meter']);
      expect(opens.map(request => (request as { widgetType?: string; sample?: boolean })))
        .toEqual(jasmine.arrayWithExactContents([
          jasmine.objectContaining({ widgetType: 'com.example.devices::gauge', sample: true }),
          jasmine.objectContaining({ widgetType: 'com.example.devices::meter', sample: true }),
        ]));
    });

    it('keeps the card with its name and a placeholder when a type has no preview', async () => {
      declined.add('com.example.devices::gauge');

      const fixture = await render('widgets');

      const gauge = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.widget-card'))
        .find(card => card.textContent?.includes('Gauge'))!;
      expect(gauge.querySelector('.type-preview-placeholder')).not.toBeNull();
      expect(gauge.textContent).toContain('Gauge description');
    });

    it('distinguishes an integration that is not running from one without widgets', async () => {
      widgetTypes = [];

      const stopped = await render('widgets', false);
      expect(text(stopped)).toContain('Not available right now');

      const running = await render('widgets', true);
      expect(text(running)).toContain('No widgets');
    });
  });
});
