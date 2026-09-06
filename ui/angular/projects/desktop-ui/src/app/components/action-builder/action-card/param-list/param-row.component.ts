import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  forwardRef,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ActionBlock, ActionBlockParameter, AppStrings, ComparisonOperator, EventDefinition, HotkeyValue, KeyboardComboValue, KeyboardSequenceValue, ParameterValue, SecretReference, WIDGET_APPEARANCE_RESET, WIDGET_INTEGRATION_ID, WidgetIconRef, defaultParameterValue, iconPackRef, iconPackReferenceOf, isEventFilterParameter, isEventReference, isHotkeyValue, isRunScriptWidgetTargetParam, isSecretReference, isStateOperator, isVariableReference, variableTokenText } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, InputComponent, LocalizationService, OverlayPanelComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import type { SecretKind, VariableType } from '@macro-deck/runtime';
import { normalizeHttpsUrl } from '../../../../domain/url-input.util';
import { filterOperatorsFor, supportsFilterOperator } from '../../../../domain/event-parameter-refinement.util';
import { comparisonOperatorOptions } from '../../default-action-defs';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { ParamInputComponent } from '../../../forms/param-input/param-input.component';
import { VariablePickerComponent } from '../../../variable-picker/variable-picker.component';
import { ComboboxComponent, ComboboxOption } from '../../../forms/combobox/combobox.component';
import { MultiSelectComponent } from '../../../forms/multi-select/multi-select.component';
import { HotkeyRecorderComponent } from '../../../forms/hotkey-recorder/hotkey-recorder.component';
import { DurationInputComponent } from '../../../forms/duration-input/duration-input.component';
import { DateTimePickerComponent } from '../../../forms/datetime-picker/datetime-picker.component';
import { KeyValueEditorComponent } from '../../../forms/keyvalue-editor/keyvalue-editor.component';
import { CodeEditorComponent } from '../../../forms/code-editor/code-editor.component';
import { SecretInputComponent } from '../../../forms/secret-input/secret-input.component';
import { FilePathInputComponent } from '../../../forms/file-path-input/file-path-input.component';
import { ColorPickerComponent } from '../../../forms/color-picker/color-picker.component';
import { KeyboardSequenceEditorComponent } from '../../../forms/keyboard-sequence-editor/keyboard-sequence-editor.component';
import { KeyboardComboEditorComponent } from '../../../forms/keyboard-combo-editor/keyboard-combo-editor.component';
import { WIDGET_TARGET_SELF, WidgetTargetPickerComponent } from '../../../forms/widget-target-picker/widget-target-picker.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { SelectCaretComponent } from '../../../forms/select-caret/select-caret.component';
import { WidgetIconControlComponent } from '../../../widget-appearance/widget-icon-control.component';

const IMAGE_EXTENSIONS = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'];

@Component({
  selector: 'shared-param-row',
  standalone: true,
  imports: [
    FormsModule,
    ParamInputComponent,
    VariablePickerComponent,
    ComboboxComponent,
    MultiSelectComponent,
    HotkeyRecorderComponent,
    DurationInputComponent,
    DateTimePickerComponent,
    KeyValueEditorComponent,
    CodeEditorComponent,
    SecretInputComponent,
    FilePathInputComponent,
    ColorPickerComponent,
    KeyboardSequenceEditorComponent,
    KeyboardComboEditorComponent,
    WidgetIconControlComponent,
    WidgetTargetPickerComponent,
    InputComponent,
    CheckboxComponent,
    ToggleSwitchComponent,
    SelectComponent,
    SelectCaretComponent,
    ButtonComponent,
    OverlayPanelComponent,
    forwardRef(() => ParamRowComponent),
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './param-row.component.html',
  styleUrls: ['./param-row.component.scss'],
})
export class ParamRowComponent implements OnInit, OnChanges {
  @Input({ required: true }) blockId!: string;
  @Input({ required: true }) param!: ActionBlockParameter;
  @Input() block?: ActionBlock;

  @Input() nested = false;
  @Output() nestedChange = new EventEmitter<ParameterValue>();

  @Input() allowReferences = true;

  @Input() eventId?: string;

  @Input() eventParameterKind: 'configuration' | 'payload' = 'configuration';

  @Input() eventDefinition?: EventDefinition;

  protected readonly store = inject(ActionFlowStore);
  private readonly optionsService = inject(ActionOptionsService);
  private readonly localization = inject(LocalizationService);

  readonly dynOptions = signal<ComboboxOption[] | null>(null);

  private loadedSiblings: string | null = null;
  readonly dynLoading = signal(false);
  readonly dynError = signal<string | null>(null);

  private filterDebounce: ReturnType<typeof setTimeout> | null = null;

  readonly filterOperatorOpen = signal(false);

  protected get filterOperators() {
    const allowed = new Set(filterOperatorsFor(this.eventDefinition, this.param));
    return comparisonOperatorOptions(key => this.localization.translateKey(key))
      .filter(o => allowed.has(o.value));
  }

  ngOnInit(): void {
    if (this.param.type === 'widget-target' && this.stringValue && this.stringValue !== WIDGET_TARGET_SELF) {
      this.loadDynamicOptions();
      return;
    }

    // A dynamic list that already carries a value but no cached label would show the raw value
    // ("current" instead of "Current state", or a Spotify device id instead of its name) until its
    // dropdown is opened once. A stored pick can carry the label with it already - see
    // {@link cacheResolvedLabel} for when it can't - so this only fetches when it's genuinely
    // missing. An autocomplete needs this as much as a choice does, and worse: with no label it
    // seeds its own search with the opaque id and comes back "No suggestions", so the list it would
    // resolve the name from is unreachable (issue #142).
    if (this.isDynamic
      && (this.param.type === 'dynamic-choice' || this.param.type === 'autocomplete')
      && this.stringValue
      && (this.param.required || !this.param.valueLabel)) {
      this.loadDynamicOptions();
    }

    // A multiselect with saved values needs the same resolution, but only once it actually has any -
    // an empty selection has nothing to name and stays local until the dropdown is opened.
    if (this.isDynamic && this.param.type === 'multiselect' && this.arrayValue.length > 0) {
      this.loadDynamicOptions();
    }
  }

  ngOnChanges(): void {
    if (!this.isDynamic || this.dynOptions() === null) return;

    const siblings = this.siblingSignature();
    if (siblings === this.loadedSiblings) return;

    this.loadedSiblings = siblings;
    this.loadDynamicOptions();
  }

  private siblingSignature(): string {
    return JSON.stringify((this.block?.parameters ?? [])
      .filter(sibling => sibling.name !== this.param.name)
      .map(sibling => [sibling.name, sibling.value]));
  }

  private static readonly INLINE_REFERENCE_TYPES = new Set(['string', 'url', 'ipaddress']);

  private get usesInlineReferences(): boolean {
    return this.allowReferences && ParamRowComponent.INLINE_REFERENCE_TYPES.has(this.param.type);
  }

  get isVariable(): boolean {
    if (this.usesInlineReferences) return false;
    return isVariableReference(this.param.value) || isEventReference(this.param.value);
  }

  get variableLabel(): string {
    const v = this.param.value;
    if (isVariableReference(v)) return `{{ vars.${v.$var} }}`;
    if (isEventReference(v)) return `{{ event.${v.$event} }}`;
    return '';
  }

  get primitive(): string | number | boolean | null {
    const v = this.param.value;
    if (isVariableReference(v) || isEventReference(v)) return null;
    if (typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean') return v;
    return null;
  }

  get iconValue(): WidgetIconRef | undefined {
    return this.stringValue ? iconPackRef(this.stringValue) : undefined;
  }

  onIconChange(icon: WidgetIconRef | undefined): void {
    this.onChange(iconPackReferenceOf(icon) ?? '');
  }

  get stringValue(): string {
    const v = this.param.value;
    if (this.usesInlineReferences) {
      if (isVariableReference(v)) return variableTokenText('variable', v.$var);
      if (isEventReference(v)) return variableTokenText('event', v.$event);
    }
    return `${this.primitive ?? ''}`;
  }

  get numberValue(): number {
    const v = this.primitive;
    return typeof v === 'number' ? v : 0;
  }

  get arrayValue(): string[] {
    return Array.isArray(this.param.value)
      ? (this.param.value as unknown[]).filter((v): v is string => typeof v === 'string')
      : [];
  }

  get secretValue(): SecretReference | null {
    return isSecretReference(this.param.value) ? this.param.value : null;
  }

  get secretKind(): SecretKind {
    return this.param.type === 'password' ? 'Password' : 'Secret';
  }

  get hotkeyValue(): HotkeyValue | null {
    return isHotkeyValue(this.param.value) ? this.param.value : null;
  }

  get keyboardComboValue(): KeyboardComboValue | null {
    const v = this.param.value;
    if (v && typeof v === 'object' && !Array.isArray(v)
      && !isVariableReference(v) && !isSecretReference(v) && 'key' in v) {
      return v as unknown as KeyboardComboValue;
    }
    return null;
  }

  get keyboardSequenceValue(): KeyboardSequenceValue | null {
    const v = this.param.value;
    if (v && typeof v === 'object' && !Array.isArray(v)
      && !isVariableReference(v) && !isSecretReference(v) && 'steps' in v) {
      return v as unknown as KeyboardSequenceValue;
    }
    return null;
  }

  get keyValueMap(): Record<string, string> {
    const v = this.param.value;
    if (v && typeof v === 'object' && !Array.isArray(v) && !isVariableReference(v) && !isSecretReference(v)) {
      return v as Record<string, string>;
    }
    return {};
  }

  get objectValue(): Record<string, ParameterValue> {
    const v = this.param.value;
    if (v && typeof v === 'object' && !Array.isArray(v) && !isVariableReference(v)) {
      return v as Record<string, ParameterValue>;
    }
    return {};
  }

  get arrayItems(): ParameterValue[] {
    return Array.isArray(this.param.value) ? (this.param.value as ParameterValue[]) : [];
  }

  get codeLanguage(): string {
    return this.param.type === 'json' ? 'json' : (this.param.language ?? 'text');
  }

  get colorResetValue(): string | undefined {
    return this.param.supportsReset ? WIDGET_APPEARANCE_RESET : undefined;
  }

  get hasSlider(): boolean {
    return this.param.type === 'number'
      && !!this.param.showSlider
      && this.param.min !== undefined
      && this.param.max !== undefined;
  }

  get isDynamic(): boolean {
    return !!this.param.dynamicOptions || !!this.param.optionsSourceId;
  }

  get hasOwnerWidget(): boolean {
    return this.param.allowSelf !== false && this.store.hasOwnerWidget();
  }

  get effectiveOptions(): ComboboxOption[] {
    if (this.isDynamic) {
      return this.dynOptions() ?? [];
    }
    return (this.param.options ?? []).map(o => ({ value: `${o.value}`, label: o.label }));
  }

  get choiceOptions(): SelectOption[] {
    return (this.param.options ?? []).map(o => ({ value: `${o.value}`, label: o.label }));
  }

  get dynamicChoiceOptions(): SelectOption[] {
    const loaded = this.dynOptions();
    if (!loaded) {
      if (!this.stringValue) return [];
      return [{ value: this.stringValue, label: this.param.valueLabel || this.stringValue }];
    }
    return loaded.map(o => ({
      value: o.value,
      label: o.label ?? o.value,
      metadata: o.metadata,
      disabled: o.disabled,
    }));
  }

  get emptyOptionLabel(): string {
    return this.param.placeholder || this.localization.translateKey(AppStrings.ActionBuilder.Param.SelectPlaceholder);
  }

  get comparisonAriaLabel(): string {
    return this.localization.translateKey(AppStrings.ActionBuilder.Param.ComparisonAriaLabel, { label: this.param.label });
  }

  get autocompleteDisplayLabel(): string {
    return this.param.valueLabel ?? this.loadedOptionLabel ?? '';
  }

  get autocompleteOpenFilter(): string {
    return this.autocompleteDisplayLabel ? '' : this.stringValue;
  }

  protected get loadedOptionLabel(): string | undefined {
    if (!this.stringValue) {
      return undefined;
    }

    const match = this.dynOptions()?.find(o => o.value === this.stringValue);
    return match?.label && match.label !== this.stringValue ? match.label : undefined;
  }

  get hasError(): boolean {
    return this.errorMessages.length > 0;
  }

  get errorMessages(): string[] {
    if (this.nested) return [];
    return this.store
      .errorsFor(this.blockId)
      .filter(e => e.paramName === this.param.name)
      .map(e => e.message);
  }

  get showsExternalVariablePicker(): boolean {
    if (!this.allowReferences || this.param.literalOnly) {
      return false;
    }
    switch (this.param.type) {
      case 'number':
      case 'boolean':
      case 'duration':
        return true;
      default:
        return false;
    }
  }

  get acceptedVariableTypes(): VariableType[] | undefined {
    const explicit = this.param.acceptedVariableTypes;
    if (explicit && explicit.length > 0) return explicit;
    switch (this.param.type) {
      case 'string':
      case 'url':
      case 'ipaddress':
      case 'autocomplete':
        return ['text', 'numeric', 'boolean'];
      case 'number':
      case 'duration':
        return ['numeric'];
      case 'boolean':
        return ['boolean'];
      default:
        return ['text'];
    }
  }

  get objectChildren(): Omit<ActionBlockParameter, 'value'>[] {
    return this.param.children ?? [];
  }

  readonly multilineRows = 5;

  onKeyboardSequenceChange(value: KeyboardSequenceValue): void {
    this.onChange(value as unknown as ParameterValue);
  }

  onKeyboardComboChange(value: KeyboardComboValue): void {
    this.onChange(value as unknown as ParameterValue);
  }

  onChange(value: ParameterValue): void {
    if (this.nested) {
      this.nestedChange.emit(value);
      return;
    }
    this.write(value);
  }

  onUrlBlur(value: string): void {
    this.onChange(this.param.autoPrefixHttps ? normalizeHttpsUrl(value) : value);
  }

  private write(value: ParameterValue, valueLabel?: string): void {
    if (this.eventId) {
      this.store.updateEventParam(this.blockId, this.param.name, value, valueLabel);
      return;
    }
    this.store.updateParam(this.blockId, this.param.name, value, valueLabel);
  }

  get showsFilterOperator(): boolean {
    if (!this.eventId || this.nested) return false;
    if (supportsFilterOperator(this.eventDefinition, this.param)) return true;
    if (!this.param.operator || this.param.operator === '==') return false;
    return !!this.eventDefinition && isEventFilterParameter(this.eventDefinition, this.param.name);
  }

  get filterOperator(): ComparisonOperator {
    return this.param.operator ?? '==';
  }

  get isStateFilterOperator(): boolean {
    return this.showsFilterOperator && isStateOperator(this.param.operator);
  }

  get filterOperatorLabel(): string {
    return this.filterOperators.find(o => o.value === this.filterOperator)?.label ?? this.filterOperator;
  }

  toggleFilterOperatorPopover(event: Event): void {
    event.stopPropagation();
    this.filterOperatorOpen.set(!this.filterOperatorOpen());
  }

  closeFilterOperatorPopover(): void {
    this.filterOperatorOpen.set(false);
  }

  pickFilterOperator(op: ComparisonOperator): void {
    this.filterOperatorOpen.set(false);
    this.store.updateEventParamOperator(this.blockId, this.param.name, op);
  }

  onOptionChange(value: ParameterValue): void {
    if (this.nested || typeof value !== 'string') {
      this.onChange(value);
      return;
    }
    this.write(value, this.resolveOptionLabel(value));
  }

  private resolveOptionLabel(value: string): string | undefined {
    if (!value) return undefined;
    const candidates = this.dynOptions()
      ?? (this.param.options ?? []).map(o => ({ value: `${o.value}`, label: o.label }));
    const match = candidates.find(o => o.value === value);
    return match?.label && match.label !== value ? match.label : undefined;
  }

  onPickVariable(variableName: string): void {
    this.onChange({ $var: variableName });
  }

  onPickEventParameter(parameterName: string): void {
    this.onChange({ $event: parameterName });
  }

  onClearVariable(): void {
    this.onChange(defaultParameterValue(this.param));
  }

  loadDynamicOptions(filter?: string): void {
    this.fetchDynamicOptions(filter, false);
  }

  reloadDynamicOptions(): void {
    this.fetchDynamicOptions(undefined, true);
  }

  private fetchDynamicOptions(filter: string | undefined, forceRefresh: boolean): void {
    if (!this.isDynamic) return;
    this.loadedSiblings = this.siblingSignature();
    // A source-id lookup is resolved directly by the host and needs no owning action - the Run
    // Script call site's synthesized `widget` row, or a standalone row with no block at all, has none.
    if (!this.eventId && !this.param.optionsSourceId
      && (!this.block?.integrationId || !this.block.actionId)) return;

    this.dynLoading.set(true);
    this.dynError.set(null);

    const currentParameters: Record<string, unknown> = {};
    for (const sibling of this.block?.parameters ?? []) {
      if (sibling.name !== this.param.name) {
        currentParameters[sibling.name] = sibling.value;
      }
    }
    // The host cannot know the flow's owner widget at authoring time, so `$self` (or an empty
    // target) is resolved to the builder's owner here, the same way `ParamListComponent.targetValue()`
    // resolves it - otherwise a state-options lookup for "this widget" would send the literal
    // sentinel and the host would fall back to its generic vocabulary instead of the widget's own.
    // Resolved only when a `widget` sibling actually names the owner (`$self`/empty) - an explicit
    // target never needs the store asked, which also keeps this a no-op for every non-widget-action
    // parameter list.
    let resolvedOwnerTarget: string | undefined;

    if ('widget' in currentParameters) {
      const widgetTarget = currentParameters['widget'];
      if (widgetTarget === WIDGET_TARGET_SELF || widgetTarget === '' || widgetTarget === undefined) {
        resolvedOwnerTarget = this.store.previewScopeRefId();
        currentParameters['widget'] = resolvedOwnerTarget ?? widgetTarget;
      }
    }

    // Draft states: the host's options endpoint answers from the widget's *persisted* data, so a
    // state just added in the open editor is invisible to it until Save. When the resolved target
    // is the widget currently being edited, the state picker still asks the host (below) - `state`
    // names the parameter on every widget appearance action too (set-icon, set-label, ...), and only
    // the host knows whether this particular action's `state` supports the `current`/`both`
    // sentinels - but the response is then merged with the draft states in place of whatever
    // concrete states the host answered with, so an unsaved state is immediately selectable.
    const draftStates = this.param.name === 'state' && this.block?.integrationId === WIDGET_INTEGRATION_ID
      && resolvedOwnerTarget !== undefined
      ? this.store.previewScopeStates()
      : undefined;

    // A parameter declared by a real action - even a plugin's widget target with a server-side
    // `WidgetTypes` restriction - is still addressed through that action so the host keeps doing the
    // filtering (`ActionParameter.WidgetTarget` sets `optionsSourceId`, so `optionsSourceId` alone
    // cannot tell the two apart). The one exception is the Run Script call site's synthesized
    // `widget` row: `run-script`/`run-remote-script` own the block but declare no such parameter
    // themselves, so it has to go by source id like a standalone row with no block at all.
    const hasOwningAction = !isRunScriptWidgetTargetParam(this.param.name, this.block)
      && !!this.block?.integrationId && !!this.block.actionId;
    const bySourceId = !hasOwningAction && !!this.param.optionsSourceId;

    void this.optionsService
      .loadLabeledOptions({
        integrationId: bySourceId ? '' : (this.block?.integrationId ?? ''),
        actionId: bySourceId ? '' : (this.block?.actionId ?? ''),
        eventId: this.eventId,
        eventParameterKind: this.eventId ? this.eventParameterKind : undefined,
        optionsSourceId: this.param.optionsSourceId,
        widgetTypes: this.param.widgetTypes,
        parameterName: this.param.name,
        filter: filter || undefined,
        currentParameters,
      }, { forceRefresh })
      .then(({ options, error }) => {
        if (error !== undefined) {
          this.dynError.set(error);
          this.dynOptions.set([]);
          return;
        }
        const merged = draftStates && draftStates.length > 0
          ? this.mergeDraftStateOptions(options, draftStates)
          : options;
        this.cacheResolvedLabel(merged);
        this.dynOptions.set(this.withUnavailableRequiredSelection(merged));
      })
      .finally(() => this.dynLoading.set(false));
  }

  private cacheResolvedLabel(options: ComboboxOption[]): void {
    if (this.nested || this.eventId) return;
    const value = this.param.value;
    if (typeof value !== 'string' || !value || value === WIDGET_TARGET_SELF) return;
    if (this.param.valueLabel !== undefined) return;
    const match = options.find(o => o.value === value);
    if (!match?.label || match.label === value) return;
    this.store.cacheParamLabel(this.blockId, this.param.name, value, match.label);
  }

  private withUnavailableRequiredSelection(options: ComboboxOption[]): ComboboxOption[] {
    if (!this.param.required || !this.stringValue || options.some(option => option.value === this.stringValue)) {
      return options;
    }

    const label = this.param.valueLabel || this.stringValue;
    return [{
      value: this.stringValue,
      label: this.localization.translateKey(AppStrings.ActionBuilder.Param.ConfigurationNoLongerAvailable, { label }),
      disabled: true,
    }, ...options];
  }

  private mergeDraftStateOptions(
    hostOptions: ComboboxOption[],
    draftStates: { id: string; label: string }[],
  ): ComboboxOption[] {
    const sentinels = hostOptions.filter(o => o.value === 'current' || o.value === 'both');
    const hasCurrent = sentinels.some(o => o.value === 'current');
    const hasBoth = sentinels.some(o => o.value === 'both');
    if (hasCurrent && !hasBoth && draftStates.length > 1) {
      sentinels.push({
        value: 'both',
        label: this.localization.translateKey(AppStrings.Integrations.Widgets.Actions.EveryStateLabel),
      });
    }
    const draftOptions = draftStates.map(state => ({ value: state.id, label: state.label }));
    return [...sentinels, ...draftOptions];
  }

  onDynamicFilterChange(filter: string): void {
    if (this.filterDebounce) clearTimeout(this.filterDebounce);
    this.filterDebounce = setTimeout(() => this.loadDynamicOptions(filter), 250);
  }

  get browseExtensions(): string[] | undefined {
    if (this.param.type === 'image') return IMAGE_EXTENSIONS;
    return this.param.fileExtensions;
  }

  childParam(child: Omit<ActionBlockParameter, 'value'>): ActionBlockParameter {
    const current = this.objectValue[child.name];
    return {
      ...child,
      value: current !== undefined ? current : defaultParameterValue(child),
    };
  }

  onChildChange(childName: string, value: ParameterValue): void {
    this.onChange({ ...this.objectValue, [childName]: value });
  }

  itemParam(index: number): ActionBlockParameter | null {
    const template = this.param.itemTemplate;
    if (!template) return null;
    return {
      ...template,
      name: `${this.param.name}[${index}]`,
      label: `${template.label || this.param.label} ${index + 1}`,
      value: this.arrayItems[index] ?? defaultParameterValue(template),
    };
  }

  arrayIndices(): number[] {
    return this.arrayItems.map((_, i) => i);
  }

  onItemChange(index: number, value: ParameterValue): void {
    const next = [...this.arrayItems];
    next[index] = value;
    this.onChange(next);
  }

  addItem(): void {
    const template = this.param.itemTemplate;
    if (!template) return;
    this.onChange([...this.arrayItems, defaultParameterValue(template)]);
  }

  removeItem(index: number): void {
    this.onChange(this.arrayItems.filter((_, i) => i !== index));
  }
}
