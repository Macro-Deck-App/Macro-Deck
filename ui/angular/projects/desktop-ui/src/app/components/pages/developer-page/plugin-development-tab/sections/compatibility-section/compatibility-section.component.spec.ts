import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PluginCompatibilityReport } from '@macro-deck/runtime';
import { DeveloperModeService } from '../../../../../../services/developer-mode.service';
import { ExternalLinkService } from '../../../../../../services/external-link.service';
import { PluginCompatibilityService } from '../../../../../../services/plugin-compatibility.service';
import { CompatibilitySectionComponent } from './compatibility-section.component';
import { provideLocalizationTesting } from '../../../../../../../testing/localization-test-support';

function report(pluginId: string, overrides: Partial<PluginCompatibilityReport> = {}): PluginCompatibilityReport {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    state: 'compatible',
    usageSource: 'unknown',
    usageTruncated: false,
    findings: [],
    ...overrides,
  };
}

describe('CompatibilitySectionComponent', () => {
  let fixture: ComponentFixture<CompatibilitySectionComponent>;
  let serviceSpy: jasmine.SpyObj<PluginCompatibilityService>;
  let reportsSignal: WritableSignal<PluginCompatibilityReport[]>;
  let loadErrorSignal: WritableSignal<string | null>;

  function configure(reports: PluginCompatibilityReport[] = [], developerMode = true): void {
    serviceSpy = jasmine.createSpyObj<PluginCompatibilityService>('PluginCompatibilityService', ['load', 'forPlugin']);
    reportsSignal = signal(reports);
    loadErrorSignal = signal<string | null>(null);
    Object.defineProperty(serviceSpy, 'reports', { value: reportsSignal });
    Object.defineProperty(serviceSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(serviceSpy, 'loadError', { value: loadErrorSignal });
    serviceSpy.load.and.resolveTo();

    const developerModeSpy = jasmine.createSpyObj<DeveloperModeService>('DeveloperModeService', ['ensureLoaded']);
    Object.defineProperty(developerModeSpy, 'enabled', { value: signal(developerMode) });
    developerModeSpy.ensureLoaded.and.resolveTo();

    TestBed.configureTestingModule({
      imports: [CompatibilitySectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginCompatibilityService, useValue: serviceSpy },
        { provide: DeveloperModeService, useValue: developerModeSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<CompatibilitySectionComponent>> {
    const f = TestBed.createComponent(CompatibilitySectionComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('shows the empty state when there are no reports', async () => {
    configure();
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.compatibility-empty')).toBeTruthy();
  });

  it('shows the load error banner', async () => {
    configure();
    fixture = await create();
    loadErrorSignal.set('boom');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.compatibility-error').textContent).toContain('boom');
  });

  it("renders a plugin's state and SDK version", async () => {
    configure([report('p1', { state: 'update_required', sdkVersion: '3.0.0' })]);
    fixture = await create();

    const card = fixture.nativeElement.querySelector('.compatibility-card');
    expect(card.textContent).toContain('Update required');
    expect(card.textContent).toContain('3.0.0');
  });

  it('words a confirmed report and an inferred report distinguishably differently', async () => {
    configure([
      report('confirmed-plugin', { usageSource: 'confirmed' }),
      report('inferred-plugin', { usageSource: 'inferred' }),
    ]);
    fixture = await create();

    const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.compatibility-card'));
    const confirmedText = cards[0].querySelector('.compatibility-evidence')!.textContent!;
    const inferredText = cards[1].querySelector('.compatibility-evidence')!.textContent!;

    expect(confirmedText).not.toEqual(inferredText);
    expect(confirmedText.toLowerCase()).not.toContain('may');
    expect(inferredText.toLowerCase()).toContain('may');
  });

  it('shows a note when the usage manifest was truncated, and omits it otherwise', async () => {
    configure([
      report('truncated', { displayName: 'A truncated plugin', usageTruncated: true }),
      report('complete', { displayName: 'B complete plugin', usageTruncated: false }),
    ]);
    fixture = await create();

    const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.compatibility-card'));
    expect(cards[0].querySelector('.compatibility-truncated-note')).toBeTruthy();
    expect(cards[1].querySelector('.compatibility-truncated-note')).toBeFalsy();
  });

  it('renders every finding field in the table', async () => {
    configure([
      report('p1', {
        findings: [
          {
            diagnosticId: 'MDP5002',
            source: 'confirmed',
            severity: 'warning',
            subject: 'MacroDeck.Sdk.Foo.Bar',
            guidance: "Use 'X' instead.",
            deprecatedIn: '3.1.0',
            removedIn: '4.0.0',
            replacement: 'MacroDeck.Sdk.X',
            migrationUrl: 'https://example.com/migrate',
          },
        ],
      }),
    ]);
    fixture = await create();

    const row = fixture.nativeElement.querySelector('.compatibility-findings tbody tr');
    expect(row.textContent).toContain('MDP5002');
    expect(row.textContent).toContain('Confirmed');
    expect(row.textContent).toContain('MacroDeck.Sdk.Foo.Bar');
    expect(row.textContent).toContain('3.1.0');
    expect(row.textContent).toContain('4.0.0');
    expect(row.textContent).toContain('MacroDeck.Sdk.X');
    expect(row.textContent).toContain("Use 'X' instead.");
    expect(row.querySelector('.finding-migration-link')).toBeTruthy();
  });

  it('renders "-" placeholders for absent optional finding fields', async () => {
    configure([
      report('p1', {
        findings: [
          {
            diagnosticId: 'MDP5003',
            source: 'inferred',
            severity: 'info',
            subject: 'protocol',
            guidance: 'Update the plugin.',
          },
        ],
      }),
    ]);
    fixture = await create();

    const cells: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('.compatibility-findings tbody tr td'));
    expect(cells[3].textContent!.trim()).toBe('-');
    expect(cells[4].textContent!.trim()).toBe('-');
    expect(cells[5].textContent!.trim()).toBe('-');
    expect(cells[6].querySelector('.finding-migration-link')).toBeFalsy();
  });

  it('opens the migration link through ExternalLinkService rather than a plain anchor', async () => {
    configure([
      report('p1', {
        findings: [
          {
            diagnosticId: 'MDP5002',
            source: 'confirmed',
            severity: 'warning',
            subject: 'MacroDeck.Sdk.Foo.Bar',
            guidance: 'Use X instead.',
            migrationUrl: 'https://example.com/migrate',
          },
        ],
      }),
    ]);
    fixture = await create();
    const externalLinks = TestBed.inject(ExternalLinkService);
    const openSpy = spyOn(externalLinks, 'open');

    (fixture.nativeElement.querySelector('.finding-migration-link') as HTMLButtonElement).click();

    expect(openSpy).toHaveBeenCalledWith('https://example.com/migrate');
  });
});
