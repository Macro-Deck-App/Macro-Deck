import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionFlow } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { Automation } from '../../../domain/automation.interface';
import { AutomationService } from '../../../services/automation.service';

import { AutomationsPageComponent } from './automations-page.component';

interface PageAccess {
  selectedAutomationId(): string | null;
  selectedAutomation(): Automation | null;
  draftFlows(): ActionFlow[];
  isDirty(): boolean;
  canSave(): boolean;
  warnsIncomplete(): boolean;
  pendingSelection(): Automation | null;
  pendingDelete(): Automation | null;
  actionCount(automation: Automation): number;
  eventName(automation: Automation): string;
  draftEventName(): string;
  deleteMessage(automation: Automation): string;
  select(automation: Automation): void;
  confirmDiscardAndSwitch(): void;
  onFlowsChange(flows: ActionFlow[]): void;
  onValidityChange(validity: { valid: boolean; errors: [] }): void;
  save(): Promise<void>;
  discard(): void;
  setEnabled(enabled: boolean): Promise<void>;
  requestDelete(): void;
  duplicate(): Promise<void>;
}

describe('AutomationsPageComponent', () => {
  let fixture: ComponentFixture<AutomationsPageComponent>;
  let page: PageAccess;
  let automations: jasmine.SpyObj<AutomationService>;
  let toasts: jasmine.SpyObj<ToastService>;
  let stored: Automation[];

  function eventFlow(
    triggerId: string,
    eventId: string | undefined,
    children: unknown[] = [],
    eventName?: string,
  ): ActionFlow {
    return {
      triggerId,
      triggerType: 'onEvent',
      event: eventId ? { providerId: 'obs', eventId, eventName } : undefined,
      children,
    } as ActionFlow;
  }

  function automation(
    id: string,
    name: string,
    flows: ActionFlow[] = [eventFlow('t1', 'scene-changed', [{ id: 'b1' }])],
    enabled = true,
  ): Automation {
    return {
      id,
      name,
      description: '',
      enabled,
      flows,
      createdAt: '2026-07-29T00:00:00Z',
      updatedAt: '2026-07-29T00:00:00Z',
    };
  }

  beforeEach(() => {
    stored = [automation('a', 'Alpha'), automation('b', 'Bravo')];

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());

    automations = jasmine.createSpyObj<AutomationService>(
      'AutomationService',
      [
        'loadAutomations',
        'createAutomation',
        'updateAutomation',
        'setEnabled',
        'duplicateAutomation',
        'deleteAutomation',
      ],
      {
        automations: (() => stored) as unknown as AutomationService['automations'],
        sortedAutomations: (() => stored) as unknown as AutomationService['sortedAutomations'],
        isLoading: (() => false) as unknown as AutomationService['isLoading'],
        loadError: (() => null) as unknown as AutomationService['loadError'],
      },
    );
    automations.loadAutomations.and.resolveTo();
    automations.updateAutomation.and.resolveTo({ success: true, data: stored[0] });
    automations.setEnabled.and.resolveTo({ success: true, data: stored[0] });
    automations.deleteAutomation.and.resolveTo({ success: true, data: undefined });
    automations.duplicateAutomation.and.resolveTo({ success: true, data: automation('c', 'Alpha (copy)') });

    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      imports: [AutomationsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: AutomationService, useValue: automations },
        { provide: ToastService, useValue: toasts },
      ],
    })
      .overrideComponent(AutomationsPageComponent, { set: { template: '', imports: [] } });

    fixture = TestBed.createComponent(AutomationsPageComponent);
    page = fixture.componentInstance as unknown as PageAccess;
  });

  function selectFirst(): void {
    page.select(stored[0]);
  }

  it('copies the stored flow into the draft on selection', () => {
    selectFirst();

    expect(page.selectedAutomationId()).toBe('a');
    expect(page.draftFlows().length).toBe(1);
    expect(page.isDirty()).toBeFalse();
  });

  it('seeds a trigger for an automation that has none, without dirtying it', () => {
    stored = [automation('a', 'Alpha', [])];
    page.select(stored[0]);

    expect(page.draftFlows().length).toBe(1);
    expect(page.draftFlows()[0].triggerType).toBe('onEvent');
    expect(page.draftFlows()[0].children).toEqual([]);
    expect(page.isDirty()).toBeFalse();
  });

  it('keeps only the event flow in the draft', () => {
    const press = { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] } as ActionFlow;
    stored = [automation('a', 'Alpha', [press, eventFlow('t1', 'scene-changed')])];
    page.select(stored[0]);

    expect(page.draftFlows().map(flow => flow.triggerType)).toEqual(['onEvent']);
  });

  it('asks before switching away from unsaved changes', () => {
    selectFirst();
    page.onFlowsChange([]);

    page.select(stored[1]);

    expect(page.pendingSelection()?.id).toBe('b');
    expect(page.selectedAutomationId()).toBe('a');
  });

  it('switches once the discard is confirmed', () => {
    selectFirst();
    page.onFlowsChange([]);
    page.select(stored[1]);

    page.confirmDiscardAndSwitch();

    expect(page.selectedAutomationId()).toBe('b');
    expect(page.isDirty()).toBeFalse();
  });

  it('does not allow saving an invalid flow', () => {
    selectFirst();
    page.onFlowsChange([]);
    page.onValidityChange({ valid: false, errors: [] });

    expect(page.canSave()).toBeFalse();
  });

  it('persists the draft flow on save', async () => {
    selectFirst();
    const edited = [eventFlow('t1', 'scene-changed', [{ id: 'b1' }, { id: 'b2' }])];
    page.onFlowsChange(edited);

    await page.save();

    expect(automations.updateAutomation).toHaveBeenCalledWith('a', { flows: edited });
    expect(page.isDirty()).toBeFalse();
  });

  it('restores the stored flow on discard', () => {
    selectFirst();
    page.onFlowsChange([]);

    page.discard();

    expect(page.draftFlows()).toEqual(stored[0].flows);
    expect(page.isDirty()).toBeFalse();
  });

  it('toggles enabled without touching the unsaved draft', async () => {
    selectFirst();
    page.onFlowsChange([]);

    await page.setEnabled(false);

    expect(automations.setEnabled).toHaveBeenCalledWith('a', false);
    expect(automations.updateAutomation).not.toHaveBeenCalled();
    expect(page.isDirty()).toBeTrue();
  });

  it('reports a failed toggle instead of leaving the switch lying', async () => {
    automations.setEnabled.and.resolveTo({ success: false, error: { code: 'x', message: 'nope' } });
    selectFirst();

    await page.setEnabled(false);

    expect(toasts.show).toHaveBeenCalledWith('nope', { variant: 'error' });
  });

  it('counts the actions of the one trigger for the list and the delete prompt', () => {
    const several = automation('m', 'Several', [
      eventFlow('t1', 'scene-changed', [{ id: 'b1' }, { id: 'b2' }, { id: 'b3' }]),
    ]);

    expect(page.actionCount(several)).toBe(3);
    expect(page.deleteMessage(several)).toContain('3 actions');
  });

  it('singularises the delete prompt for one action', () => {
    expect(page.deleteMessage(stored[0])).toContain('1 action ');
  });

  it('names the event the automation reacts to', () => {
    const named = automation('n', 'Named', [eventFlow('t1', 'scene-changed', [], 'Scene Changed')]);

    expect(page.eventName(named)).toBe('Scene Changed');
  });

  it('says so plainly when no event is picked yet', () => {
    expect(page.eventName(automation('n', 'Named', [eventFlow('t1', undefined)]))).toBe('No event yet');
  });

  it('names the event picked in the draft before it is saved', () => {
    stored = [automation('a', 'Alpha', [eventFlow('t1', undefined)])];
    page.select(stored[0]);
    expect(page.draftEventName()).toBe('No event yet');

    page.onFlowsChange([eventFlow('t1', 'interval', [], 'Every X')]);

    expect(page.draftEventName()).toBe('Every X');
  });

  it('warns when an enabled automation has no event picked', () => {
    stored = [automation('a', 'Alpha', [eventFlow('t1', undefined)])];
    page.select(stored[0]);

    expect(page.warnsIncomplete()).toBeTrue();
  });

  it('warns when an enabled automation has no trigger at all', () => {
    stored = [automation('a', 'Alpha', [])];
    page.select(stored[0]);

    expect(page.warnsIncomplete()).toBeTrue();
  });

  it('does not warn about a disabled automation, which is not meant to run', () => {
    stored = [automation('a', 'Alpha', [], false)];
    page.select(stored[0]);

    expect(page.warnsIncomplete()).toBeFalse();
  });

  it('does not warn once the event is picked', () => {
    selectFirst();

    expect(page.warnsIncomplete()).toBeFalse();
  });

  it('asks before deleting and names what goes with it', () => {
    selectFirst();

    page.requestDelete();

    expect(page.pendingDelete()?.id).toBe('a');
    expect(page.deleteMessage(stored[0])).toContain('deleted permanently');
  });

  it('says a duplicate starts switched off', async () => {
    selectFirst();

    await page.duplicate();

    expect(toasts.show).toHaveBeenCalledWith('Created "Alpha (copy)" - it starts switched off');
  });
});
