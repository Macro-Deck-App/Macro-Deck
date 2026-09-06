import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BackupComponentCatalogEntry } from '@macro-deck/runtime';
import { BackupRestoreModalComponent } from './backup-restore-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

const CATALOG: BackupComponentCatalogEntry[] = [
  { id: 'Icons', requires: [] },
  { id: 'Variables', requires: [] },
  { id: 'Integrations', requires: [] },
  { id: 'AppSettings', requires: [] },
  { id: 'Plugins', requires: ['Integrations'] },
  { id: 'Scripts', requires: ['Integrations'] },
  { id: 'Profiles', requires: ['Icons', 'Integrations', 'Plugins'] },
  { id: 'Automations', requires: ['Scripts', 'Integrations', 'Variables'] },
  { id: 'Accounts', requires: ['Profiles'] },
];

describe('BackupRestoreModalComponent', () => {
  let fixture: ComponentFixture<BackupRestoreModalComponent>;

  async function create(): Promise<ComponentFixture<BackupRestoreModalComponent>> {
    TestBed.configureTestingModule({
      imports: [BackupRestoreModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    const f = TestBed.createComponent(BackupRestoreModalComponent);
    f.componentRef.setInput('catalog', CATALOG);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function checkboxFor(group: string): HTMLInputElement {
    const row = fixture.nativeElement.querySelector(`[data-group="${group}"]`) as HTMLElement;
    return row.querySelector('input[type="checkbox"]') as HTMLInputElement;
  }

  function clickLink(label: string): void {
    const link = Array.from(fixture.nativeElement.querySelectorAll('.brm-link'))
      .find(el => (el as HTMLElement).textContent?.trim() === label) as HTMLElement;
    link.click();
  }

  function continueButton(): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === 'Continue') as HTMLButtonElement;
  }

  it('checks every group by default', async () => {
    fixture = await create();

    for (const entry of CATALOG) {
      expect(checkboxFor(entry.id).checked).withContext(entry.id).toBeTrue();
    }
  });

  it('unchecking a dependency renders the warning and leaves the Restore button enabled', async () => {
    fixture = await create();

    checkboxFor('Icons').click();
    await settle();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('without icons');
    expect(continueButton().disabled).toBeFalse();
  });

  it('checking a group auto-checks its dependencies transitively, starting from a cleared selection', async () => {
    fixture = await create();

    clickLink('Clear all');
    await settle();
    expect(checkboxFor('Accounts').checked).toBeFalse();

    checkboxFor('Accounts').click();
    await settle();

    expect(checkboxFor('Accounts').checked).toBeTrue();
    expect(checkboxFor('Profiles').checked).toBeTrue();
    expect(checkboxFor('Plugins').checked).toBeTrue();
    expect(checkboxFor('Integrations').checked).toBeTrue();
    expect(checkboxFor('Icons').checked).toBeTrue();
    // Not pulled in - no path from Accounts to any of these through the catalogue above.
    expect(checkboxFor('Scripts').checked).toBeFalse();
    expect(checkboxFor('Variables').checked).toBeFalse();
    expect(checkboxFor('Automations').checked).toBeFalse();
    expect(checkboxFor('AppSettings').checked).toBeFalse();
  });

  describe('confirming (issue #36)', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('emits only the selected groups on confirm, not the full catalogue', async () => {
      fixture = await create();
      const confirmed = jasmine.createSpy('confirmed');
      fixture.componentInstance.confirmed.subscribe(confirmed);

      clickLink('Clear all');
      await settle();
      checkboxFor('Icons').click();
      await settle();

      fixture.componentInstance.onConfirm();
      fixture.detectChanges();
      jasmine.clock().tick(150);

      expect(confirmed).toHaveBeenCalledWith(['Icons']);
    });

    it('does nothing when nothing is selected', async () => {
      fixture = await create();
      const confirmed = jasmine.createSpy('confirmed');
      fixture.componentInstance.confirmed.subscribe(confirmed);

      clickLink('Clear all');
      await settle();

      fixture.componentInstance.onConfirm();
      jasmine.clock().tick(150);

      expect(confirmed).not.toHaveBeenCalled();
    });
  });
});
