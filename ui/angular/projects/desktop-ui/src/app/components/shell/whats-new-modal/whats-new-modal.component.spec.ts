import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PostUpdateChangelogService } from '../../../services/post-update-changelog.service';
import { WhatsNewModalComponent } from './whats-new-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('WhatsNewModalComponent', () => {
  let dismissPostUpdateChangelog: jasmine.Spy;

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  async function createFixture(changelog: ShellPostUpdateChangelog): Promise<ComponentFixture<WhatsNewModalComponent>> {
    dismissPostUpdateChangelog = jasmine.createSpy('dismiss').and.resolveTo(undefined);
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      getPostUpdateChangelog: () => Promise.resolve(changelog),
      dismissPostUpdateChangelog,
    };
    TestBed.configureTestingModule({
      imports: [WhatsNewModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    await TestBed.inject(PostUpdateChangelogService).load();
    const fixture = TestBed.createComponent(WhatsNewModalComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows the installed version and its release notes', async () => {
    const fixture = await createFixture({
      version: '3.2.0',
      notes: '## Fixes\n- **Faster** deck loading',
      publishedAt: '2026-09-16',
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain("What's new in Macro Deck 3.2.0");
    expect(element.querySelector('strong')?.textContent).toBe('Faster');
    expect(element.textContent).not.toContain('NaN');
  });

  it('is dismissed for good when the user closes it', async () => {
    const fixture = await createFixture({ version: '3.2.0', notes: 'notes', publishedAt: null });
    const service = TestBed.inject(PostUpdateChangelogService);

    const gotIt = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(b => b.textContent?.includes('Got it'));
    gotIt?.click();
    await new Promise(resolve => setTimeout(resolve, 400));
    await fixture.whenStable();

    expect(service.isOpen()).toBeFalse();
    expect(dismissPostUpdateChangelog).toHaveBeenCalledTimes(1);
  });
});
