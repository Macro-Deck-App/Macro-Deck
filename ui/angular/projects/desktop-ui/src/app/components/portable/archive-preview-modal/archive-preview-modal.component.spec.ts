import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ArchiveSummary } from '@macro-deck/runtime';
import { ArchivePreviewModalComponent } from './archive-preview-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('ArchivePreviewModalComponent', () => {
  let fixture: ComponentFixture<ArchivePreviewModalComponent>;

  function summary(overrides: Partial<ArchiveSummary> = {}): ArchiveSummary {
    return {
      kind: 'Profile',
      name: 'Streaming',
      appVersion: '3.0.0',
      createdAt: '2026-07-30T10:00:00Z',
      encrypted: false,
      includesSecrets: false,
      folderCount: 2,
      widgetCount: 7,
      iconCount: 4,
      scriptCount: 0,
      variableCount: 0,
      secretCount: 0,
      integrations: [],
      ...overrides,
    };
  }

  async function render(value: ArchiveSummary): Promise<void> {
    fixture.componentRef.setInput('summary', value);
    await fixture.whenStable();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ArchivePreviewModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(ArchivePreviewModalComponent);
    await fixture.whenStable();
  });

  it('lists what the archive holds, leaving out what it has none of', async () => {
    await render(summary());

    expect(text()).toContain('Streaming');
    expect(text()).toContain('2 folders');
    expect(text()).toContain('7 widgets');
    expect(text()).toContain('4 icons');
    expect(text()).not.toContain('script');
    expect(text()).not.toContain('saved password');
  });

  it('counts a single item without pluralising it', async () => {
    await render(summary({ folderCount: 1, widgetCount: 1, iconCount: 0 }));

    expect(text()).toContain('1 folder');
    expect(text()).toContain('1 widget');
  });

  it('shows the widget variable count only when the archive holds any', async () => {
    await render(summary());
    expect(text()).not.toContain('widget variable');

    await render(summary({ variableCount: 3 }));
    expect(text()).toContain('3 widget variables');
  });

  it('omits the folder count for a widget archive, which has no folders', async () => {
    await render(summary({ kind: 'Widgets', folderCount: 0, widgetCount: 3 }));

    expect(text()).toContain('3 widgets');
    expect(text()).not.toContain('folder');
  });

  it('says a password will be asked for when the archive is encrypted', async () => {
    await render(summary({ encrypted: true, includesSecrets: true, secretCount: 2 }));

    expect(text()).toContain('2 saved passwords');
    expect(fixture.nativeElement.querySelector('.archive-preview-warning')).not.toBeNull();
  });

  it('reports each required integration with how far this machine satisfies it', async () => {
    await render(summary({
      integrations: [
        { id: 'a', name: 'OBS Studio', version: '1.0.0', requiresConfiguration: true, availability: 'NotConfigured' },
        { id: 'b', name: 'Keyboard', version: '1.0.0', requiresConfiguration: false, availability: 'Ready' },
        { id: 'c', name: 'Twitch', version: '1.0.0', requiresConfiguration: true, availability: 'Missing' },
      ],
    }));

    const statuses = Array.from(
      fixture.nativeElement.querySelectorAll('.archive-preview-status') as NodeListOf<HTMLElement>
    ).map(element => `${element.textContent?.trim()}|${element.className}`);

    expect(statuses[0]).toContain('Not set up yet');
    expect(statuses[0]).toContain('is-warn');
    expect(statuses[1]).toContain('Ready');
    expect(statuses[1]).toContain('is-ok');
    expect(statuses[2]).toContain('Not installed');
    expect(statuses[2]).toContain('is-error');
    expect(text()).toContain('The import works either way');
  });

  it('says nothing about integrations when the archive needs none', async () => {
    await render(summary());

    expect(fixture.nativeElement.querySelector('.archive-preview-integrations')).toBeNull();
  });
});
