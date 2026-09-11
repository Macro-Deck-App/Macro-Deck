import { ChangeDetectionStrategy, Component, DestroyRef, computed, forwardRef, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { UiConfigEvents, UiConfigPrimitives, UiConfigProperties, UiNode, UiNodeOption, WidgetIconDisplay, WidgetIconRef, emitsEvent, iconPackRef, iconPackReferenceOf, isIconPackRef, nodeBoolean, nodeNumber, nodeOptions, nodeRaw, nodeString, nodeStringArray, nodeText, readWidgetIconRef } from '@macro-deck/runtime';
import { ButtonComponent, IconImageService, InputComponent, LocalizationService, SegmentedControlComponent, SegmentedOption, ToggleSwitchComponent, TranslatePipe } from '@shared';
import type { ActionFlow, HotkeyValue, KeyboardComboValue, KeyboardSequenceValue, VariableType } from '@macro-deck/runtime';
import { normalizeHttpsUrl } from '../../domain/url-input.util';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { ComboboxComponent, ComboboxOption } from '../forms/combobox/combobox.component';
import { MultiSelectComponent, MultiSelectOption } from '../forms/multi-select/multi-select.component';
import { ReorderableListComponent } from '../forms/reorderable-list/reorderable-list.component';
import { ColorPickerComponent } from '../forms/color-picker/color-picker.component';
import { FilePathInputComponent, FilePathKind } from '../forms/file-path-input/file-path-input.component';
import { HotkeyRecorderComponent } from '../forms/hotkey-recorder/hotkey-recorder.component';
import { DurationInputComponent } from '../forms/duration-input/duration-input.component';
import { DateTimePickerComponent } from '../forms/datetime-picker/datetime-picker.component';
import { CodeEditorComponent } from '../forms/code-editor/code-editor.component';
import { KeyValueEditorComponent } from '../forms/keyvalue-editor/keyvalue-editor.component';
import { KeyboardComboEditorComponent } from '../forms/keyboard-combo-editor/keyboard-combo-editor.component';
import { KeyboardSequenceEditorComponent } from '../forms/keyboard-sequence-editor/keyboard-sequence-editor.component';
import { WidgetIconControlComponent } from '../widget-appearance/widget-icon-control.component';
import { WidgetIconDisplayControlComponent } from '../widget-appearance/widget-icon-display-control.component';
import { WidgetTargetPickerComponent } from '../forms/widget-target-picker/widget-target-picker.component';
import { DevicePickerComponent } from '../forms/device-picker/device-picker.component';
import { IntegrationPickerComponent } from '../forms/integration-picker/integration-picker.component';
import { NodeVariablePickerComponent } from '../forms/variable-picker-field/variable-picker-field.component';
import { NodeParamInputComponent } from '../forms/param-input-field/param-input-field.component';
import { NodeActionBuilderComponent } from '../action-builder/node-action-builder.component';
import { NodeActionPickerComponent } from '../action-builder/action-picker/node-action-picker.component';
import { NodeStateMappingEditorComponent, type StateMappingValue } from '../widgets/state-mapping/node-state-mapping-editor.component';
import { UiRenderContext } from './ui-render-context';
import { UiNodeComponent } from './ui-node.component';

const Primitives = UiConfigPrimitives;
const Properties = UiConfigProperties;

@Component({
  selector: 'shared-ui-input',
  standalone: true,
  imports: [
    FormsModule,
    TranslatePipe,
    forwardRef(() => UiNodeComponent),
    ButtonComponent,
    InputComponent,
    ToggleSwitchComponent,
    SegmentedControlComponent,
    SelectComponent,
    ComboboxComponent,
    MultiSelectComponent,
    ReorderableListComponent,
    ColorPickerComponent,
    FilePathInputComponent,
    HotkeyRecorderComponent,
    DurationInputComponent,
    DateTimePickerComponent,
    CodeEditorComponent,
    KeyValueEditorComponent,
    KeyboardComboEditorComponent,
    KeyboardSequenceEditorComponent,
    WidgetIconControlComponent,
    WidgetIconDisplayControlComponent,
    WidgetTargetPickerComponent,
    DevicePickerComponent,
    IntegrationPickerComponent,
    NodeVariablePickerComponent,
    NodeParamInputComponent,
    NodeActionPickerComponent,
    // `NodeActionBuilderComponent` wraps `ActionBuilderComponent`, which renders a parameter's own
    // descriptive UI through `shared-ui-tree` (`ParamListComponent`) - closing a real cycle back to
    // this module, deferred like `UiNodeComponent` above rather than dereferenced at this file's own
    // load time.
    forwardRef(() => NodeActionBuilderComponent),
    NodeStateMappingEditorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-input.component.html',
  styleUrls: ['./ui-render.component.scss'],
})
export class UiInputComponent {
  readonly node = input.required<UiNode>();

  protected readonly types = Primitives;
  protected readonly context = inject(UiRenderContext);
  private readonly localization = inject(LocalizationService);
  private readonly iconImage = inject(IconImageService);

  protected readonly label = computed(() => nodeText(this.node(), Properties.Label, this.localization));
  protected readonly hideLabel = computed(() => nodeBoolean(this.node(), Properties.HideLabel) === true);
  protected readonly visibleLabel = computed(() => (this.hideLabel() ? null : this.label()));
  protected readonly description = computed(() => nodeText(this.node(), Properties.Description, this.localization));
  protected readonly placeholder = computed(() => nodeText(this.node(), Properties.Placeholder, this.localization) ?? '');
  protected readonly required = computed(() => nodeBoolean(this.node(), Properties.Required) === true);
  protected readonly maxLength = computed(() => nodeNumber(this.node(), Properties.MaxLength) ?? null);
  protected readonly min = computed(() => nodeNumber(this.node(), Properties.Min) ?? null);
  protected readonly max = computed(() => nodeNumber(this.node(), Properties.Max) ?? null);
  protected readonly step = computed(() => nodeNumber(this.node(), Properties.Step) ?? null);
  protected readonly multiline = computed(() => nodeBoolean(this.node(), Properties.Multiline) === true);
  protected readonly literalOnly = computed(() => nodeBoolean(this.node(), Properties.LiteralOnly) === true);
  protected readonly showSlider = computed(
    () => nodeBoolean(this.node(), Properties.ShowSlider) === true && this.min() !== null && this.max() !== null,
  );
  protected readonly allowsCustomValue = computed(
    () => nodeBoolean(this.node(), Properties.AllowsCustomValue) === true,
  );
  protected readonly segmented = computed(() => nodeBoolean(this.node(), Properties.Segmented) === true);
  protected readonly cards = computed(() => nodeBoolean(this.node(), Properties.Cards) === true);
  protected readonly reorderable = computed(() => nodeBoolean(this.node(), Properties.Reorderable) === true);

  protected readonly toggleRow = computed(
    () => this.node().type === UiConfigPrimitives.Boolean && !this.segmented() && !this.hideLabel(),
  );
  protected readonly booleanFalseLabel = computed(
    () => nodeText(this.node(), Properties.FalseLabel, this.localization) ?? '',
  );
  protected readonly booleanTrueLabel = computed(
    () => nodeText(this.node(), Properties.TrueLabel, this.localization) ?? '',
  );
  protected readonly booleanSegmentedOptions = computed<SegmentedOption[]>(() => [
    { value: 'false', label: this.booleanFalseLabel() },
    { value: 'true', label: this.booleanTrueLabel() },
  ]);
  protected readonly loading = computed(() => {
    const properties = this.node().properties ?? {};
    return Properties.Loading in properties && properties[Properties.Loading] !== false;
  });
  protected readonly autoPrefixHttps = computed(
    () => nodeBoolean(this.node(), Properties.AutoPrefixHttps) === true,
  );
  protected readonly fileExtensions = computed(() => nodeStringArray(this.node(), Properties.FileExtensions));
  protected readonly language = computed(() => nodeString(this.node(), Properties.Language));

  protected readonly supportsReset = computed(() => nodeBoolean(this.node(), Properties.SupportsReset) === true);
  protected readonly colorDefaultValue = computed(() =>
    this.supportsReset() ? (nodeString(this.node(), Properties.DefaultValue) ?? '') : undefined,
  );

  protected readonly disabled = computed(
    () => nodeBoolean(this.node(), Properties.Disabled) === true || this.context.disabled,
  );

  protected readonly invalid = computed(() => nodeBoolean(this.node(), Properties.Invalid) === true);
  protected readonly ariaInvalid = computed<'true' | null>(() => (this.invalid() ? 'true' : null));

  protected readonly errorEntries = computed(() => {
    const node = this.node();
    const entries: { id: string; text: string }[] = [];

    const validationMessage = nodeText(node, Properties.ValidationMessage, this.localization);
    if (validationMessage) entries.push({ id: `${node.id}-validation`, text: validationMessage });

    for (const message of this.context.messagesFor(node.id)) entries.push(message);

    const optionsError = nodeText(node, Properties.Error, this.localization);
    if (optionsError) entries.push({ id: `${node.id}-options-error`, text: optionsError });

    return entries;
  });

  protected readonly descriptionId = computed(() => (this.description() ? `${this.node().id}-description` : null));

  protected readonly ariaDescribedby = computed(() => {
    const ids = [this.descriptionId(), ...this.errorEntries().map(entry => entry.id)].filter(
      (id): id is string => !!id,
    );
    return ids.length ? ids.join(' ') : null;
  });

  protected readonly canReload = computed(() => emitsEvent(this.node(), UiConfigEvents.Reload));
  protected readonly canAdd = computed(() => emitsEvent(this.node(), UiConfigEvents.Add));
  protected readonly canRemove = computed(() => emitsEvent(this.node(), UiConfigEvents.Remove));

  protected readonly numberOptions = computed<SelectOption[]>(() =>
    this.node().type === UiConfigPrimitives.Number
      ? (nodeOptions(this.node(), this.localization) ?? []).map(toSelectOption)
      : [],
  );
  protected readonly numberOptionValue = computed(() => {
    const value = this.numberValue();
    return value === null || value === undefined ? '' : String(value);
  });

  protected readonly selectOptions = computed<SelectOption[]>(() =>
    (nodeOptions(this.node(), this.localization) ?? []).map(toSelectOption),
  );
  protected readonly comboboxOptions = computed<ComboboxOption[]>(() =>
    (nodeOptions(this.node(), this.localization) ?? []).map(toComboboxOption),
  );
  protected readonly cardOptions = computed(() =>
    (nodeOptions(this.node(), this.localization) ?? []).map(option => ({
      value: option.value,
      label: option.label ?? option.value,
      description: option.description ?? '',
    })),
  );
  protected readonly segmentedOptions = computed<SegmentedOption[]>(() =>
    (nodeOptions(this.node(), this.localization) ?? []).map(toSegmentedOption),
  );
  protected readonly multiSelectOptions = computed<MultiSelectOption[]>(() =>
    (nodeOptions(this.node(), this.localization) ?? []).map(toComboboxOption),
  );

  protected readonly stringValue = computed(() => {
    const value = this.context.getValue(this.node());
    return typeof value === 'string' ? value : value == null ? '' : String(value);
  });

  protected readonly iconValue = computed<WidgetIconRef | undefined>(() => {
    const value = this.rawValue();
    if (typeof value === 'string') return value ? iconPackRef(value) : undefined;
    return readWidgetIconRef(value, undefined);
  });

  protected readonly iconEditable = computed(() => {
    const icon = this.iconValue();
    return icon === undefined || isIconPackRef(icon);
  });

  protected onIconChange(icon: WidgetIconRef | undefined): void {
    const wasBareString = typeof this.rawValue() === 'string';
    this.onChange(wasBareString ? (iconPackReferenceOf(icon) ?? '') : (icon ?? null));
  }

  // --- icon-display: the framing being edited is the node's own value; the icon it previews
  // against, the aspect ratio and the background it is framed against are properties, since none of
  // them are part of the framing itself (issue #837).
  protected readonly iconDisplayValue = computed<WidgetIconDisplay | undefined>(() => {
    const value = this.rawValue();
    return value !== null && typeof value === 'object' && !Array.isArray(value)
      ? (value as WidgetIconDisplay)
      : undefined;
  });
  protected readonly iconDisplayIcon = computed<WidgetIconRef | undefined>(
    () => readWidgetIconRef(nodeRaw(this.node(), Properties.Icon), undefined),
  );
  protected readonly iconDisplayAspectRatio = computed(() => nodeNumber(this.node(), Properties.AspectRatio) ?? 1);
  protected readonly iconDisplayBackground = computed(() => nodeString(this.node(), Properties.Background));
  protected readonly iconDisplayIconUrl = computed(
    () => this.iconImage.getIconUrl(iconPackReferenceOf(this.iconDisplayIcon()), 128),
  );

  protected onIconDisplayChange(display: WidgetIconDisplay): void {
    this.onChange(display);
  }

  protected onIconDisplayReset(): void {
    this.onChange(null);
  }

  protected readonly numberValue = computed(() => {
    const value = this.context.getValue(this.node());
    return typeof value === 'number' ? value : null;
  });

  protected readonly booleanValue = computed(() => this.context.getValue(this.node()) === true);

  protected readonly arrayValue = computed(() => {
    const value = this.context.getValue(this.node());
    return Array.isArray(value) ? (value as string[]) : [];
  });

  protected readonly recordValue = computed(() => {
    const value = this.context.getValue(this.node());
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, string>) : {};
  });

  protected readonly rawValue = computed(() => this.context.getValue(this.node()));

  protected readonly hotkeyValue = computed(() => (this.rawValue() ?? null) as HotkeyValue | null);
  protected readonly keyboardComboValue = computed(() => (this.rawValue() ?? null) as KeyboardComboValue | null);
  protected readonly keyboardSequenceValue = computed(
    () => (this.rawValue() ?? null) as KeyboardSequenceValue | null,
  );

  // --- actions-list-editor: `NodeActionBuilderComponent` sources the client-owned action catalogue
  // and variable list itself (issue #837); this only reads what the node's own properties carry
  // (`triggers` - a flat list of trigger-type ids, not labelled tabs - and `canRun`) and feeds the
  // flow list back out as the node's own value.
  protected readonly actionFlowsValue = computed<ActionFlow[]>(() => {
    const value = this.rawValue();
    return Array.isArray(value) ? (value as ActionFlow[]) : [];
  });
  protected readonly actionTriggers = computed(() => nodeStringArray(this.node(), Properties.Triggers));
  protected readonly canRunFlows = computed(() => nodeBoolean(this.node(), Properties.CanRun) === true);

  // Absent, not empty, when the node carries no states at all: an empty draft list is itself an answer
  // ("this widget has one appearance"), so a node that says nothing must not be read as saying that.
  protected readonly actionStateOptions = computed<UiNodeOption[] | undefined>(() =>
    Array.isArray(nodeRaw(this.node(), Properties.States)) ? this.nodeStateOptions() : undefined);

  protected onActionFlowsChange(flows: ActionFlow[]): void {
    this.onChange(flows);
  }

  // --- state-mapping-editor: `NodeStateMappingEditorComponent` sources the client-owned variable
  // list itself (issue #837), the same split `actions-list-editor` makes above; this only reads what
  // the node's own value and `states` property carry. `nodeStateOptions` is shared with
  // `actions-list-editor`, which carries the same property for its own state pickers.
  protected readonly stateMappingValue = computed<StateMappingValue | undefined>(() => {
    const value = this.rawValue();
    return isPlainRecord(value) && Array.isArray(value['rules']) && typeof value['fallbackStateId'] === 'string'
      ? (value as unknown as StateMappingValue)
      : undefined;
  });
  protected readonly nodeStateOptions = computed<UiNodeOption[]>(() => {
    const raw = nodeRaw(this.node(), Properties.States);
    if (!Array.isArray(raw)) return [];
    return raw
      .filter((item): item is Record<string, unknown> => isPlainRecord(item) && typeof item['value'] === 'string')
      .map(item => ({
        value: item['value'] as string,
        label: typeof item['label'] === 'string' ? item['label'] : undefined,
      }));
  });

  protected onStateMappingChange(value: StateMappingValue): void {
    this.onChange(value);
  }

  // --- variable-picker: the actual `VariableService` lookup and `writableOnly` filtering live in
  // `NodeVariablePickerComponent`, not here - see that component's doc comment for why.
  protected readonly variableTypesFilter = computed<VariableType[] | undefined>(() => {
    const types = nodeStringArray(this.node(), Properties.VariableTypes);
    return types && types.length > 0 ? (types as VariableType[]) : undefined;
  });
  protected readonly writableOnly = computed(() => nodeBoolean(this.node(), Properties.WritableOnly) === true);

  // --- integration-picker
  protected readonly integrationCapability = computed(() => nodeString(this.node(), Properties.Capability));
  protected readonly integrationConfigurationEntries = computed(
    () => nodeBoolean(this.node(), Properties.ConfigurationEntries) === true,
  );

  private filterTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.filterTimer));
  }

  protected fileKind(): FilePathKind {
    return this.node().type as FilePathKind;
  }

  protected onChange(value: unknown): void {
    this.context.setValue(this.node(), value);
  }

  protected onNumberChange(value: string | number): void {
    if (value === '' || value == null) return;
    const parsed = Number(value);
    if (Number.isFinite(parsed)) this.onChange(parsed);
  }

  protected onBooleanSegmentedChange(value: string): void {
    this.onChange(value === 'true');
  }

  protected onUrlBlur(): void {
    if (!this.autoPrefixHttps()) return;
    this.onChange(normalizeHttpsUrl(this.stringValue()));
  }

  protected onOpen(): void {
    this.context.emit(this.node(), UiConfigEvents.Open);
  }

  protected onFilter(text: string): void {
    const debounce = nodeNumber(this.node(), Properties.FilterDebounceMs) ?? 0;
    if (this.filterTimer) clearTimeout(this.filterTimer);
    if (debounce <= 0) {
      this.context.emit(this.node(), UiConfigEvents.Filter, text);
      return;
    }
    this.filterTimer = setTimeout(() => this.context.emit(this.node(), UiConfigEvents.Filter, text), debounce);
  }

  protected onReload(): void {
    this.context.emit(this.node(), UiConfigEvents.Reload);
  }

  protected onAdd(): void {
    this.context.emit(this.node(), UiConfigEvents.Add);
  }

  protected onRemove(itemId: string): void {
    this.context.emit(this.node(), UiConfigEvents.Remove, itemId);
  }

  protected rawOf(key: string): unknown {
    return nodeRaw(this.node(), key);
  }
}

function toSelectOption(option: UiNodeOption): SelectOption {
  const selectOption: SelectOption = { value: option.value, label: option.label ?? option.value };
  if (option.badge) selectOption.badge = option.badge;
  return selectOption;
}

function toComboboxOption(option: UiNodeOption): ComboboxOption {
  return { value: option.value, label: option.label ?? option.value };
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function toSegmentedOption(option: UiNodeOption): SegmentedOption {
  // An icon renders instead of the text - matching the original alignment/position pickers' plain icon
  // buttons - but the label (or the value, when no label was authored) still names the option for anyone
  // not reading the icon, via the segmented control's own `ariaLabel` fallback.
  return option.icon
    ? { value: option.value, icon: option.icon, ariaLabel: option.label ?? option.value }
    : { value: option.value, label: option.label ?? option.value };
}
