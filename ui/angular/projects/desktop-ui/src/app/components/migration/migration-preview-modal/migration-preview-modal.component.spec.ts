import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MigrationSummary } from '@macro-deck/runtime';
import { MigrationPreviewModalComponent } from './migration-preview-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('MigrationPreviewModalComponent', () => {
  let fixture: ComponentFixture<MigrationPreviewModalComponent>;

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

  async function render(value: MigrationSummary): Promise<void> {
    fixture.componentRef.setInput('summary', value);
    await fixture.whenStable();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [MigrationPreviewModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(MigrationPreviewModalComponent);
    await fixture.whenStable();
  });

  it('shows the counts of what would be migrated', async () => {
    await render(summary());

    expect(text()).toContain('Macro Deck 2');
    expect(text()).toContain('2 profiles');
    expect(text()).toContain('5 folders');
    expect(text()).toContain('30 widgets');
    expect(text()).toContain('4 icons');
    expect(text()).toContain('40 actions will be translated');
    expect(text()).not.toContain('widget variable');
  });

  it('counts a single item without pluralising it', async () => {
    await render(summary({ profileCount: 1, folderCount: 1 }));

    expect(text()).toContain('1 profile');
    expect(text()).toContain('1 folder');
  });

  it('groups unsupported actions with their occurrence counts', async () => {
    await render(summary({
      unsupportedActionCount: 3,
      unsupportedActions: [
        { sourcePlugin: 'OBS Studio', sourceAction: 'Start streaming', occurrences: 2 },
        { sourcePlugin: 'OBS Studio', sourceAction: 'Switch scene', occurrences: 1 },
      ],
    }));

    expect(text()).toContain('3 actions could not be translated');
    expect(text()).toContain('Start streaming');
    expect(text()).toContain('used 2 times');
    expect(text()).toContain('Switch scene');
    expect(text()).toContain('used 1 time');
  });

  it('says nothing about unsupported actions when there are none', async () => {
    await render(summary());

    expect(fixture.nativeElement.querySelector('.migration-preview-unsupported')).toBeNull();
  });

  it('lists the integrations that would be configured, flagging which carry credentials', async () => {
    await render(summary({
      integrations: [
        { id: 'obs', title: 'OBS Studio', carriesCredentials: true },
        { id: 'twitch', title: 'Twitch', carriesCredentials: false },
      ],
    }));

    expect(text()).toContain('OBS Studio');
    expect(text()).toContain('Needs credentials');
    expect(text()).toContain('Twitch');
  });

  it('warns that the migration is best effort, naming the app being read', async () => {
    await render(summary());

    expect(text()).toContain('best-effort');
    expect(text()).toContain('Macro Deck 2');
  });

  it('lists warnings by subject and detail', async () => {
    await render(summary({
      warnings: [{ kind: 'MissingIcon', subject: 'Stream Deck +', detail: 'The icon file could not be found.' }],
    }));

    expect(text()).toContain('Stream Deck +');
    expect(text()).toContain('The icon file could not be found.');
  });

  it('renders a warning the host sent as a reference in the language this client shows', async () => {
    await render(summary({
      warnings: [
        {
          kind: 'MissingIcon',
          subject: 'Stream Deck +',
          detail: { $localized: { scope: 'macrodeck.app', key: 'Migration.Preview.WarningsTitle' } },
        },
      ],
    }));

    expect(text()).toContain('Warnings');
    expect(text()).not.toContain('$localized');
  });

  it('notes when encrypted data was skipped', async () => {
    await render(summary({ credentialStatus: 'Skipped' }));

    expect(text()).toContain('not migrated');
  });

  it('shows an import failure over the same preview without losing it', async () => {
    await render(summary());
    fixture.componentRef.setInput('bannerMessage', 'Storage failure');
    await fixture.whenStable();

    expect(text()).toContain('Storage failure');
    expect(text()).toContain('Macro Deck 2');
  });
});
