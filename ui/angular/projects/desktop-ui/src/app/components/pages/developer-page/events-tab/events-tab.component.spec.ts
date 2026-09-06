import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ActionParameterType, EventDefinitionDto } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { SelectComponent } from '../../../forms/select/select.component';
import { EventCatalogService } from '../../../../services/event-catalog.service';
import { EventsTabComponent } from './events-tab.component';

function eventDto(
  id: string,
  name: string,
  overrides: Partial<EventDefinitionDto> = {},
): EventDefinitionDto {
  return {
    id,
    providerId: id.split('::')[0],
    providerName: 'OBS Studio',
    isIntegration: true,
    name,
    deliveryKind: 'push',
    configurationParameters: [],
    payloadParameters: [],
    ...overrides,
  };
}

describe('EventsTabComponent', () => {
  let fixture: ComponentFixture<EventsTabComponent>;
  let component: EventsTabComponent;
  let apiSpy: jasmine.SpyObj<ApiService>;

  async function setUp(events: EventDefinitionDto[]): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getEventDefinitions',
      'triggerEvent',
      'onNotification',
      'getActionParameterOptions',
    ]);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    apiSpy.getEventDefinitions.and.resolveTo({ events });
    apiSpy.triggerEvent.and.resolveTo({ success: true, queuedSubscriptions: 0 });
    apiSpy.getActionParameterOptions.and.resolveTo({ options: [], allowsCustomValue: false });

    TestBed.configureTestingModule({
      imports: [EventsTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    TestBed.inject(EventCatalogService);

    fixture = TestBed.createComponent(EventsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function select(id: string): void {
    const event = TestBed.inject(EventCatalogService).find(id)!;
    component['select'](event);
    fixture.detectChanges();
  }

  it('warns that a trigger runs real flows, before the form', async () => {
    await setUp([eventDto('obs::scene-changed', 'Scene Changed')]);
    select('obs::scene-changed');

    expect(fixture.nativeElement.querySelector('.trigger-warning')?.textContent)
      .toContain('publishes a real event');
  });

  it('posts the payload values and reports that nothing listens', async () => {
    await setUp([eventDto('obs::scene-changed', 'Scene Changed', {
      payloadParameters: [
        { name: 'sceneName', label: 'Scene', description: '', type: ActionParameterType.String },
      ],
    })]);
    select('obs::scene-changed');

    component['setValue']('sceneName', 'Intro');
    await component['trigger']();
    fixture.detectChanges();

    expect(apiSpy.triggerEvent).toHaveBeenCalledWith({
      eventId: 'obs::scene-changed',
      parameters: { sceneName: 'Intro' },
    });
    expect(fixture.nativeElement.querySelector('.trigger-result-detail')?.textContent)
      .toContain('No trigger currently listens');
  });

  it('says how many triggers a published occurrence was queued for', async () => {
    await setUp([eventDto('obs::scene-changed', 'Scene Changed')]);
    apiSpy.triggerEvent.and.resolveTo({ success: true, queuedSubscriptions: 2 });
    select('obs::scene-changed');

    await component['trigger']();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.trigger-result-detail')?.textContent)
      .toContain('Queued for 2 triggers');
  });

  it('refuses to trigger a scheduled event', async () => {
    await setUp([eventDto('time::every-day', 'Every Day', {
      providerName: 'Time',
      deliveryKind: 'scheduled',
    })]);
    select('time::every-day');

    await component['trigger']();

    expect(apiSpy.triggerEvent).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-button button')?.disabled).toBeTrue();
  });

  it('surfaces the host error code and message', async () => {
    await setUp([eventDto('obs::scene-changed', 'Scene Changed')]);
    apiSpy.triggerEvent.and.resolveTo({
      success: false,
      queuedSubscriptions: 0,
      error: { code: 'INTEGRATION_DISABLED', message: 'The integration is disabled' },
    });
    select('obs::scene-changed');

    await component['trigger']();
    fixture.detectChanges();

    const error = fixture.nativeElement.querySelector('.trigger-result-error')?.textContent;
    expect(error).toContain('INTEGRATION_DISABLED');
    expect(error).toContain('The integration is disabled');
  });

  it('titles the pane after the selected provider', async () => {
    await setUp([eventDto('obs::scene-changed', 'Scene Changed')]);

    expect(component['heading']()).toBe('All events');

    component['selectProvider']('obs');

    expect(component['heading']()).toBe('OBS Studio');
  });

  it('narrows the list by search over name, id and category', async () => {
    await setUp([
      eventDto('obs::scene-changed', 'Scene Changed', { category: 'Scenes' }),
      eventDto('obs::recording-started', 'Recording Started', { category: 'Recording' }),
    ]);

    component['search'].set('recording');

    expect(component['filteredEvents']().map(e => e.id)).toEqual(['obs::recording-started']);
  });

  it('renders a picker for a plugin-backed dynamic-choice payload parameter and requests its qualified event id', async () => {
    await setUp([eventDto('hue::light-changed', 'Light Changed', {
      payloadParameters: [
        { name: 'entityId', label: 'Entity', description: '', type: ActionParameterType.DynamicChoice, dynamicOptions: true },
      ],
    })]);
    apiSpy.getActionParameterOptions.and.resolveTo({
      options: [{ value: 'light.kitchen', label: 'Kitchen Light' }],
      allowsCustomValue: false,
    });
    select('hue::light-changed');

    const picker = fixture.debugElement.query(By.directive(SelectComponent));
    expect(picker).withContext('a dynamic-choice payload parameter should render a picker').not.toBeNull();

    (picker!.componentInstance as SelectComponent).open();
    await fixture.whenStable();

    expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(1);
    const request = apiSpy.getActionParameterOptions.calls.mostRecent().args[0];
    expect(request.eventId).toBe('hue::light-changed');
    expect(request.eventParameterKind).toBe('payload');
    expect(request.parameterName).toBe('entityId');
  });
});
