import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PluginAccessToken } from '@macro-deck/runtime';
import { ToastService } from '@shared';
import { DeveloperModeService } from '../../../../../../services/developer-mode.service';
import { PluginTokenService } from '../../../../../../services/plugin-token.service';
import { CredentialsSectionComponent } from './credentials-section.component';
import { provideLocalizationTesting } from '../../../../../../../testing/localization-test-support';

function token(id: string, overrides: Partial<PluginAccessToken> = {}): PluginAccessToken {
  return {
    id,
    name: `Token ${id}`,
    scopes: [],
    createdAt: '2026-08-01T00:00:00Z',
    registrations: [],
    activeSessionCount: 0,
    ...overrides,
  };
}

describe('CredentialsSectionComponent', () => {
  let fixture: ComponentFixture<CredentialsSectionComponent>;
  let serviceSpy: jasmine.SpyObj<PluginTokenService>;
  let tokensSignal: WritableSignal<PluginAccessToken[]>;
  let loadErrorSignal: WritableSignal<string | null>;

  function configure(tokens: PluginAccessToken[] = []): void {
    serviceSpy = jasmine.createSpyObj<PluginTokenService>(
      'PluginTokenService', ['load', 'createToken', 'revokeToken', 'deleteToken', 'terminateSession']);
    tokensSignal = signal(tokens);
    loadErrorSignal = signal<string | null>(null);
    Object.defineProperty(serviceSpy, 'tokens', { value: tokensSignal });
    Object.defineProperty(serviceSpy, 'sessions', { value: signal([]) });
    Object.defineProperty(serviceSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(serviceSpy, 'loadError', { value: loadErrorSignal });
    serviceSpy.load.and.resolveTo();
    serviceSpy.revokeToken.and.resolveTo({ success: true, terminatedSessions: 0 });
    serviceSpy.deleteToken.and.resolveTo({ success: true });

    const developerModeSpy = jasmine.createSpyObj<DeveloperModeService>('DeveloperModeService', ['ensureLoaded']);
    Object.defineProperty(developerModeSpy, 'enabled', { value: signal(true) });
    developerModeSpy.ensureLoaded.and.resolveTo();

    TestBed.configureTestingModule({
      imports: [CredentialsSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginTokenService, useValue: serviceSpy },
        { provide: DeveloperModeService, useValue: developerModeSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<CredentialsSectionComponent>> {
    const f = TestBed.createComponent(CredentialsSectionComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  // Issue #709: the section's own scroll container (`.plugin-tokens`, `overflow-y: auto`) can only
  // scroll while the host is a height-bounded flex child of the page's column.
  it('lays the section host out as a height-bounded flex column so its content can scroll', async () => {
    configure();
    fixture = await create();

    const host = getComputedStyle(fixture.nativeElement as HTMLElement);
    expect(host.display).toBe('flex');
    expect(host.flexDirection).toBe('column');
    expect(host.flexGrow).not.toBe('0');
    expect(host.minHeight).toBe('0px');
  });

  it('shows the empty state when there are no tokens', async () => {
    configure();
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.plugin-tokens-empty')).toBeTruthy();
  });

  it('renders one row per token with its name', async () => {
    configure([token('t1', { name: 'CI bot' }), token('t2', { name: 'Kiosk enroller' })]);
    fixture = await create();

    const rows = fixture.nativeElement.querySelectorAll('.plugin-tokens-row');
    expect(rows.length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('CI bot');
    expect(fixture.nativeElement.textContent).toContain('Kiosk enroller');
  });

  describe('create + one-time reveal', () => {
    it('shows the plaintext exactly once after a successful create, then clears it on acknowledge', async () => {
      configure();
      fixture = await create();
      serviceSpy.createToken.and.resolveTo({
        token: token('t1', { name: 'CI bot' }),
        plaintext: 'super-secret-value',
      });

      fixture.componentInstance.openCreateModal();
      fixture.componentInstance.createName.set('CI bot');
      await fixture.componentInstance.submitCreate();
      fixture.detectChanges();

      expect(fixture.componentInstance.revealed()?.plaintext).toBe('super-secret-value');
      expect(fixture.nativeElement.textContent).toContain('super-secret-value');

      fixture.componentInstance.acknowledgeReveal();
      fixture.detectChanges();

      expect(fixture.componentInstance.revealed()).toBeNull();
      expect(fixture.componentInstance.createModalOpen()).toBeFalse();
      expect(fixture.nativeElement.textContent).not.toContain('super-secret-value');
    });

    it('has no way to re-reveal the plaintext once acknowledged', async () => {
      configure();
      fixture = await create();
      serviceSpy.createToken.and.resolveTo({ token: token('t1'), plaintext: 'super-secret-value' });

      fixture.componentInstance.openCreateModal();
      fixture.componentInstance.createName.set('CI bot');
      await fixture.componentInstance.submitCreate();
      fixture.componentInstance.acknowledgeReveal();

      fixture.componentInstance.openCreateModal();
      fixture.detectChanges();

      expect(fixture.componentInstance.revealed()).toBeNull();
      expect(fixture.nativeElement.textContent).not.toContain('super-secret-value');
    });

    it('does not submit with an empty name', async () => {
      configure();
      fixture = await create();

      fixture.componentInstance.openCreateModal();
      fixture.componentInstance.createName.set('   ');
      await fixture.componentInstance.submitCreate();

      expect(serviceSpy.createToken).not.toHaveBeenCalled();
    });
  });

  describe('revoke', () => {
    it('requires confirmation before revoking a token', async () => {
      configure([token('t1')]);
      fixture = await create();

      fixture.componentInstance.requestRevoke(fixture.componentInstance.tokens()[0]);
      fixture.detectChanges();

      expect(fixture.componentInstance.revokeCandidate()).not.toBeNull();
      expect(serviceSpy.revokeToken).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

      await fixture.componentInstance.confirmRevoke();

      expect(serviceSpy.revokeToken).toHaveBeenCalledWith('t1');
      expect(fixture.componentInstance.revokeCandidate()).toBeNull();
    });

    it('toasts the error message when revoke fails', async () => {
      configure([token('t1')]);
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');
      serviceSpy.revokeToken.and.resolveTo({
        success: false, error: { code: 'nope', message: 'Token already revoked.' }, terminatedSessions: 0,
      });

      fixture.componentInstance.requestRevoke(fixture.componentInstance.tokens()[0]);
      await fixture.componentInstance.confirmRevoke();

      expect(toastSpy).toHaveBeenCalledWith('Token already revoked.', { variant: 'error' });
    });

    it('cancelling leaves the token untouched', async () => {
      configure([token('t1')]);
      fixture = await create();

      fixture.componentInstance.requestRevoke(fixture.componentInstance.tokens()[0]);
      fixture.componentInstance.cancelRevoke();

      expect(fixture.componentInstance.revokeCandidate()).toBeNull();
      expect(serviceSpy.revokeToken).not.toHaveBeenCalled();
    });
  });

  describe('remove a revoked credential', () => {
    function actionLabels(rowIndex: number): string[] {
      const rows: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.plugin-tokens-row'));
      return Array.from(rows[rowIndex].querySelectorAll('.plugin-tokens-actions button'))
        .map(button => (button.textContent ?? '').trim());
    }

    it('offers Remove and no Revoke on a revoked token', async () => {
      configure([token('t1', { revokedAt: '2026-08-02T00:00:00Z' })]);
      fixture = await create();

      expect(actionLabels(0)).toEqual(['Remove']);
    });

    it('offers Revoke and no Remove on a live token', async () => {
      configure([token('t1')]);
      fixture = await create();

      expect(actionLabels(0)).toEqual(['Revoke']);
    });

    it('deletes the token once on confirm, and the row goes with it', async () => {
      configure([token('t1', { revokedAt: '2026-08-02T00:00:00Z' })]);
      fixture = await create();
      serviceSpy.deleteToken.and.callFake(async (id: string) => {
        tokensSignal.update(list => list.filter(t => t.id !== id));
        return { success: true };
      });

      fixture.componentInstance.requestRemove(fixture.componentInstance.tokens()[0]);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

      await fixture.componentInstance.confirmRemove();
      fixture.detectChanges();

      expect(serviceSpy.deleteToken).toHaveBeenCalledOnceWith('t1');
      expect(fixture.nativeElement.querySelector('.plugin-tokens-row')).toBeFalsy();
    });

    it('keeps the row and surfaces the message when the host refuses the removal', async () => {
      configure([token('t1', { revokedAt: '2026-08-02T00:00:00Z' })]);
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');
      serviceSpy.deleteToken.and.resolveTo({
        success: false, error: { code: 'not_revoked', message: 'Revoke the token first.' },
      });

      fixture.componentInstance.requestRemove(fixture.componentInstance.tokens()[0]);
      await fixture.componentInstance.confirmRemove();
      fixture.detectChanges();

      expect(toastSpy).toHaveBeenCalledWith('Revoke the token first.', { variant: 'error' });
      expect(fixture.nativeElement.querySelector('.plugin-tokens-row')).toBeTruthy();
    });
  });
});
