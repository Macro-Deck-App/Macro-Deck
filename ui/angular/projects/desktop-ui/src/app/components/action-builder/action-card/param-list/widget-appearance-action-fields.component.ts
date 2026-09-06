import { ChangeDetectionStrategy, Component, Input, OnChanges, OnInit, SimpleChanges, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ActionBlock, ActionBlockParameter, WIDGET_APPEARANCE_RESET, WIDGET_TARGET_SELF, WidgetBorder, WidgetIconDisplay, WidgetIconRef, iconPackRef, iconPackReferenceOf } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { ColorPickerComponent } from '../../../forms/color-picker/color-picker.component';
import { ParamInputComponent } from '../../../forms/param-input/param-input.component';
import { WidgetBorderControlComponent } from '../../../widget-appearance/widget-border-control.component';
import { WidgetFontAppearanceChange, WidgetFontAppearanceControlComponent } from '../../../widget-appearance/widget-font-appearance-control.component';
import { WidgetIconControlComponent } from '../../../widget-appearance/widget-icon-control.component';
import { WidgetIconDisplayControlComponent } from '../../../widget-appearance/widget-icon-display-control.component';
import { ActionFlowStore } from '../../services/action-flow.store';

type AppearanceKind = 'label' | 'background' | 'labelColor' | 'icon' | 'iconDisplay' | 'font' | 'border';

@Component({
  selector: 'shared-widget-appearance-action-fields',
  standalone: true,
  imports: [FormsModule, ColorPickerComponent, ParamInputComponent, WidgetFontAppearanceControlComponent,
    WidgetBorderControlComponent, WidgetIconControlComponent, WidgetIconDisplayControlComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isSupported()) {
      @switch (kind) {
        @case ('label') {
          <div class="form-group form-group--dense"><label>{{ 'macrodeck.app:ActionBuilder.WidgetAppearance.LabelField' | translate }}</label>
            <shared-param-input [multiline]="true" [rows]="5" [placeholder]="'macrodeck.app:ActionBuilder.WidgetAppearance.ButtonLabelPlaceholder' | translate"
              [value]="stringValue('label')" [variables]="store.pickerVariables()" [scope]="store.previewScope()"
              [scopeRefId]="store.previewScopeRefId()" (valueChange)="write('label', $event)" />
          </div>
        }
        @case ('background') {
          <div class="form-group form-group--dense"><label>{{ 'macrodeck.app:ActionBuilder.WidgetAppearance.ColorField' | translate }}</label><shared-color-picker [ngModel]="stringValue('color')"
            [resetValue]="resetValue" (ngModelChange)="write('color', $event)" /></div>
        }
        @case ('labelColor') {
          <div class="form-group form-group--dense"><label>{{ 'macrodeck.app:ActionBuilder.WidgetAppearance.ColorField' | translate }}</label><shared-color-picker [ngModel]="stringValue('color')"
            [resetValue]="resetValue" (ngModelChange)="write('color', $event)" /></div>
        }
        @case ('icon') {
          <div class="form-group form-group--dense"><label>{{ 'macrodeck.app:ActionBuilder.WidgetAppearance.IconField' | translate }}</label><shared-widget-icon-control [icon]="iconValue()"
            (valueChange)="writeIcon($event)" /></div>
        }
        @case ('iconDisplay') {
          <shared-widget-icon-display-control [actionMode]="true" [display]="iconDisplay"
            [resetActive]="framingReset" (displayChange)="onIconDisplayChange($event)"
            (resetRequested)="onIconDisplayReset()" />
        }
        @case ('font') {
          <shared-widget-font-appearance-control [actionMode]="true" [fontFaceId]="stringValue('fontFaceId')"
            [fontSize]="numberValue('fontSize')" [textAlign]="alignmentValue('textAlign')"
            [labelPosition]="positionValue('labelPosition')" (appearanceChange)="onFontAppearanceChange($event)" />
        }
        @case ('border') {
          <shared-widget-border-control [actionMode]="true" [resetColor]="resetValue" [border]="border"
            (borderChange)="onBorderChange($event)" />
        }
      }
    } @else {
      <p class="unsupported">{{ 'macrodeck.app:ActionBuilder.WidgetAppearance.Unsupported' | translate }}</p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-width: 0; gap: var(--space-3); }
    .form-group { flex: 1 1 0; }
    .unsupported { margin: 0; color: var(--color-text-muted); font-size: var(--text-sm); }
  `,
})
export class WidgetAppearanceActionFieldsComponent implements OnInit, OnChanges {
  @Input({ required: true }) block!: ActionBlock;

  protected readonly store = inject(ActionFlowStore);
  private readonly options = inject(ActionOptionsService);
  protected readonly supportedProperties = signal<Set<string> | null>(null);
  protected readonly resetValue = WIDGET_APPEARANCE_RESET;
  private capabilityRequest = 0;

  protected get kind(): AppearanceKind {
    return WidgetAppearanceActionFieldsComponent.kindFor(this.block.actionId);
  }

  protected get border(): WidgetBorder {
    return { style: this.stringValue('style') as WidgetBorder['style'], color: this.stringValue('color') };
  }

  protected get iconDisplay(): WidgetIconDisplay {
    const fit = this.stringValue('iconFit');
    return {
      fit: (this.framingReset ? undefined : fit || undefined) as WidgetIconDisplay['fit'],
      zoom: this.numberValue('iconZoom'),
      offsetX: this.numberValue('iconOffsetX'),
      offsetY: this.numberValue('iconOffsetY'),
      opacity: this.numberValue('iconOpacity'),
    };
  }

  protected get framingReset(): boolean {
    return this.stringValue('iconFit') === WIDGET_APPEARANCE_RESET;
  }

  ngOnInit(): void {
    void this.resolveCapabilities();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['block'] && !changes['block'].firstChange) void this.resolveCapabilities();
  }

  protected stringValue(name: string): string {
    const value = this.parameter(name)?.value;
    return typeof value === 'string' ? value : '';
  }

  protected write(name: string, value: string | number): void {
    this.store.updateParam(this.block.id, name, value);
  }

  protected iconValue(): WidgetIconRef | undefined {
    const value = this.stringValue('iconId');
    return value ? iconPackRef(value) : undefined;
  }

  protected writeIcon(icon: WidgetIconRef | undefined): void {
    this.write('iconId', iconPackReferenceOf(icon) ?? '');
  }

  protected onBorderChange(border: WidgetBorder): void {
    this.write('style', border.style ?? '');
    this.write('color', border.color ?? '');
  }

  protected numberValue(name: string): number | undefined {
    const value = this.parameter(name)?.value;
    return typeof value === 'number' ? value : undefined;
  }

  protected alignmentValue(name: string): 'left' | 'center' | 'right' | undefined {
    const value = this.stringValue(name);
    return value === 'left' || value === 'center' || value === 'right' ? value : undefined;
  }

  protected positionValue(name: string): 'top' | 'center' | 'bottom' | undefined {
    const value = this.stringValue(name);
    return value === 'top' || value === 'center' || value === 'bottom' ? value : undefined;
  }

  protected onIconDisplayChange(display: WidgetIconDisplay): void {
    this.write('iconFit', display.fit ?? '');
    this.writeFramingNumber('iconZoom', display.zoom);
    this.writeFramingNumber('iconOffsetX', display.offsetX);
    this.writeFramingNumber('iconOffsetY', display.offsetY);
    this.writeFramingNumber('iconOpacity', display.opacity);
  }

  private writeFramingNumber(name: string, value: number | undefined): void {
    const stored = this.parameter(name)?.value;
    if (value === undefined && stored !== undefined && stored !== '' && typeof stored !== 'number') {
      return;
    }
    this.write(name, value ?? '');
  }

  protected onIconDisplayReset(): void {
    this.write('iconFit', WIDGET_APPEARANCE_RESET);
    this.write('iconZoom', '');
    this.write('iconOffsetX', '');
    this.write('iconOffsetY', '');
    this.write('iconOpacity', '');
  }

  protected onFontAppearanceChange(change: WidgetFontAppearanceChange): void {
    this.write(change.field, change.value === undefined ? '' : change.value);
  }

  protected isSupported(): boolean {
    const supported = this.supportedProperties();
    return supported === null || supported.has(WidgetAppearanceActionFieldsComponent.propertyFor(this.kind));
  }

  private parameter(name: string): ActionBlockParameter | undefined {
    return this.block.parameters?.find(parameter => parameter.name === name);
  }

  private async resolveCapabilities(): Promise<void> {
    const target = this.stringValue('widget');
    const ownerId = this.store.previewScopeRefId();
    const lookupTarget = target === WIDGET_TARGET_SELF ? ownerId : target;
    const request = ++this.capabilityRequest;
    if (!lookupTarget) {
      this.supportedProperties.set(null);
      return;
    }
    try {
      const response = await this.options.getOptions({
        integrationId: 'app.macro-deck.widget', actionId: this.block.actionId ?? '', parameterName: 'widget',
      });
      if (request !== this.capabilityRequest) return;
      const properties = response.options.find(option => option.value === lookupTarget)?.metadata?.['appearanceProperties'];
      this.supportedProperties.set(properties ? new Set(properties.split(',')) : null);
    } catch {
      if (request === this.capabilityRequest) this.supportedProperties.set(null);
    }
  }

  private static kindFor(actionId: string | undefined): AppearanceKind {
    switch (actionId) {
      case 'set-background-color': return 'background';
      case 'set-label-color': return 'labelColor';
      case 'set-icon': return 'icon';
      case 'set-icon-display': return 'iconDisplay';
      case 'set-font': return 'font';
      case 'set-border': return 'border';
      default: return 'label';
    }
  }

  private static propertyFor(kind: AppearanceKind): string {
    switch (kind) {
      case 'background': return 'BackgroundColor';
      case 'labelColor': return 'LabelColor';
      case 'icon': return 'Icon';
      case 'iconDisplay': return 'IconDisplay';
      case 'font': return 'Font';
      case 'border': return 'Border';
      default: return 'Label';
    }
  }
}
