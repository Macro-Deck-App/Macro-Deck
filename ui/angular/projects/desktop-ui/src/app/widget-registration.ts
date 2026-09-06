import { EnvironmentProviders, makeEnvironmentProviders, APP_INITIALIZER } from '@angular/core';
import { WidgetType } from '@macro-deck/runtime';
import {
  WidgetRegistryService,
  UiTreeWidgetComponent,
} from '@shared';

function registerWidgets(registry: WidgetRegistryService): () => void {
  return () => {

    registry.register({
      type: WidgetType.ActionButton,
      component: UiTreeWidgetComponent,
      editorSize: { width: 'min(1400px, 95vw)', height: 'min(820px, 90vh)' },
    });

    registry.register({
      type: WidgetType.MusicPlayer,
      component: UiTreeWidgetComponent,
    });

    registry.register({
      type: WidgetType.Slider,
      component: UiTreeWidgetComponent,
    });

    registry.register({
      type: WidgetType.Weather,
      component: UiTreeWidgetComponent,
    });

    registry.register({
      type: WidgetType.HistoryGraph,
      component: UiTreeWidgetComponent,
    });

    registry.register({
      type: WidgetType.Clock,
      component: UiTreeWidgetComponent,
    });
  };
}

export function provideWidgetRegistry(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: APP_INITIALIZER,
      useFactory: registerWidgets,
      deps: [WidgetRegistryService],
      multi: true
    }
  ]);
}
