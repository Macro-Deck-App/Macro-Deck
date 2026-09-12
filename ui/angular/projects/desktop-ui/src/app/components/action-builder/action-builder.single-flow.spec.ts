import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import { ActionBuilderComponent } from './action-builder.component';
import { ActionFlowStore } from './services/action-flow.store';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

describe('ActionBuilderComponent single-flow mode', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;
  let store: ActionFlowStore;

  const SCRIPT_TABS = [{ triggerType: 'onRun', label: 'Actions' }];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: fakeApiService() }],
    })
      .overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } })
      .compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    component = fixture.componentInstance;
    store = fixture.debugElement.injector.get(ActionFlowStore);
    fixture.detectChanges();
  });

  it('replaces the widget trigger tabs with the caller-provided ones', () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(item => item.id)).toEqual(['onRun']);
  });

  it('ignores showToggleTriggers while an override is set', () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.componentRef.setInput('showToggleTriggers', true);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(item => item.id)).toEqual(['onRun']);
  });

  it('hides the tab row when a single tab has nothing to switch to', () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.componentRef.setInput('allowEventTriggers', false);
    fixture.detectChanges();

    expect(component.showTabRow()).toBeFalse();
  });

  it('shows a single fixed tab when the caller asks for the row, so its label and Run stay reachable', () => {
    fixture.componentRef.setInput('triggerTabs', [{ triggerType: 'onDoublePress', label: 'Double Tap' }]);
    fixture.componentRef.setInput('allowEventTriggers', false);
    fixture.componentRef.setInput('alwaysShowTabRow', true);
    fixture.detectChanges();

    expect(component.showTabRow()).toBeTrue();
    expect(component.triggerTabItems().map(item => item.label)).toEqual(['Double Tap']);
  });

  it('keeps the tab row when events are still offered', () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.componentRef.setInput('allowEventTriggers', true);
    fixture.detectChanges();

    expect(component.showTabRow()).toBeTrue();
  });

  it('keeps the tab row for widgets, which have several triggers', () => {
    fixture.componentRef.setInput('allowEventTriggers', false);
    fixture.detectChanges();

    expect(component.showTabRow()).toBeTrue();
  });

  it('falls back to the widget tabs when the override is emptied', () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.detectChanges();
    fixture.componentRef.setInput('triggerTabs', []);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(item => item.id)).toContain('onShortPress');
  });

  it('selects the single tab so the action list is never empty-by-selection', async () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.componentRef.setInput('allowEventTriggers', false);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(store.selectedTriggerId()).toBe('onRun');
  });

  it('offers no add-trigger affordance and no per-tab menu under an override (#480)', () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.detectChanges();

    expect(component.showAddTrigger()).toBeFalse();
    expect(component.triggerTabItems().every(item => !item.removable)).toBeTrue();
  });

  it('shows the actions of the stored onRun flow', async () => {
    fixture.componentRef.setInput('triggerTabs', SCRIPT_TABS);
    fixture.componentRef.setInput('allowEventTriggers', false);
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onRun', triggerType: 'onRun', children: [{ id: 'b1' }, { id: 'b2' }] },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.currentChildren().length).toBe(2);
  });
});
