import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import { GridSettingsComponent } from './grid-settings.component';

describe('GridSettingsComponent', () => {
  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [GridSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
  });

  function create(): { component: GridSettingsComponent; fixture: ReturnType<typeof TestBed.createComponent<GridSettingsComponent>> } {
    const fixture = TestBed.createComponent(GridSettingsComponent);
    return { component: fixture.componentInstance, fixture };
  }

  function resetButtons(fixture: ReturnType<typeof TestBed.createComponent<GridSettingsComponent>>): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.setting-reset'));
  }

  function sliders(fixture: ReturnType<typeof TestBed.createComponent<GridSettingsComponent>>): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('input[type="range"].slider'));
  }

  it('disables only the axis a device fixes, leaving the free axis editable', async () => {
    const { component, fixture } = create();
    const emitted: (number | null)[] = [];
    component.colsChange.subscribe(value => emitted.push(value));

    fixture.componentRef.setInput('rowsLocked', true);
    fixture.componentRef.setInput('colsLocked', false);
    await fixture.whenStable();

    // Columns render first, rows second - see the template's two inheritable-setting blocks.
    const [columns, rows] = sliders(fixture);
    expect(rows.disabled).toBeTrue();
    expect(columns.disabled).toBeFalse();

    component.onColsChange(7);
    expect(emitted).toEqual([7]);
  });

  it('shows the lock note instead of the range for a locked axis', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('rowsLocked', true);
    fixture.componentRef.setInput('lockNote', 'Fixed by Stream Deck XL');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Fixed by Stream Deck XL');
    expect(resetButtons(fixture).length).toBe(1);
  });

  it('bounds each slider by the min and max it is given', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('minCols', 2);
    fixture.componentRef.setInput('maxCols', 8);
    fixture.componentRef.setInput('minRows', 1);
    fixture.componentRef.setInput('maxRows', 3);
    await fixture.whenStable();

    const [columns, rows] = sliders(fixture);
    expect([columns.min, columns.max]).toEqual(['2', '8']);
    expect([rows.min, rows.max]).toEqual(['1', '3']);
  });

  it('leaves both sliders enabled when no device fixes the grid', async () => {
    const { fixture } = create();
    await fixture.whenStable();

    expect(sliders(fixture).map(slider => slider.disabled)).toEqual([false, false]);
  });

  it('hides the widget appearance section by default', async () => {
    const { fixture } = create();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).not.toContain('Widget spacing');
    expect(fixture.nativeElement.textContent).not.toContain('Corner radius');
  });

  it('shows the section when showWidgetAppearance is enabled', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('showWidgetAppearance', true);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Widget spacing');
    expect(fixture.nativeElement.textContent).toContain('Corner radius');
  });

  it('shows a reset control for columns and rows by default', async () => {
    const { fixture } = create();
    await fixture.whenStable();

    expect(resetButtons(fixture).length).toBe(2);
  });

  it('adds spacing and corner-radius resets once widget appearance is shown', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('showWidgetAppearance', true);
    await fixture.whenStable();

    expect(resetButtons(fixture).length).toBe(4);
  });

  it('suppresses only the grid reset when gridInheritable is false', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('gridInheritable', false);
    fixture.componentRef.setInput('showWidgetAppearance', true);
    await fixture.whenStable();

    expect(resetButtons(fixture).length).toBe(2);
  });

  it('emits null on colsChange/rowsChange when their reset is clicked', async () => {
    const { component, fixture } = create();
    fixture.componentRef.setInput('cols', 8);
    fixture.componentRef.setInput('rows', 6);
    await fixture.whenStable();
    fixture.detectChanges();

    const colsValues: (number | null)[] = [];
    const rowsValues: (number | null)[] = [];
    component.colsChange.subscribe(v => colsValues.push(v));
    component.rowsChange.subscribe(v => rowsValues.push(v));

    const [colsReset, rowsReset] = resetButtons(fixture);
    colsReset.click();
    rowsReset.click();

    expect(colsValues).toEqual([null]);
    expect(rowsValues).toEqual([null]);
  });

  it('emits null on spacingChange/borderRadiusChange when their reset is clicked', async () => {
    const { component, fixture } = create();
    fixture.componentRef.setInput('showWidgetAppearance', true);
    fixture.componentRef.setInput('spacing', 20);
    fixture.componentRef.setInput('borderRadius', 30);
    await fixture.whenStable();
    fixture.detectChanges();

    const spacingValues: (number | null)[] = [];
    const radiusValues: (number | null)[] = [];
    component.spacingChange.subscribe(v => spacingValues.push(v));
    component.borderRadiusChange.subscribe(v => radiusValues.push(v));

    const [, , spacingReset, radiusReset] = resetButtons(fixture);
    spacingReset.click();
    radiusReset.click();

    expect(spacingValues).toEqual([null]);
    expect(radiusValues).toEqual([null]);
  });

  it('does not emit a change from a disabled (already inherited) reset', async () => {
    const { component, fixture } = create();
    fixture.componentRef.setInput('cols', null);
    await fixture.whenStable();
    fixture.detectChanges();

    const colsValues: (number | null)[] = [];
    component.colsChange.subscribe(v => colsValues.push(v));

    const [colsReset] = resetButtons(fixture);
    expect(colsReset.disabled).toBeTrue();
    colsReset.click();

    expect(colsValues).toEqual([]);
  });

  it('shows the inherited label with the resolved value while a setting is null', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('cols', null);
    fixture.componentRef.setInput('effectiveCols', 16);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Inherited · 16');
  });

  it('forwards inheritedLabel to every setting', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('inheritedLabel', 'Default');
    fixture.componentRef.setInput('showWidgetAppearance', true);
    fixture.componentRef.setInput('cols', null);
    fixture.componentRef.setInput('effectiveCols', 16);
    fixture.componentRef.setInput('spacing', null);
    fixture.componentRef.setInput('effectiveSpacing', 12);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Default · 16');
    expect(text).toContain('Default · 12 px');
    expect(text).not.toContain('Inherited');
  });

  it('emits slider values as numbers', () => {
    const { component } = create();
    const colsValues: (number | null)[] = [];
    const spacingValues: (number | null)[] = [];
    const radiusValues: (number | null)[] = [];
    component.colsChange.subscribe(v => colsValues.push(v));
    component.spacingChange.subscribe(v => spacingValues.push(v));
    component.borderRadiusChange.subscribe(v => radiusValues.push(v));

    component.onColsChange('7' as unknown as number);
    component.onSpacingSliderChange('24' as unknown as number);
    component.onBorderRadiusSliderChange('36' as unknown as number);

    expect(colsValues).toEqual([7]);
    expect(spacingValues).toEqual([24]);
    expect(radiusValues).toEqual([36]);
  });

  it('shows Default for a fully inherited radius without a resolved value', async () => {
    const { fixture } = create();
    fixture.componentRef.setInput('showWidgetAppearance', true);
    fixture.componentRef.setInput('borderRadius', null);
    fixture.componentRef.setInput('effectiveBorderRadius', null);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Default');
  });
});
