import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { WidgetStateUpdatedEvent } from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '@shared';
import { WidgetStateService } from './widget-state.service';

describe('WidgetStateService', () => {
  const widgetId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

  let apiSpy: jasmine.SpyObj<ApiService>;
  let connectionState: BehaviorSubject<ConnectionState>;
  let events: Subject<WidgetStateUpdatedEvent>;
  let service: WidgetStateService;

  function subscribeResult(event: WidgetStateUpdatedEvent | null): void {
    apiSpy.invokeResult.and.resolveTo(event);
  }

  beforeEach(() => {
    connectionState = new BehaviorSubject<ConnectionState>('connected');
    events = new Subject<WidgetStateUpdatedEvent>();
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['onNotification', 'invoke', 'invokeResult'],
      { connectionState$: connectionState.asObservable() },
    );
    apiSpy.onNotification.and.returnValue(events.asObservable() as Observable<never>);
    apiSpy.invoke.and.resolveTo();
    subscribeResult(null);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(WidgetStateService);
  });

  it('applies the state id returned by the subscribe invoke', async () => {
    subscribeResult({ widgetId, stateId: 'on', stateLabel: 'On' });

    const stateId = service.subscribe(widgetId);
    await Promise.resolve();

    expect(apiSpy.invokeResult).toHaveBeenCalledWith('SubscribeWidgetState', widgetId);
    expect(stateId()).toBe('on');
  });

  it('updates the signal from a pushed state-change event, by id', () => {
    const stateId = service.subscribe(widgetId);

    events.next({ widgetId, stateId: 'warming-up', stateLabel: 'Warming up' });

    expect(stateId()).toBe('warming-up');
  });

  // H2: the service must not special-case on/off - a third (or later) state id is applied exactly
  // like any other, since it is the host's resolved value.
  it('applies a non on/off state id exactly as pushed', () => {
    const stateId = service.subscribe(widgetId);

    events.next({ widgetId, stateId: 'unavailable', stateLabel: 'Unavailable' });

    expect(stateId()).toBe('unavailable');
  });

  // H6: a reconnect re-subscribes, but must never regress the signal to null/undefined in the
  // meantime - the last state the client actually knows about stays displayed until the fresh reply
  // (which is the host's own answer, not a client-side guess) arrives.
  it('keeps the last known state id across a reconnect until the fresh subscribe reply lands', async () => {
    subscribeResult({ widgetId, stateId: 'on', stateLabel: 'On' });
    const stateId = service.subscribe(widgetId);
    await Promise.resolve();
    expect(stateId()).toBe('on');

    apiSpy.invokeResult.and.returnValue(new Promise(() => undefined)); // reconnect's reply never resolves in this test
    connectionState.next('disconnected');
    connectionState.next('connected');

    expect(stateId()).toBe('on');
  });

  it('re-subscribes and applies the reconnect reply\'s state once it lands', async () => {
    subscribeResult({ widgetId, stateId: 'off', stateLabel: 'Off' });
    const stateId = service.subscribe(widgetId);
    await Promise.resolve();
    expect(stateId()).toBe('off');

    subscribeResult({ widgetId, stateId: 'on', stateLabel: 'On' });
    connectionState.next('connected');
    await Promise.resolve();

    expect(apiSpy.invokeResult).toHaveBeenCalledTimes(2);
    expect(stateId()).toBe('on');
  });

  it('unsubscribes server-side once the last subscriber releases', () => {
    service.subscribe(widgetId);
    service.subscribe(widgetId);

    service.release(widgetId);
    expect(apiSpy.invoke).not.toHaveBeenCalled();

    service.release(widgetId);
    expect(apiSpy.invoke).toHaveBeenCalledWith('UnsubscribeWidgetState', widgetId);
  });
});
