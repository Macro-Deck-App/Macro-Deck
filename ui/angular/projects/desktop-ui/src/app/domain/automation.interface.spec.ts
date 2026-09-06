import { ActionFlow, LocalizationTranslator } from '@macro-deck/runtime';
import { automationActionCount, automationEditableFlows, automationEventFlow, automationEventName, automationIsIncomplete } from './automation.interface';

const fakeTranslator: LocalizationTranslator = {
  translate: (_scope, key) => (key === 'Obs.SceneChanged.Name' ? 'Scene Changed' : `[[${key}]]`),
};

describe('automation.interface', () => {
  function eventFlow(triggerId: string, eventId?: string, children: unknown[] = [], eventName?: string): ActionFlow {
    return {
      triggerId,
      triggerType: 'onEvent',
      event: eventId ? { providerId: 'obs', eventId, eventName } : undefined,
      children,
    } as ActionFlow;
  }

  function pressFlow(): ActionFlow {
    return { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [{ id: 'b1' }] } as ActionFlow;
  }

  it('finds the single event flow', () => {
    const flows = [pressFlow(), eventFlow('t1', 'scene-changed')];

    expect(automationEventFlow({ flows })?.triggerId).toBe('t1');
  });

  it('takes only the first event flow', () => {
    const flows = [eventFlow('t1', 'scene-changed'), eventFlow('t2', 'recording-started')];

    expect(automationEventFlow({ flows })?.triggerId).toBe('t1');
  });

  it('counts the actions of that one trigger', () => {
    const flows = [eventFlow('t1', 'scene-changed', [{ id: 'b1' }, { id: 'b2' }])];

    expect(automationActionCount({ flows })).toBe(2);
  });

  it('ignores a stray press flow when counting actions', () => {
    expect(automationActionCount({ flows: [pressFlow()] })).toBe(0);
  });

  it('reports the cached event name for the list', () => {
    const flows = [eventFlow('t1', 'scene-changed', [], 'Scene Changed')];

    expect(automationEventName({ flows })).toBe('Scene Changed');
  });

  it('has no event name before an event is picked', () => {
    expect(automationEventName({ flows: [eventFlow('t1')] })).toBeUndefined();
    expect(automationEventName({ flows: [eventFlow('t1', 'scene-changed', [], '  ')] })).toBeUndefined();
  });

  it('resolves a $localized eventName rather than throwing on it', () => {
    const flows = [eventFlow('t1', 'scene-changed')];
    flows[0].event!.eventName = { $localized: { scope: 'macrodeck.app', key: 'Obs.SceneChanged.Name' } } as unknown as string;

    expect(() => automationEventName({ flows }, fakeTranslator)).not.toThrow();
    expect(automationEventName({ flows }, fakeTranslator)).toBe('Scene Changed');
  });

  it('treats an automation with no trigger as incomplete', () => {
    expect(automationIsIncomplete({ flows: [] })).toBeTrue();
  });

  it('treats a trigger with no event picked yet as incomplete', () => {
    expect(automationIsIncomplete({ flows: [eventFlow('t1')] })).toBeTrue();
  });

  it('is complete once the event is picked', () => {
    expect(automationIsIncomplete({ flows: [eventFlow('t1', 'scene-changed')] })).toBeFalse();
  });

  it('seeds a trigger for an automation that has none', () => {
    const flows = automationEditableFlows({ flows: [] }, () => 'generated');

    expect(flows).toEqual([{ triggerId: 'generated', triggerType: 'onEvent', children: [] }]);
  });

  it('keeps the stored trigger rather than reseeding it', () => {
    const stored = eventFlow('t1', 'scene-changed', [{ id: 'b1' }]);

    expect(automationEditableFlows({ flows: [stored] }, () => 'generated')).toEqual([stored]);
  });

  it('drops anything that is not the event flow', () => {
    const stored = eventFlow('t1', 'scene-changed');

    expect(automationEditableFlows({ flows: [pressFlow(), stored] }, () => 'generated')).toEqual([stored]);
  });

  it('normalizes a $localized eventName on the stored trigger to a plain resolved string', () => {
    const stored = eventFlow('t1', 'scene-changed');
    stored.event!.eventName = { $localized: { scope: 'macrodeck.app', key: 'Obs.SceneChanged.Name' } } as unknown as string;

    const [flow] = automationEditableFlows({ flows: [stored] }, () => 'generated', fakeTranslator);

    expect(typeof flow.event?.eventName).toBe('string');
    expect(flow.event?.eventName).toBe('Scene Changed');
    expect(JSON.stringify(flow)).not.toContain('$localized');
  });
});
