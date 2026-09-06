import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DockIconSettingsComponent } from './dock-icon-settings.component';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';

describe('DockIconSettingsComponent', () => {
  function setShell(shell: Record<string, unknown> | undefined): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = shell;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DockIconSettingsComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();
  });

  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  async function create(): Promise<ComponentFixture<DockIconSettingsComponent>> {
    const fixture = TestBed.createComponent(DockIconSettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function toggle(f: ComponentFixture<DockIconSettingsComponent>): HTMLInputElement | null {
    return f.nativeElement.querySelector('.ts-input');
  }

  it('renders nothing in a plain browser', async () => {
    setShell(undefined);
    const fixture = await create();

    expect(toggle(fixture)).toBeNull();
  });

  it('renders nothing on a shell that predates the setting', async () => {
    setShell({ kind: 'tauri' });
    const fixture = await create();

    expect(toggle(fixture)).toBeNull();
  });

  it('renders nothing when the shell reports the setting is unsupported', async () => {
    setShell({
      getHideDockIcon: () => Promise.resolve({ supported: false, enabled: false }),
    });
    const fixture = await create();

    expect(toggle(fixture)).toBeNull();
    expect(fixture.componentInstance.supported()).toBeFalse();
  });

  it('reflects the stored preference on macOS', async () => {
    setShell({
      getHideDockIcon: () => Promise.resolve({ supported: true, enabled: true }),
    });
    const fixture = await create();

    const input = toggle(fixture);
    expect(input).toBeTruthy();
    expect(input!.checked).toBeTrue();
  });

  it('persists a change through the shell bridge', async () => {
    const setHideDockIcon = jasmine
      .createSpy('setHideDockIcon')
      .and.callFake((enabled: boolean) => Promise.resolve({ supported: true, enabled }));
    setShell({
      getHideDockIcon: () => Promise.resolve({ supported: true, enabled: false }),
      setHideDockIcon,
    });
    const fixture = await create();

    toggle(fixture)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(setHideDockIcon).toHaveBeenCalledWith(true);
    expect(fixture.componentInstance.enabled()).toBeTrue();
    expect(fixture.componentInstance.busy()).toBeFalse();
  });

  it('reverts the toggle when the shell rejects the change', async () => {
    setShell({
      getHideDockIcon: () => Promise.resolve({ supported: true, enabled: false }),
      setHideDockIcon: () => Promise.reject(new Error('write failed')),
    });
    const fixture = await create();

    toggle(fixture)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.enabled()).toBeFalse();
    expect(toggle(fixture)!.checked).toBeFalse();
  });

  it('ignores further clicks while a change is in flight', async () => {
    let resolveSet!: (status: ShellDockIconStatus) => void;
    const setHideDockIcon = jasmine
      .createSpy('setHideDockIcon')
      .and.returnValue(new Promise<ShellDockIconStatus>(resolve => (resolveSet = resolve)));
    setShell({
      getHideDockIcon: () => Promise.resolve({ supported: true, enabled: false }),
      setHideDockIcon,
    });
    const fixture = await create();

    toggle(fixture)!.click();
    expect(fixture.componentInstance.busy()).toBeTrue();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(setHideDockIcon).toHaveBeenCalledTimes(1);

    toggle(fixture)!.click();
    fixture.detectChanges();
    expect(setHideDockIcon).toHaveBeenCalledTimes(1);

    resolveSet({ supported: true, enabled: true });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.enabled()).toBeTrue();
    expect(fixture.componentInstance.busy()).toBeFalse();
  });
});
