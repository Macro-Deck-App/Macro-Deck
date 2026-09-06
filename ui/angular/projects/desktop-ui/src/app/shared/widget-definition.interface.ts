import { Type, EventEmitter, Signal } from '@angular/core';
import { ActionButtonTriggerType, UiNode, WidgetData, WidgetType } from '@macro-deck/runtime';

export interface IWidgetComponent {
  data: WidgetData;
  width: number;
  height: number;
  disabled: boolean;
  widgetId?: string;
  valueChange?: EventEmitter<Partial<WidgetData>>;
  trigger?: EventEmitter<ActionButtonTriggerType>;
  pressedChange?: EventEmitter<boolean>;
  activateFromInput?: () => void;
  /** Set by a surface that draws the widget's ring itself - see `UiTreeWidgetComponent`. */
  tileDrawsBorder?: boolean;
  /** The rendered tree, for the ring value a host resolves onto its root node. */
  treeRoot?: Signal<UiNode | null>;
}

export interface IWidgetEditorComponent {
  widget: import('@macro-deck/runtime').GridWidget;
  save: import('@angular/core').EventEmitter<Partial<WidgetData>>;
  close: import('@angular/core').EventEmitter<void>;
  valid?: import('@angular/core').Signal<boolean>;
  /**
   * False while the editor still has nothing but placeholder chrome to paint - a config editor is
   * only ready once its UI tree has arrived from the host. The page keeps its loading state up until
   * this turns true, so the editor is never shown half-built. An editor that is ready as soon as it
   * is created omits it.
   */
  ready?: import('@angular/core').Signal<boolean>;
  /**
   * Rebuilds whatever the editor derived from `widget` when the page reseeds it with a draft that came
   * from outside the editor - a JSON-mode edit, a live update adopted mid-edit. An editor that reads
   * `widget` directly on every render has nothing to rebuild and omits it.
   */
  reload?: () => void;
  unsavedChanges?: boolean;
}

export interface IWidgetDefinition {
  type: WidgetType;
  component: Type<IWidgetComponent>;
  loadEditorComponent?: () => Promise<Type<IWidgetEditorComponent>>;
  editorSize?: { width: string; height?: string };
}
