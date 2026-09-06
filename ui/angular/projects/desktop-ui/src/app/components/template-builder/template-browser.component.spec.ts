import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import { TemplateBrowserComponent } from './template-browser.component';

describe('TemplateBrowserComponent', () => {
  let fixture: ComponentFixture<TemplateBrowserComponent>;
  let component: TemplateBrowserComponent;

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables', 'getIntegrations', 'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [TemplateBrowserComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(TemplateBrowserComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  function visiblePanels(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[role="tabpanel"]'))
      .filter(panel => getComputedStyle(panel as Element).display !== 'none')
      .map(panel => (panel as Element).id);
  }

  it('shows only the active tab panel', async () => {
    expect(visiblePanels()).toEqual(['template-browser-panel-variables']);

    component.setActiveTab('filters');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(visiblePanels()).toEqual(['template-browser-panel-filters']);

    component.setActiveTab('control-flow');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(visiblePanels()).toEqual(['template-browser-panel-control-flow']);
  });

  it('emits nothing merely from switching tabs', async () => {
    const emitted: unknown[] = [];
    component.insert.subscribe(value => emitted.push(value));

    component.setActiveTab('filters');
    fixture.detectChanges();
    await fixture.whenStable();
    component.setActiveTab('variables');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(emitted).toEqual([]);
  });
});
