import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PendingPluginPairingRequest } from '@macro-deck/runtime';
import { PluginPairingService } from '../../../services/plugin-pairing.service';
import { PluginPairingDialogComponent } from './plugin-pairing-dialog.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function request(overrides: Partial<PendingPluginPairingRequest> = {}): PendingPluginPairingRequest {
  const expiresAt = new Date(Date.now() + 5 * 60 * 1000).toISOString();
  return {
    requestId: 'r1',
    pluginId: 'com.example.tools',
    displayName: 'Example Tools',
    client: { executablePath: '/usr/local/bin/example-tools', processId: 4242, sdkVersion: '1.4.0' },
    createdAt: new Date().toISOString(),
    expiresAt,
    replacesExistingRegistration: false,
    existingRegistrationOrigin: null,
    existingRegistrationCreatedAt: null,
    arrivedOnPublicListener: false,
    ...overrides,
  };
}

describe('PluginPairingDialogComponent', () => {
  let fixture: ComponentFixture<PluginPairingDialogComponent>;
  let pairingSpy: jasmine.SpyObj<PluginPairingService>;
  let currentSignal: WritableSignal<PendingPluginPairingRequest | null>;
  let queuedCountSignal: WritableSignal<number>;

  function configure(current: PendingPluginPairingRequest | null, queuedCount = 0): void {
    pairingSpy = jasmine.createSpyObj<PluginPairingService>(
      'PluginPairingService', ['approve', 'reject', 'expireLocally']);
    currentSignal = signal(current);
    queuedCountSignal = signal(queuedCount);
    Object.defineProperty(pairingSpy, 'current', { value: currentSignal });
    Object.defineProperty(pairingSpy, 'queuedCount', { value: queuedCountSignal });
    pairingSpy.approve.and.resolveTo(true);
    pairingSpy.reject.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [PluginPairingDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginPairingService, useValue: pairingSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<PluginPairingDialogComponent>> {
    const f = TestBed.createComponent(PluginPairingDialogComponent);
    f.detectChanges();
    await f.whenStable();
    return f;
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  function approveButton(): HTMLButtonElement {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.find(button => button.textContent?.includes('Approve'))!;
  }

  function checkbox(): HTMLInputElement | null {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('renders nothing when there is no current request', async () => {
    configure(null);
    fixture = await create();

    expect(fixture.nativeElement.querySelector('shared-modal')).toBeNull();
  });

  it('renders the plugin id, display name and executable path', async () => {
    configure(request());
    fixture = await create();

    expect(text()).toContain('com.example.tools');
    expect(text()).toContain('Example Tools');
    expect(text()).toContain('/usr/local/bin/example-tools');
  });

  it('labels the self-reported block as not verified by Macro Deck', async () => {
    configure(request());
    fixture = await create();

    expect(text()).toContain('Not verified by Macro Deck');
  });

  describe('without a replacement', () => {
    it('shows no warning and no checkbox, and approves with false', async () => {
      configure(request({ replacesExistingRegistration: false }));
      fixture = await create();

      expect(checkbox()).toBeNull();
      expect(text()).not.toContain('replaces');

      approveButton().click();
      await fixture.whenStable();

      expect(pairingSpy.approve).toHaveBeenCalledWith('r1', false);
    });
  });

  describe('with a replacement', () => {
    it('renders the warning, disables Approve until confirmed, then approves with true', async () => {
      configure(request({
        replacesExistingRegistration: true,
        existingRegistrationOrigin: 'developer-token',
        existingRegistrationCreatedAt: '2026-07-01T00:00:00Z',
      }));
      fixture = await create();

      expect(text()).toContain('replaces');
      const box = checkbox();
      expect(box).not.toBeNull();
      expect(approveButton().disabled).toBeTrue();

      box!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(approveButton().disabled).toBeFalse();

      approveButton().click();
      await fixture.whenStable();

      expect(pairingSpy.approve).toHaveBeenCalledWith('r1', true);
    });
  });

  it('shows an error and leaves the prompt open when approve rejects with a transport failure', async () => {
    configure(request());
    pairingSpy.approve.and.rejectWith(new Error('network error'));
    fixture = await create();

    approveButton().click();
    await fixture.whenStable();

    expect(text()).toContain('Could not approve this pairing request.');
    expect(fixture.nativeElement.querySelector('shared-modal')).not.toBeNull();
  });

  it('shows an error when reject rejects with a transport failure', async () => {
    configure(request());
    pairingSpy.reject.and.rejectWith(new Error('network error'));
    fixture = await create();

    const rejectButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes('Reject'))!;
    rejectButton.click();
    await fixture.whenStable();

    expect(text()).toContain('Could not reject this pairing request.');
  });

  describe('public listener note', () => {
    it('is shown only when arrivedOnPublicListener is true', async () => {
      configure(request({ arrivedOnPublicListener: true }));
      fixture = await create();

      expect(text()).toContain('network listener');
    });

    it('is omitted when arrivedOnPublicListener is false or absent', async () => {
      configure(request({ arrivedOnPublicListener: false }));
      fixture = await create();

      expect(text()).not.toContain('network listener');
    });
  });

  it('rejects the request when the modal is dismissed rather than leaving it pending', async () => {
    configure(request());
    fixture = await create();

    const closeButton = fixture.nativeElement.querySelector('.modal-close-btn') as HTMLButtonElement;
    expect(closeButton).not.toBeNull();
    closeButton.click();
    await new Promise(resolve => setTimeout(resolve, 200));
    await fixture.whenStable();

    expect(pairingSpy.reject).toHaveBeenCalledWith('r1');
  });

  it('shows the queued count when more than one request is pending', async () => {
    configure(request(), 2);
    fixture = await create();

    expect(text()).toContain('2 more waiting');
  });

  it('never binds a secret, code verifier or code challenge', async () => {
    configure(request());
    fixture = await create();

    const lower = text().toLowerCase();
    expect(lower).not.toContain('secret');
    expect(lower).not.toContain('verifier');
    expect(lower).not.toContain('challenge');
  });
});
