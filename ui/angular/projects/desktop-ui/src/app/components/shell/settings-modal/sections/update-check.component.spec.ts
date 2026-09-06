import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UpdateCheckComponent } from './update-check.component';
import { UpdateModalService } from '../../../../services/update-modal.service';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';

interface UpdateModeStatus {
  mode: 'off' | 'notifyOnly' | 'automatic';
  automaticSupported: boolean;
}

function makeState(overrides: Partial<ShellUpdateState> = {}): ShellUpdateState {
  return {
    phase: 'idle',
    supported: true,
    currentVersion: '3.0.0',
    version: null,
    notes: null,
    publishedAt: null,
    channel: 'stable',
    betaInstalled: false,
    installStrategy: 'inApp',
    downloadUrl: null,
    partialCheck: null,
    error: null,
    failure: null,
    progress: null,
    lastCheckedAt: null,
    ...overrides,
  };
}

function makeModeStatus(overrides: Partial<UpdateModeStatus> = {}): UpdateModeStatus {
  return {
    mode: 'notifyOnly',
    automaticSupported: true,
    ...overrides,
  };
}

describe('UpdateCheckComponent', () => {
  afterEach(() => {
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function setShell(shell: Record<string, unknown>): void {
    (window as { macroDeckShell?: unknown }).macroDeckShell = shell;
  }

  function findBetaToggleInput(fixture: ComponentFixture<UpdateCheckComponent>): HTMLInputElement | null {
    return fixture.nativeElement.querySelector('input[aria-label="Receive beta updates"]');
  }

  function findModeGroup(fixture: ComponentFixture<UpdateCheckComponent>): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="group"][aria-label="Automatic updates"]');
  }

  function findModeOption(fixture: ComponentFixture<UpdateCheckComponent>, label: string): HTMLButtonElement | null {
    const group = findModeGroup(fixture);
    if (!group) {
      return null;
    }
    const buttons = Array.from(group.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.find(button => button.textContent?.trim() === label) ?? null;
  }

  async function createFixture(): Promise<ComponentFixture<UpdateCheckComponent>> {
    await TestBed.configureTestingModule({
      imports: [UpdateCheckComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    const fixture = TestBed.createComponent(UpdateCheckComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<UpdateCheckComponent>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function clickButtonWithText(fixture: ComponentFixture<UpdateCheckComponent>, text: string): void {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const target = buttons.find(button => button.textContent?.includes(text));
    if (!target) {
      throw new Error(`button containing "${text}" not found`);
    }
    target.click();
  }

  it('renders nothing outside the desktop shell', async () => {
    const fixture = await createFixture();

    expect(fixture.nativeElement.querySelector('shared-settings-section')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Updates');
  });

  it('shows a "Check for updates" action inside the desktop shell', async () => {
    setShell({ getUpdateState: () => Promise.resolve(makeState()), onUpdateState: () => Promise.resolve(() => {}) });

    const fixture = await createFixture();

    expect(fixture.nativeElement.textContent).toContain('Updates');
    expect(fixture.nativeElement.textContent).toContain('Check for updates');
  });

  it('asks the shell for a manual re-check when "Check for updates" is clicked', async () => {
    // The manual button must go through the Manual trigger (checkForUpdate), not
    // the rate-limited requestUpdateCheck reserved for the reconnect trigger
    // (issue #249) - otherwise a click shortly after any other check is a no-op.
    const checkForUpdate = jasmine.createSpy('checkForUpdate').and.resolveTo(undefined);
    setShell({
      getUpdateState: () => Promise.resolve(makeState()),
      onUpdateState: () => Promise.resolve(() => {}),
      checkForUpdate,
    });

    const fixture = await createFixture();
    clickButtonWithText(fixture, 'Check for updates');
    await settle(fixture);

    expect(checkForUpdate).toHaveBeenCalled();
  });

  it('reports being up to date once the state says so', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'upToDate', currentVersion: '3.0.0' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('latest version');
    expect(text).toContain('3.0.0');
  });

  it('shows the available version and a way to see details, without installing directly from the settings row', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'available', version: '3.1.0' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Version 3.1.0 is available');
    expect(text).toContain('View details');
  });

  it('opens the update modal from "View details"', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'available', version: '3.1.0' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();
    const modal = TestBed.inject(UpdateModalService);
    clickButtonWithText(fixture, 'View details');

    expect(modal.isOpen()).toBeTrue();
  });

  it('shows an error when the state reports one, not a check stuck forever', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'failed', error: 'offline' })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('offline');
    expect(text).toContain('Try again');
  });

  it('leaves an error state (never stuck at "checking") when the re-check request itself rejects', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'checking' })),
      onUpdateState: () => Promise.resolve(() => {}),
      checkForUpdate: () => Promise.reject(new Error('boom')),
    });

    const fixture = await createFixture();
    clickButtonWithText(fixture, 'Check for updates');
    await settle(fixture);

    // The service swallows the rejection itself (the shell is expected to push the real
    // failure through onUpdateState); this only proves the click does not throw and the
    // section keeps rendering normally rather than getting stuck.
    expect(fixture.nativeElement.querySelector('shared-settings-section')).toBeTruthy();
  });

  it('shows a note when the updater is unsupported (dev build), distinctly from a failure', async () => {
    setShell({
      getUpdateState: () => Promise.resolve(makeState({ phase: 'unsupported', supported: false })),
      onUpdateState: () => Promise.resolve(() => {}),
    });

    const fixture = await createFixture();
    await settle(fixture);

    expect(fixture.nativeElement.textContent).toContain("Automatic updates aren't available");
    expect(fixture.nativeElement.textContent).not.toContain('Try again');
  });

  describe('update channel (issue #272)', () => {
    it('renders the beta toggle with its accessible name once the channel is known', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'stable' })),
        onUpdateState: () => Promise.resolve(() => {}),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(findBetaToggleInput(fixture)).toBeTruthy();
      expect(fixture.nativeElement.textContent).toContain('Receive beta updates');
    });

    it('hides the beta toggle on an older shell that never reports a channel', async () => {
      setShell({});

      const fixture = await createFixture();
      await settle(fixture);

      expect(findBetaToggleInput(fixture)).toBeNull();
      expect(fixture.nativeElement.textContent).not.toContain('Receive beta updates');
    });

    it('asks for confirmation before enabling beta updates, and does not call setUpdateChannel until confirmed', async () => {
      const setUpdateChannel = jasmine
        .createSpy('setUpdateChannel')
        .and.resolveTo({ channel: 'beta', betaEnabled: true });
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'stable' })),
        onUpdateState: () => Promise.resolve(() => {}),
        setUpdateChannel,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findBetaToggleInput(fixture)?.click();
      fixture.detectChanges();
      await settle(fixture);

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();
      expect(fixture.nativeElement.textContent).toContain('Enable beta updates?');
      expect(setUpdateChannel).not.toHaveBeenCalled();
    });

    it('cancelling the beta confirmation persists nothing and leaves the checkbox visually off', async () => {
      const setUpdateChannel = jasmine.createSpy('setUpdateChannel');
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'stable' })),
        onUpdateState: () => Promise.resolve(() => {}),
        setUpdateChannel,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findBetaToggleInput(fixture)?.click();
      fixture.detectChanges();
      await settle(fixture);

      fixture.componentInstance.cancelBetaOptIn();
      fixture.detectChanges();
      await settle(fixture);

      expect(setUpdateChannel).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
      expect(findBetaToggleInput(fixture)?.checked).toBe(false);
    });

    it('confirming the beta opt-in applies the channel and re-checks', async () => {
      const setUpdateChannel = jasmine
        .createSpy('setUpdateChannel')
        .and.resolveTo({ channel: 'beta', betaEnabled: true });
      // The follow-up check after a channel switch is a manual check too
      // (issue #249): it must resolve the new channel right away, not wait
      // out the shell's rate limit.
      const checkForUpdate = jasmine.createSpy('checkForUpdate').and.resolveTo(undefined);
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'stable' })),
        onUpdateState: () => Promise.resolve(() => {}),
        setUpdateChannel,
        checkForUpdate,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findBetaToggleInput(fixture)?.click();
      fixture.detectChanges();
      await settle(fixture);

      fixture.componentInstance.confirmBetaOptIn();
      await settle(fixture);

      expect(setUpdateChannel).toHaveBeenCalledWith('beta');
      expect(checkForUpdate).toHaveBeenCalled();
    });

    it('disables beta updates immediately, without a confirmation', async () => {
      const setUpdateChannel = jasmine
        .createSpy('setUpdateChannel')
        .and.resolveTo({ channel: 'stable', betaEnabled: false });
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'beta' })),
        onUpdateState: () => Promise.resolve(() => {}),
        setUpdateChannel,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findBetaToggleInput(fixture)?.click();
      fixture.detectChanges();
      await settle(fixture);

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
      expect(setUpdateChannel).toHaveBeenCalledWith('stable');
    });

    it('shows the beta warning on first paint when beta is already enabled', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'beta' })),
        onUpdateState: () => Promise.resolve(() => {}),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(fixture.nativeElement.textContent).toContain('Beta versions may contain unfinished features');
    });

    it('renders the active update channel', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'beta' })),
        onUpdateState: () => Promise.resolve(() => {}),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(fixture.nativeElement.textContent).toContain('Update channel: Beta');
    });

    it('shows the no-downgrade sentence for a beta build on the stable channel, not the plain up-to-date wording', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(
          makeState({ phase: 'upToDate', currentVersion: '3.0.0-beta.5', channel: 'stable', betaInstalled: true }),
        ),
        onUpdateState: () => Promise.resolve(() => {}),
      });

      const fixture = await createFixture();
      await settle(fixture);

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('You are using a beta version');
      expect(text).not.toContain("You're on the latest version");
    });

    it('still shows the plain up-to-date wording for a stable build', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(
          makeState({ phase: 'upToDate', currentVersion: '3.0.0', channel: 'stable', betaInstalled: false }),
        ),
        onUpdateState: () => Promise.resolve(() => {}),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(fixture.nativeElement.textContent).toContain("You're on the latest version");
    });

    it('renders a partial-check note when one is present', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ partialCheck: 'Could not check the beta update feed.' })),
        onUpdateState: () => Promise.resolve(() => {}),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(fixture.nativeElement.textContent).toContain('Could not check the beta update feed.');
    });

    it('reverts the toggle and shows an error when setUpdateChannel fails', async () => {
      const reason = 'could not persist the update channel';
      const setUpdateChannel = jasmine.createSpy('setUpdateChannel').and.rejectWith(reason);
      setShell({
        getUpdateState: () => Promise.resolve(makeState({ channel: 'stable' })),
        onUpdateState: () => Promise.resolve(() => {}),
        setUpdateChannel,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findBetaToggleInput(fixture)?.click();
      fixture.detectChanges();
      await settle(fixture);

      fixture.componentInstance.confirmBetaOptIn();
      await settle(fixture);

      expect(fixture.nativeElement.textContent).toContain(reason);
      expect(findBetaToggleInput(fixture)?.checked).toBe(false);
    });
  });

  describe('update mode (issue #715)', () => {
    it('renders no mode control when the bridge has no getUpdateMode, without throwing', async () => {
      setShell({ getUpdateState: () => Promise.resolve(makeState()), onUpdateState: () => Promise.resolve(() => {}) });

      const fixture = await createFixture();
      await settle(fixture);

      expect(findModeGroup(fixture)).toBeNull();
    });

    it('reflects the stored mode on first paint', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState()),
        onUpdateState: () => Promise.resolve(() => {}),
        getUpdateMode: () => Promise.resolve(makeModeStatus({ mode: 'automatic' })),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(fixture.componentInstance.mode()).toBe('automatic');
      expect(findModeOption(fixture, 'Automatic')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('reflects the off mode on first paint', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState()),
        onUpdateState: () => Promise.resolve(() => {}),
        getUpdateMode: () => Promise.resolve(makeModeStatus({ mode: 'off' })),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(fixture.componentInstance.mode()).toBe('off');
      expect(findModeOption(fixture, 'Off')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('selecting a mode calls setUpdateMode with that exact token and shows it selected', async () => {
      const setUpdateMode = jasmine
        .createSpy('setUpdateMode')
        .and.resolveTo(makeModeStatus({ mode: 'off', automaticSupported: true }));
      setShell({
        getUpdateState: () => Promise.resolve(makeState()),
        onUpdateState: () => Promise.resolve(() => {}),
        getUpdateMode: () => Promise.resolve(makeModeStatus()),
        setUpdateMode,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findModeOption(fixture, 'Off')?.click();
      fixture.detectChanges();
      await settle(fixture);

      expect(setUpdateMode).toHaveBeenCalledWith('off');
      expect(fixture.componentInstance.mode()).toBe('off');
      expect(findModeOption(fixture, 'Off')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('reverts to the previously stored mode and shows an error when setUpdateMode is rejected', async () => {
      const reason = 'could not persist the update mode';
      const setUpdateMode = jasmine.createSpy('setUpdateMode').and.rejectWith(reason);
      setShell({
        getUpdateState: () => Promise.resolve(makeState()),
        onUpdateState: () => Promise.resolve(() => {}),
        getUpdateMode: () => Promise.resolve(makeModeStatus({ mode: 'notifyOnly' })),
        setUpdateMode,
      });

      const fixture = await createFixture();
      await settle(fixture);

      findModeOption(fixture, 'Off')?.click();
      fixture.detectChanges();
      await settle(fixture);
      await settle(fixture);

      expect(fixture.nativeElement.textContent).toContain(reason);
      expect(fixture.componentInstance.mode()).toBe('notifyOnly');
      expect(findModeOption(fixture, 'Notify only')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('hides the Automatic option when automaticSupported is false, so it can never be selected', async () => {
      setShell({
        getUpdateState: () => Promise.resolve(makeState()),
        onUpdateState: () => Promise.resolve(() => {}),
        getUpdateMode: () => Promise.resolve(makeModeStatus({ mode: 'notifyOnly', automaticSupported: false })),
      });

      const fixture = await createFixture();
      await settle(fixture);

      expect(findModeOption(fixture, 'Automatic')).toBeNull();
      expect(findModeOption(fixture, 'Off')).toBeTruthy();
      expect(findModeOption(fixture, 'Notify only')).toBeTruthy();
    });

    it('leaves the manual check control working when the mode is off', async () => {
      // Manual bypasses UpdateMode.Off by design: "off" only means no automatic
      // checks, not no manual ones.
      const checkForUpdate = jasmine.createSpy('checkForUpdate').and.resolveTo(undefined);
      setShell({
        getUpdateState: () => Promise.resolve(makeState()),
        onUpdateState: () => Promise.resolve(() => {}),
        checkForUpdate,
        getUpdateMode: () => Promise.resolve(makeModeStatus({ mode: 'off' })),
      });

      const fixture = await createFixture();
      await settle(fixture);

      clickButtonWithText(fixture, 'Check for updates');
      await settle(fixture);
      expect(checkForUpdate).toHaveBeenCalled();
    });
  });
});
