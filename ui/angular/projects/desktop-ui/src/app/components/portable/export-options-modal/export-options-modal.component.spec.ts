import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EXPORT_ICONS_TOGGLE, EXPORT_SUBFOLDERS_TOGGLE, ExportOptionsModalComponent } from './export-options-modal.component';
import { PortableExportOptions } from '../../../services/portability.service';
import { bundledTranslator, provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('ExportOptionsModalComponent', () => {
  let fixture: ComponentFixture<ExportOptionsModalComponent>;
  let component: ExportOptionsModalComponent;
  let confirmed: PortableExportOptions[];

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ExportOptionsModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    jasmine.clock().install();
    fixture = TestBed.createComponent(ExportOptionsModalComponent);
    component = fixture.componentInstance;
    confirmed = [];
    component.confirmed.subscribe(options => confirmed.push(options));
    await fixture.whenStable();
  });

  afterEach(() => jasmine.clock().uninstall());

  function checkboxLabels(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-checkbox') as NodeListOf<HTMLElement>)
      .map(element => element.textContent?.trim() ?? '');
  }

  function exportNow(): void {
    component['onExport']();
    jasmine.clock().tick(200);
  }

  it('offers only the secrets choice when no options are declared, and reports no other one', async () => {
    fixture.componentRef.setInput('options', []);
    await fixture.whenStable();

    exportNow();

    expect(checkboxLabels()).toEqual(['Include saved passwords']);
    expect(confirmed).toEqual([{ includeSecrets: false, password: undefined }]);
  });

  it('renders a declared option checked by default and reports it under its own key', async () => {
    fixture.componentRef.setInput('options', [EXPORT_SUBFOLDERS_TOGGLE, EXPORT_ICONS_TOGGLE]);
    await fixture.whenStable();

    expect(checkboxLabels()).toEqual(['Include subfolders', 'Include icons', 'Include saved passwords']);

    exportNow();

    expect(confirmed[0].includeSubfolders).toBeTrue();
    expect(confirmed[0].includeIcons).toBeTrue();
  });

  it('reports an unchecked option, and shows what leaving it out costs', async () => {
    fixture.componentRef.setInput('options', [EXPORT_SUBFOLDERS_TOGGLE, EXPORT_ICONS_TOGGLE]);
    await fixture.whenStable();
    component['setToggle']('includeIcons', false);
    await fixture.whenStable();

    const hints = fixture.nativeElement.querySelectorAll('.export-options-hint') as NodeListOf<HTMLElement>;
    expect(hints.length).toBe(1);
    expect(hints[0].textContent).toContain(bundledTranslator(EXPORT_ICONS_TOGGLE.uncheckedHintKey!));

    exportNow();

    expect(confirmed[0].includeIcons).toBeFalse();
    expect(confirmed[0].includeSubfolders).toBeTrue();
  });

  it('keeps a choice when the caller recomputes the option list', async () => {
    fixture.componentRef.setInput('options', [EXPORT_SUBFOLDERS_TOGGLE, EXPORT_ICONS_TOGGLE]);
    await fixture.whenStable();
    component['setToggle']('includeSubfolders', false);

    fixture.componentRef.setInput('options', [{ ...EXPORT_SUBFOLDERS_TOGGLE }, { ...EXPORT_ICONS_TOGGLE }]);
    await fixture.whenStable();

    exportNow();

    expect(confirmed[0].includeSubfolders).toBeFalse();
    expect(confirmed[0].includeIcons).toBeTrue();
  });

  it('generates a password when secrets are included, and blocks export until it is long enough', async () => {
    component['setIncludeSecrets'](true);
    await fixture.whenStable();

    expect(component['password']().length).toBeGreaterThan(8);
    expect(component['canExport']()).toBeTrue();

    component['password'].set('short');

    expect(component['canExport']()).toBeFalse();
    exportNow();
    expect(confirmed).toEqual([]);
  });
});
