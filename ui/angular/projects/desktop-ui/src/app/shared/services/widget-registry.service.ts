import { Injectable, Type, inject } from '@angular/core';
import { UiTreeWidgetComponent } from '../components/widget-types/ui-tree-widget/ui-tree-widget.component';
import { IWidgetDefinition, IWidgetComponent, IWidgetEditorComponent } from '../widget-definition.interface';
import { WidgetData, WidgetType, resolveLocalizedText } from '@macro-deck/runtime';
import { LocalizationService } from '../localization/localization.service';
import { WidgetTypeCatalogService } from './widget-type-catalog.service';

function loadWidgetConfigurationEditor(): Promise<Type<IWidgetEditorComponent>> {
  return import(
    '../../components/widgets/widget-editors/widget-configuration-editor/widget-configuration-editor.component'
  ).then(m => m.WidgetConfigurationEditorComponent);
}

@Injectable({ providedIn: 'root' })
export class WidgetRegistryService {
  private readonly localization = inject(LocalizationService);
  private readonly widgetTypes = inject(WidgetTypeCatalogService);
  private readonly definitions = new Map<WidgetType, IWidgetDefinition>();

  register(definition: IWidgetDefinition): void {
    this.definitions.set(definition.type, definition);
  }

  get(type: WidgetType): IWidgetDefinition | undefined {
    return this.definitions.get(type);
  }

  getAll(): IWidgetDefinition[] {
    return Array.from(this.definitions.values());
  }

  getComponent(type: WidgetType): Type<IWidgetComponent> {
    return this.definitions.get(type)?.component ?? UiTreeWidgetComponent;
  }

  async getEditorComponent(type: WidgetType): Promise<Type<IWidgetEditorComponent>> {
    const info = await this.widgetTypes.infoFor(type);
    if (info?.supportsConfigUi) return loadWidgetConfigurationEditor();

    const definition = this.definitions.get(type);
    return definition?.loadEditorComponent?.() ?? loadWidgetConfigurationEditor();
  }

  getDefaultData(type: WidgetType): WidgetData | undefined {
    const entry = this.widgetTypes.types().find(t => t.id === type);
    return entry ? structuredClone(entry.defaultData) as WidgetData : undefined;
  }

  getWidgetTypeName(type: WidgetType): string {
    const entry = this.widgetTypes.types().find(t => t.id === type);
    return (entry && resolveLocalizedText(entry.name, this.localization)) || type;
  }
}
