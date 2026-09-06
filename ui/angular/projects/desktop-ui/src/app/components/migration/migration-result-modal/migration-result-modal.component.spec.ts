import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MigrationSummary } from '@macro-deck/runtime';
import { MigrationResult, MigrationResultModalComponent } from './migration-result-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('MigrationResultModalComponent', () => {
  let fixture: ComponentFixture<MigrationResultModalComponent>;

  function summary(overrides: Partial<MigrationSummary> = {}): MigrationSummary {
    return {
      sourceId: 'macrodeck2',
      sourceName: 'Macro Deck 2',
      profileCount: 2,
      folderCount: 5,
      widgetCount: 30,
      iconCount: 4,
      variableCount: 0,
      migratedActionCount: 40,
      unsupportedActionCount: 0,
      credentialStatus: 'NotPresent',
      integrations: [],
      unsupportedActions: [],
      warnings: [],
      ...overrides,
    };
  }

  async function render(value: MigrationResult): Promise<void> {
    fixture.componentRef.setInput('result', value);
    await fixture.whenStable();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [MigrationResultModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(MigrationResultModalComponent);
    await fixture.whenStable();
  });

  it('reports how many profiles were created', async () => {
    await render({ summary: summary(), profileIds: ['p1', 'p2'] });

    expect(text()).toContain('2 profiles');
  });

  it('counts a single created profile without pluralising it', async () => {
    await render({ summary: summary(), profileIds: ['p1'] });

    expect(text()).toContain('1 profile');
  });

  it('lists remaining warnings', async () => {
    await render({
      summary: summary({ warnings: [{ kind: 'MissingIcon', subject: 'Stream Deck +', detail: 'Icon missing.' }] }),
      profileIds: ['p1'],
    });

    expect(text()).toContain('Stream Deck +');
    expect(text()).toContain('Icon missing.');
  });

  it('says nothing about warnings when there are none', async () => {
    await render({ summary: summary(), profileIds: ['p1'] });

    expect(fixture.nativeElement.querySelector('.migration-result-warnings')).toBeNull();
  });

  it('emits done when closed', async () => {
    await render({ summary: summary(), profileIds: ['p1'] });

    const emitted: void[] = [];
    fixture.componentInstance.done.subscribe(() => emitted.push(undefined));

    jasmine.clock().install();
    try {
      (fixture.nativeElement.querySelector('shared-button button') as HTMLButtonElement).click();
      jasmine.clock().tick(150);
    } finally {
      jasmine.clock().uninstall();
    }

    expect(emitted.length).toBe(1);
  });
});
