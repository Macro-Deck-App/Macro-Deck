import { UiConfigPrimitives } from '@macro-deck/runtime';

export const UI_CHROME_TYPES: ReadonlySet<string> = new Set([
  UiConfigPrimitives.Flow,
  UiConfigPrimitives.Step,
  UiConfigPrimitives.Stack,
  UiConfigPrimitives.Tabs,
  UiConfigPrimitives.Tab,
  UiConfigPrimitives.Heading,
  UiConfigPrimitives.Prose,
  UiConfigPrimitives.Instructions,
  UiConfigPrimitives.Instruction,
  UiConfigPrimitives.CopyValue,
  UiConfigPrimitives.Link,
  UiConfigPrimitives.AdvancedSection,
  UiConfigPrimitives.Divider,
  UiConfigPrimitives.Banner,
  UiConfigPrimitives.ValidationMessage,
  UiConfigPrimitives.Busy,
  UiConfigPrimitives.Button,
  // The three regions of a widget's configuration surface (issue #837): a separate workstream
  // assembles the widget editor shell around them, but wherever this renderer meets them itself -
  // negotiated down to an older client, or nested anywhere else in a tree - they are plain ordered
  // containers, exactly like Stack.
  UiConfigPrimitives.WidgetConfiguration,
  UiConfigPrimitives.WidgetProperties,
  UiConfigPrimitives.WidgetEditor,
]);
