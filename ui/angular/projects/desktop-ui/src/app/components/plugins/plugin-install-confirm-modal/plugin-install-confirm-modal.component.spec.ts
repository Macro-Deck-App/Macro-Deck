import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PluginInstallActionResponse, PluginInstallWarning, PluginSignatureVerification } from '@macro-deck/runtime';
import { PluginInstallConfirmModalComponent } from './plugin-install-confirm-modal.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('PluginInstallConfirmModalComponent', () => {
  let fixture: ComponentFixture<PluginInstallConfirmModalComponent>;

  function inspection(overrides: Partial<PluginInstallActionResponse> = {}): PluginInstallActionResponse {
    return {
      success: true,
      pluginId: 'com.acme.deck-tools',
      version: '1.2.0',
      activated: false,
      rolledBack: false,
      warnings: [],
      publisher: { name: 'Acme Ltd' },
      signature: { verification: 'not_signed' },
      artifact: { name: 'Deck Tools', description: 'Does a thing.', supportedOnThisPlatform: true },
      ...overrides,
    };
  }

  function warning(overrides: Partial<PluginInstallWarning>): PluginInstallWarning {
    return { code: 'permissions_requested', severity: 'advisory', message: 'Wants clipboard access.', ...overrides };
  }

  async function setup(result: PluginInstallActionResponse): Promise<void> {
    TestBed.configureTestingModule({
      imports: [PluginInstallConfirmModalComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(PluginInstallConfirmModalComponent);
    fixture.componentRef.setInput('inspectionResult', result);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  function verifiedMarker(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[data-testid="plugin-signature-verified"]');
  }

  function fact(label: string): string {
    const terms = Array.from(fixture.nativeElement.querySelectorAll('dt')) as HTMLElement[];
    const index = terms.findIndex(term => term.textContent?.trim() === label);
    const values = Array.from(fixture.nativeElement.querySelectorAll('dd')) as HTMLElement[];
    return index < 0 ? '' : (values[index].textContent ?? '').trim();
  }

  function signingRow(): HTMLElement {
    const terms = Array.from(fixture.nativeElement.querySelectorAll('dt')) as HTMLElement[];
    const index = terms.findIndex(term => term.textContent?.trim() === 'Signing status');
    return (fixture.nativeElement.querySelectorAll('dd') as NodeListOf<HTMLElement>)[index];
  }

  function authorValue(): string {
    return fact('Author');
  }

  function installButton(): HTMLButtonElement {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.find(button => button.textContent?.includes('Install'))!;
  }

  it('warns that a plugin runs as a normal program and to install only what you trust', async () => {
    await setup(inspection());

    expect(text()).toContain('run as regular programs on your computer');
    expect(text()).toContain('trust its developer');
    expect(text()).toContain('may execute harmful code');
  });

  // The security-critical case: "well-formed but uncheckable" is not "valid", and neither is "absent".
  (['not_signed', 'unverified'] as PluginSignatureVerification[]).forEach(verification => {
    it(`presents a ${verification} signature as an unverified claim`, async () => {
      await setup(inspection({ signature: { verification } }));

      expect(verifiedMarker()).toBeNull();
      expect(text()).toContain('Acme Ltd');
      expect(text()).toContain('unverified claim');
      expect(text()).toContain('has not been reviewed by the Macro Deck Creator Portal');
      expect(text()).toContain('trust its developer');
    });
  });

  // Only an absent signature is the user's to overrule. A signature the host could not check is a
  // failure it refuses, so it must not reach the same "install anyway" affordance.
  it('allows an unsigned install', async () => {
    await setup(inspection({ signature: { verification: 'not_signed' } }));

    expect(installButton().disabled).toBeFalse();
  });

  it('refuses an install whose signature could not be checked', async () => {
    await setup(inspection({ signature: { verification: 'unverified' } }));

    expect(installButton().disabled).toBeTrue();
  });

  // "Not signed at all" and "signed but uncheckable" are different facts, and the warning has to say
  // which one it is - collapsing them is the same mistake as rendering `unverified` as verified. Each
  // asserts the absence of the other's wording, so one sentence covering both cannot pass.
  it('says an unsigned package is unsigned', async () => {
    await setup(inspection({ signature: { verification: 'not_signed' } }));

    expect(text()).toContain('is not signed');
    expect(text()).not.toContain('cannot verify its signature');
  });

  it('says a signature it cannot check is unverifiable rather than absent', async () => {
    await setup(inspection({ signature: { verification: 'unverified' } }));

    expect(text()).toContain('cannot verify its signature');
    expect(text()).not.toContain('is not signed');
  });

  it('names the publisher as verified and reviewed by the Creator Portal for a valid signature', async () => {
    await setup(inspection({ signature: { verification: 'valid' } }));

    expect(verifiedMarker()).not.toBeNull();
    expect(text()).toContain('Acme Ltd');
    expect(text()).toContain('signed');
    expect(text()).toContain('reviewed by the Macro Deck Creator Portal');
    expect(text()).not.toContain('may execute harmful code');
    // A reviewed plugin is still a program, so the trust reminder stays.
    expect(text()).toContain('publishers you trust');
  });

  it('falls back to unverified when the host reports no signature at all', async () => {
    await setup(inspection({ signature: null }));

    expect(verifiedMarker()).toBeNull();
    expect(text()).toContain('unverified claim');
  });

  it('does not present the plugin as its own publisher when the manifest names none', async () => {
    await setup(inspection({ publisher: null }));

    expect(authorValue()).toBe('Unknown');
    expect(verifiedMarker()).toBeNull();
    expect(text()).not.toContain('unverified claim');
  });

  it('states the signing status and what the artifact claims to be signed with', async () => {
    await setup(inspection({
      signature: {
        verification: 'unverified',
        message: 'This host cannot verify signatures yet.',
        algorithm: 'ed25519',
        keyId: 'portal-1',
      },
    }));

    expect(fact('Signing status')).toBe('Signed, but not verifiable');
    const row = signingRow();
    expect(row.getAttribute('title')).toContain('portal-1');
    expect(row.getAttribute('title')).toContain('ed25519');
  });

  it('calls an unsigned package unsigned rather than unverifiable', async () => {
    await setup(inspection({ signature: { verification: 'not_signed' } }));

    expect(fact('Signing status')).toBe('Unsigned');
  });

  it('stays open while the install runs and shows the primary button busy', async () => {
    await setup(inspection());
    let confirmed = 0;
    fixture.componentInstance.confirmed.subscribe(() => confirmed++);

    installButton().click();
    await fixture.whenStable();

    expect(confirmed).toBe(1);
    expect(fixture.nativeElement.querySelector('shared-modal')).not.toBeNull();

    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.sb-spinner')).not.toBeNull();
    expect(installButton().disabled).toBeTrue();
  });

  it('cannot be dismissed while the install is running', async () => {
    await setup(inspection());
    let cancelled = 0;
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();

    const close: HTMLButtonElement = fixture.nativeElement.querySelector('shared-modal button');
    close?.click();
    await fixture.whenStable();

    expect(cancelled).toBe(0);
  });

  it('refuses to confirm an artifact whose signature the host rejects', async () => {
    await setup(inspection({ signature: { verification: 'invalid', category: 'signature_invalid' } }));

    expect(installButton().disabled).toBeTrue();
    expect(text()).toContain('has been modified since it was signed');
  });

  // The reason has to name the actual failure: "modified since signing" and "not signed by a certificate
  // we recognise" are different problems with different responses, and one sentence covering both would
  // tell the user nothing they can act on. Asserting the absence of the other's wording means a single
  // sentence covering both cannot pass either case.
  it('says a revoked certificate was revoked', async () => {
    await setup(inspection({ signature: { verification: 'invalid', category: 'revoked' } }));

    expect(text()).toContain('has been revoked');
    expect(text()).not.toContain('modified since it was signed');
  });

  it('says an unrecognised certificate was not issued by Macro Deck', async () => {
    await setup(inspection({ signature: { verification: 'invalid', category: 'untrusted_root' } }));

    expect(text()).toContain('not by a certificate Macro Deck recognises');
    expect(text()).not.toContain('has been revoked');
  });

  it('still refuses when the host reports a failure category this build does not know', async () => {
    await setup(inspection({ signature: { verification: 'invalid', category: 'some_future_verdict' } }));

    expect(installButton().disabled).toBeTrue();
    expect(text()).toContain('could not establish that this package is trustworthy');
  });

  describe('unsigned consent', () => {
    function consentBox(): HTMLInputElement | null {
      return fixture.nativeElement.querySelector('.plugin-install-consent input');
    }

    async function setupWithChoice(result: PluginInstallActionResponse): Promise<void> {
      TestBed.configureTestingModule({
        imports: [PluginInstallConfirmModalComponent],
        providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
      });
      fixture = TestBed.createComponent(PluginInstallConfirmModalComponent);
      fixture.componentRef.setInput('inspectionResult', result);
      fixture.componentRef.setInput('allowUnsignedChoice', true);
      fixture.detectChanges();
      await fixture.whenStable();
    }

    it('holds the install until the user accepts that the package is unsigned', async () => {
      await setupWithChoice(inspection({ signature: { verification: 'not_signed' } }));

      expect(consentBox()).not.toBeNull();
      expect(installButton().disabled).toBeTrue();

      consentBox()!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(installButton().disabled).toBeFalse();
    });

    it('reports the consent it was given, so the caller cannot send it unasked', async () => {
      await setupWithChoice(inspection({ signature: { verification: 'not_signed' } }));
      const emitted: { allowUnsigned: boolean }[] = [];
      fixture.componentInstance.confirmed.subscribe(value => emitted.push(value));

      consentBox()!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      installButton().click();

      expect(emitted).toEqual([{ allowUnsigned: true }]);
    });

    it('never sends consent for a signed package, whatever the box was left at', async () => {
      await setupWithChoice(inspection({ signature: { verification: 'valid', trusted: true } }));
      const emitted: { allowUnsigned: boolean }[] = [];
      fixture.componentInstance.confirmed.subscribe(value => emitted.push(value));

      installButton().click();

      expect(consentBox()).toBeNull();
      expect(emitted).toEqual([{ allowUnsigned: false }]);
    });

    // A store download can never install unsigned, so offering the choice there would be a checkbox that
    // does nothing. The install stays available because the host is the one that refuses it.
    it('offers no consent affordance for a source that cannot carry it', async () => {
      await setup(inspection({ signature: { verification: 'not_signed' } }));

      expect(consentBox()).toBeNull();
      expect(installButton().disabled).toBeFalse();
    });
  });

  it('surfaces the installer warnings and says a blocking one stops the plugin starting', async () => {
    await setup(inspection({
      warnings: [
        warning({ message: 'Wants clipboard access.' }),
        warning({ code: 'dependency_unresolved', severity: 'blocking', message: 'Needs com.other.plugin.' }),
      ],
    }));

    expect(text()).toContain('Wants clipboard access.');
    expect(text()).toContain('Needs com.other.plugin.');
    expect(text()).toContain('will not start');
    expect(installButton().disabled).toBeFalse();
  });

  it('does not repeat the signature as an installer warning', async () => {
    await setup(inspection({
      warnings: [warning({ code: 'unsigned', severity: 'advisory', message: 'This artifact is not signed.' })],
    }));

    expect(text()).not.toContain('This artifact is not signed.');
  });

  it('names the plugin from its manifest rather than only by its id', async () => {
    await setup(inspection());

    expect(fact('Name')).toBe('Deck Tools');
    expect(fact('Package ID')).toBe('com.acme.deck-tools');
    expect(fact('Version')).toBe('1.2.0');
    expect(fact('Description')).toBe('Does a thing.');
  });

  it('falls back to the package id when the manifest details are missing', async () => {
    await setup(inspection({ artifact: null }));

    expect(fact('Name')).toBe('com.acme.deck-tools');
  });

  // "No build for this machine" is a claim, not a safe default. Asserting it because the host said
  // nothing would tell the user an artifact cannot run when it runs perfectly well.
  it('leaves out platform support entirely when the host did not report it', async () => {
    await setup(inspection({ artifact: null }));

    expect(text()).not.toContain('Supported on this platform');
    expect(text()).not.toContain('no build for this machine');
  });

  it('says whether the artifact can run on this machine', async () => {
    await setup(inspection());
    expect(fact('Supported on this platform')).toBe('Yes');
  });

  it('refuses an artifact that has no build for this machine', async () => {
    await setup(inspection({
      artifact: { name: 'Deck Tools', supportedOnThisPlatform: false },
    }));

    expect(fact('Supported on this platform')).toBe('No');
    expect(text()).toContain('no build for this machine');
    expect(installButton().disabled).toBeTrue();
  });

  it('renders an inline svg icon from the artifact', async () => {
    await setup(inspection({
      artifact: {
        name: 'Deck Tools',
        supportedOnThisPlatform: true,
        iconDataUri: 'data:image/svg+xml;base64,PHN2Zy8+',
      },
    }));

    const img: HTMLImageElement = fixture.nativeElement.querySelector('.plugin-install-icon img');
    expect(img).not.toBeNull();
    expect(img.getAttribute('src')).toBe('data:image/svg+xml;base64,PHN2Zy8+');
  });

  it('refuses an icon that is not a data image and falls back to the placeholder', async () => {
    await setup(inspection({
      artifact: {
        name: 'Deck Tools',
        supportedOnThisPlatform: true,
        iconDataUri: 'javascript:alert(1)',
      },
    }));

    expect(fixture.nativeElement.querySelector('.plugin-install-icon img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.plugin-install-icon .icon')).not.toBeNull();
  });
});
