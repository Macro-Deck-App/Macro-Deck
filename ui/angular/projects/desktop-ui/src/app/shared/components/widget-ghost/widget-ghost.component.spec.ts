import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { GridWidget, WidgetData, WidgetType } from '@macro-deck/runtime';
import { IWidgetComponent } from '../../widget-definition.interface';
import { WidgetRegistryService } from '../../services/widget-registry.service';
import { ApiService } from '../../transport';
import { UiTreeWidgetComponent } from '../widget-types/ui-tree-widget/ui-tree-widget.component';
import { WidgetGhostComponent } from './widget-ghost.component';

@Component({
  selector: 'shared-test-tree-widget',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div></div>',
})
class TestTreeWidgetComponent implements IWidgetComponent {
  @Input() data: WidgetData = {};
  @Input() width = 0;
  @Input() height = 0;
  @Input() disabled = false;
  @Input() widgetId?: string;
  @Input() ghost = false;
  @Output() pressedChange = new EventEmitter<boolean>();
}

function gridWidget(overrides: Partial<GridWidget> = {}): GridWidget {
  return {
    id: 'w1', folderId: 'f1', x: 0, y: 0, w: 1, h: 1, type: WidgetType.Weather, data: {}, ...overrides,
  };
}

describe('WidgetGhostComponent', () => {
  function createFixture(widget: GridWidget): ComponentFixture<WidgetGhostComponent> {
    const fixture = TestBed.createComponent(WidgetGhostComponent);
    fixture.componentRef.setInput('widget', widget);
    fixture.componentRef.setInput('widthPx', 120);
    fixture.componentRef.setInput('heightPx', 120);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService',
      ['onNotification', 'onUiSessionTreeUpdated', 'onUiSessionPatched', 'onUiSessionInvalidated', 'onUiSessionClosed', 'openWidgetUiSession', 'closeUiSession', 'onWidgetTypeCatalogChanged']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    apiSpy.onUiSessionTreeUpdated.and.callFake(() => new Subject());
    apiSpy.onUiSessionPatched.and.callFake(() => new Subject());
    apiSpy.onUiSessionInvalidated.and.callFake(() => new Subject());
    apiSpy.onUiSessionClosed.and.callFake(() => new Subject());
    apiSpy.openWidgetUiSession.and.returnValue(Promise.resolve({ success: false } as never));
    apiSpy.closeUiSession.and.returnValue(Promise.resolve({ success: true } as never));
    apiSpy.onWidgetTypeCatalogChanged.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    const registry = TestBed.inject(WidgetRegistryService);
    registry.register({
      type: WidgetType.Weather,
      component: TestTreeWidgetComponent,
      loadEditorComponent: () => Promise.resolve(TestTreeWidgetComponent as never),
    });
  });

  it('feeds a tree-rendered widget its own widget id and ghost: true', () => {
    const fixture = createFixture(gridWidget({ id: 'w1', type: WidgetType.Weather }));

    const instance = fixture.debugElement.query(
      element => element.componentInstance instanceof TestTreeWidgetComponent)?.componentInstance as
      TestTreeWidgetComponent;

    expect(instance.widgetId).toBe('w1');
    expect(instance.ghost).toBeTrue();
    fixture.destroy();
  });

  // There is deliberately no "a widget type with no tree session" case left: every widget type has been
  // a host-built tree since #750, which is why the ghost feeds the id unconditionally.
  it('feeds an unregistered widget type its id too, since it draws through the same renderer', () => {
    const fixture = createFixture(gridWidget({ id: 'w2', type: 'com.example.gauge' }));

    const instance = fixture.debugElement.query(
      element => element.componentInstance instanceof UiTreeWidgetComponent)?.componentInstance as
      UiTreeWidgetComponent;

    expect(instance.widgetId).toBe('w2');
    fixture.destroy();
  });
});
