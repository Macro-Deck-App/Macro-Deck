import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ActionBlock, WIDGET_APPEARANCE_RESET } from '@macro-deck/runtime';
import { ApiService, IconImageService } from '@shared';
import { WidgetFontAppearanceControlComponent } from '../../../widget-appearance/widget-font-appearance-control.component';
import { WidgetIconDisplayControlComponent } from '../../../widget-appearance/widget-icon-display-control.component';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { FontService } from '../../../../services/font.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { WidgetAppearanceActionFieldsComponent } from './widget-appearance-action-fields.component';

describe('WidgetAppearanceActionFieldsComponent', () => {
  let fixture: ComponentFixture<WidgetAppearanceActionFieldsComponent>;
  let options: jasmine.SpyObj<ActionOptionsService>;
  let ownerId: string | undefined;
  let updateParam: jasmine.Spy;

  const block = (actionId: string, target = 'clock'): ActionBlock => ({
    id: 'action-1', type: 'action', blockType: `app.macro-deck.widget.${actionId}`, label: actionId, color: '',
    integrationId: 'app.macro-deck.widget', actionId,
    parameters: [
      { name: 'widget', type: 'widget-target', label: 'Widget', value: target },
      { name: 'state', type: 'dynamic-choice', label: 'State', value: 'current' },
      { name: 'style', type: 'choice', label: 'Style', value: '' },
      { name: 'color', type: 'color', label: 'Color', value: '' },
    ],
  });

  beforeEach(async () => {
    ownerId = undefined;
    updateParam = jasmine.createSpy('updateParam');
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);
    options.getOptions.and.resolveTo({ options: [], allowsCustomValue: false });

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    await TestBed.configureTestingModule({
      imports: [WidgetAppearanceActionFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        { provide: FontService, useValue: { families: signal([]), loadSystemFonts: () => Promise.resolve(),
          faceById: () => undefined, facesForFamily: () => [] } },
        { provide: IconImageService, useValue: { getIconUrl: () => null } },
        { provide: ActionFlowStore, useValue: { updateParam, pickerVariables: () => [],
          previewScope: () => 'global', previewScopeRefId: () => ownerId } },
        { provide: ApiService, useValue: apiSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WidgetAppearanceActionFieldsComponent);
  });

  it('uses the shared border control and keeps its explicit Unchanged style', async () => {
    options.getOptions.and.resolveTo({
      options: [{ value: 'clock', metadata: { appearanceProperties: 'Border,BorderColor' } }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('block', block('set-border'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-border-control')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Unchanged');
    expect(fixture.nativeElement.textContent).toContain('Color');
  });

  it('hides a known unsupported appearance setting instead of rendering a dead control', async () => {
    options.getOptions.and.resolveTo({
      options: [{ value: 'clock', metadata: { appearanceProperties: 'Border,BorderColor' } }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('block', block('set-icon'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-icon-control')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('does not support this appearance setting');
  });

  it('resolves This widget through its owner and hides an unsupported appearance setting', async () => {
    ownerId = 'clock';
    options.getOptions.and.resolveTo({
      options: [{ value: 'clock', metadata: { appearanceProperties: 'Border,BorderColor' } }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('block', block('set-icon', '$self'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(options.getOptions).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-widget-icon-control')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('does not support this appearance setting');
  });

  it('remains permissive for This widget when its owner cannot be resolved', async () => {
    fixture.componentRef.setInput('block', block('set-icon', '$self'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(options.getOptions).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('shared-widget-icon-control')).not.toBeNull();
  });

  it('remains permissive when capability lookup fails', async () => {
    options.getOptions.and.rejectWith(new Error('transport failed'));
    fixture.componentRef.setInput('block', block('set-icon'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-icon-control')).not.toBeNull();
  });

  it('does not let a delayed capability response replace a newer target selection', async () => {
    let resolveFirst!: (value: { options: Array<{ value: string; metadata: Record<string, string> }>;
      allowsCustomValue: boolean }) => void;
    const first = new Promise<{ options: Array<{ value: string; metadata: Record<string, string> }>;
      allowsCustomValue: boolean }>(resolve => { resolveFirst = resolve; });
    options.getOptions.and.returnValues(
      first,
      Promise.resolve({ options: [{ value: 'weather', metadata: { appearanceProperties: 'Icon,Border,BorderColor' } }],
        allowsCustomValue: false }),
    );
    fixture.componentRef.setInput('block', block('set-icon', 'clock'));
    fixture.detectChanges();
    fixture.componentRef.setInput('block', block('set-icon', 'weather'));
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    resolveFirst({ options: [{ value: 'clock', metadata: { appearanceProperties: 'Border,BorderColor' } }],
      allowsCustomValue: false });
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-icon-control')).not.toBeNull();
  });

  it('offers the framing controls for a widget that renders a framed icon', async () => {
    options.getOptions.and.resolveTo({
      options: [{ value: 'button', metadata: { appearanceProperties: 'Icon,IconDisplay,Border' } }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('block', block('set-icon-display', 'button'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-icon-display-control')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Unchanged');
  });

  it('hides the framing controls for a widget that renders an icon but does not frame it', async () => {
    options.getOptions.and.resolveTo({
      options: [{ value: 'slider', metadata: { appearanceProperties: 'Icon,Border,BorderColor' } }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('block', block('set-icon-display', 'slider'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-icon-display-control')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('does not support this appearance setting');
  });

  it('writes one parameter per framing field, named as the action declares them', async () => {
    const displayBlock = block('set-icon-display', 'button');
    displayBlock.parameters!.push(
      { name: 'iconFit', type: 'choice', label: 'Display', value: '' },
      { name: 'iconZoom', type: 'number', label: 'Zoom', value: '' },
      { name: 'iconOffsetX', type: 'number', label: 'X', value: '' },
      { name: 'iconOffsetY', type: 'number', label: 'Y', value: '' },
      { name: 'iconOpacity', type: 'number', label: 'Opacity', value: '' },
    );
    fixture.componentRef.setInput('block', displayBlock);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const control = fixture.debugElement.query(By.directive(WidgetIconDisplayControlComponent))
      .componentInstance as WidgetIconDisplayControlComponent;
    control.displayChange.emit({ fit: 'cover', offsetX: -15 });

    expect(updateParam.calls.allArgs()).toEqual([
      ['action-1', 'iconFit', 'cover'],
      ['action-1', 'iconZoom', ''],
      ['action-1', 'iconOffsetX', -15],
      ['action-1', 'iconOffsetY', ''],
      ['action-1', 'iconOpacity', ''],
    ]);
  });

  it('resets through the sentinel and leaves no value behind for the clear to discard', async () => {
    const displayBlock = block('set-icon-display', 'button');
    displayBlock.parameters!.push(
      { name: 'iconFit', type: 'choice', label: 'Display', value: 'cover' },
      { name: 'iconZoom', type: 'number', label: 'Zoom', value: 300 },
      { name: 'iconOffsetX', type: 'number', label: 'X', value: '' },
      { name: 'iconOffsetY', type: 'number', label: 'Y', value: '' },
      { name: 'iconOpacity', type: 'number', label: 'Opacity', value: '' },
    );
    fixture.componentRef.setInput('block', displayBlock);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const control = fixture.debugElement.query(By.directive(WidgetIconDisplayControlComponent))
      .componentInstance as WidgetIconDisplayControlComponent;
    control.resetRequested.emit();

    expect(updateParam.calls.allArgs()).toEqual([
      ['action-1', 'iconFit', WIDGET_APPEARANCE_RESET],
      ['action-1', 'iconZoom', ''],
      ['action-1', 'iconOffsetX', ''],
      ['action-1', 'iconOffsetY', ''],
      ['action-1', 'iconOpacity', ''],
    ]);
  });

  it('keeps every font field explicitly unchanged until the user selects a value', async () => {
    const fontBlock = block('set-font', 'clock');
    fontBlock.parameters!.push(
      { name: 'fontFaceId', type: 'autocomplete', label: 'Font', value: '' },
      { name: 'fontSize', type: 'number', label: 'Size', value: 0 },
      { name: 'textAlign', type: 'choice', label: 'Align', value: '' },
      { name: 'labelPosition', type: 'choice', label: 'Position', value: '' },
    );
    fixture.componentRef.setInput('block', fontBlock);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-widget-font-appearance-control')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Leave alignment unchanged"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Leave position unchanged"]')).not.toBeNull();
    expect(updateParam).not.toHaveBeenCalled();
  });

  it('writes a single fontFaceId parameter when a face is chosen', async () => {
    const fontBlock = block('set-font', 'clock');
    fontBlock.parameters!.push(
      { name: 'fontFaceId', type: 'autocomplete', label: 'Font', value: '' },
      { name: 'fontSize', type: 'number', label: 'Size', value: 0 },
      { name: 'textAlign', type: 'choice', label: 'Align', value: '' },
      { name: 'labelPosition', type: 'choice', label: 'Position', value: '' },
    );
    fixture.componentRef.setInput('block', fontBlock);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const control = fixture.debugElement.query(By.directive(WidgetFontAppearanceControlComponent))
      .componentInstance as WidgetFontAppearanceControlComponent;
    control.appearanceChange.emit({ field: 'fontFaceId', value: 'helvetica-neue-700-5-upright' });

    expect(updateParam).toHaveBeenCalledWith('action-1', 'fontFaceId', 'helvetica-neue-700-5-upright');
  });
});
