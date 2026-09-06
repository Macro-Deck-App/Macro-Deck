import { ActionFlow, EVENT_TRIGGER_TYPE, LocalizationTranslator, LocalizedText, isEventFlow, normalizeEventTriggerNames, resolveLocalizedText } from '@macro-deck/runtime';

export interface Automation {
  id: string;
  name: string;
  description: string;
  enabled: boolean;
  flows: ActionFlow[];
  createdAt: string;
  updatedAt: string;
}

export function automationEventFlow(automation: Pick<Automation, 'flows'>): ActionFlow | undefined {
  return automation.flows.find(isEventFlow);
}

export function automationActionCount(automation: Pick<Automation, 'flows'>): number {
  return automationEventFlow(automation)?.children.length ?? 0;
}

export function automationEventName(
  automation: Pick<Automation, 'flows'>,
  localization?: LocalizationTranslator,
): string | undefined {
  const eventName = automationEventFlow(automation)?.event?.eventName;
  if (eventName === undefined) return undefined;

  const resolved = typeof eventName === 'string'
    ? eventName
    : localization ? resolveLocalizedText(eventName as LocalizedText, localization) : '';
  return resolved.trim() || undefined;
}

export function automationIsIncomplete(automation: Pick<Automation, 'flows'>): boolean {
  return !automationEventFlow(automation)?.event?.eventId;
}

export function automationEditableFlows(
  automation: Pick<Automation, 'flows'>,
  newTriggerId: () => string,
  localization?: LocalizationTranslator,
): ActionFlow[] {
  const existing = automationEventFlow(automation);
  const flows = existing
    ? [existing]
    : [{ triggerId: newTriggerId(), triggerType: EVENT_TRIGGER_TYPE, children: [] }];
  return localization ? normalizeEventTriggerNames(flows, localization) : flows;
}
