import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActionService } from '../../../services/action.service';
import type { ActionDefinitionModel } from '../../../services/action.service';
import { ActionRunnerComponent } from './action-runner.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function model(id: string, integrationId: string, integrationName: string, name: string): ActionDefinitionModel {
  return { id, integrationId, integrationName, name, description: '', parameters: [] };
}

describe('ActionRunnerComponent', () => {
  let fixture: ComponentFixture<ActionRunnerComponent>;
  let component: ActionRunnerComponent;

  const actions = [
    model('set-volume', 'system', 'System', 'Set Volume'),
    model('mute', 'system', 'System', 'Mute'),
    model('toggle-source', 'obs', 'OBS Studio', 'Toggle Source'),
  ];

  beforeEach(() => {
    const actionServiceSpy = jasmine.createSpyObj<ActionService>(
      'ActionService',
      ['runAction', 'loadActions'],
      { actions: signal(actions), isLoading: signal(false) },
    );

    TestBed.configureTestingModule({
      imports: [ActionRunnerComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting(), { provide: ActionService, useValue: actionServiceSpy }],
    });

    fixture = TestBed.createComponent(ActionRunnerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('heading', 'All actions');
    fixture.componentRef.setInput('integrationId', null);
    fixture.detectChanges();
  });

  it('lists every action sorted by name when no integration is selected', () => {
    expect(component['filteredActions']().map(a => a.name)).toEqual(['Mute', 'Set Volume', 'Toggle Source']);
  });

  it('filters to the selected integration', () => {
    fixture.componentRef.setInput('integrationId', 'system');
    fixture.detectChanges();

    expect(component['filteredActions']().map(a => a.id)).toEqual(['mute', 'set-volume']);
  });

  it('filters by search across name and id', () => {
    component['search'].set('toggle-source');
    expect(component['filteredActions']().map(a => a.id)).toEqual(['toggle-source']);

    component['search'].set('mute');
    expect(component['filteredActions']().map(a => a.id)).toEqual(['mute']);
  });

  it('selects an action and exposes it to the tester', () => {
    expect(component['selectedAction']()).toBeNull();

    component['select'](actions[0]);

    expect(component['selectedAction']()?.id).toBe('set-volume');
  });

  it('drops the selection when the action leaves the filtered list', () => {
    component['select'](actions[0]);

    fixture.componentRef.setInput('integrationId', 'obs');
    fixture.detectChanges();

    expect(component['selectedAction']()).toBeNull();
  });

  it('keys actions by integration and action id', () => {
    expect(component['keyOf'](actions[0])).toBe('system::set-volume');
  });

  it('renders the heading input as the runner title', () => {
    const title = fixture.nativeElement.querySelector('.runner-title') as HTMLElement;
    expect(title.textContent?.trim()).toBe('All actions');
  });

  it('does not leak the heading onto the host as a native title tooltip', () => {
    // A static `title` attribute on <app-action-runner> would both bind the input and stay on
    // the host element, showing a browser tooltip over the whole page; the input is named
    // `heading` precisely to avoid that (issue #233).
    expect(fixture.nativeElement.hasAttribute('title')).toBe(false);
  });
});
