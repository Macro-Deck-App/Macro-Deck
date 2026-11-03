import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { McpConfirmationService } from '../../../services/mcp-confirmation.service';
import { McpPendingConfirmation } from '../../../shared/transport/mcp-messages';
import { McpConfirmationDialogComponent } from './mcp-confirmation-dialog.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

function confirmation(overrides: Partial<McpPendingConfirmation> = {}): McpPendingConfirmation {
  return {
    id: 'c1',
    clientName: 'Claude Desktop',
    lines: ['Keyboard: Press key (F13)'],
    alwaysAllowable: true,
    createdAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
    ...overrides,
  };
}

describe('McpConfirmationDialogComponent', () => {
  let fixture: ComponentFixture<McpConfirmationDialogComponent>;
  let service: jasmine.SpyObj<McpConfirmationService>;
  let current: WritableSignal<McpPendingConfirmation | null>;

  async function create(request: McpPendingConfirmation | null): Promise<void> {
    service = jasmine.createSpyObj<McpConfirmationService>('McpConfirmationService',
      ['allow', 'decline', 'expireLocally']);
    current = signal(request);
    Object.defineProperty(service, 'current', { value: current });
    service.allow.and.resolveTo(true);
    service.decline.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [McpConfirmationDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: McpConfirmationService, useValue: service },
      ],
    });
    fixture = TestBed.createComponent(McpConfirmationDialogComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  function button(label: string): HTMLButtonElement {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.find(candidate => candidate.textContent?.trim() === label)!;
  }

  function checkbox(): HTMLInputElement | null {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('shows nothing while no AI client is waiting', async () => {
    await create(null);

    expect(text().trim()).toBe('');
  });

  it('names the client and what it wants to do', async () => {
    await create(confirmation());

    expect(text()).toContain('Claude Desktop');
    expect(text()).toContain('Keyboard: Press key (F13)');
  });

  it('allows only this time unless always allow is ticked', async () => {
    await create(confirmation());

    button('Allow').click();
    await settle();

    expect(service.allow).toHaveBeenCalledWith('c1', false);
  });

  it('allows for good when always allow is ticked', async () => {
    await create(confirmation());

    checkbox()!.click();
    await settle();
    button('Allow').click();
    await settle();

    expect(service.allow).toHaveBeenCalledWith('c1', true);
  });

  it('keeps always allow ticked when the same request is loaded again', async () => {
    await create(confirmation());

    checkbox()!.click();
    await settle();
    current.set(confirmation());
    await settle();
    button('Allow').click();
    await settle();

    expect(service.allow).toHaveBeenCalledWith('c1', true);
  });

  it('offers no always allow when the request also touches values the user must see each time', async () => {
    await create(confirmation({ alwaysAllowable: false }));

    expect(checkbox()).toBeNull();
  });

  it('declines', async () => {
    await create(confirmation());

    button('Decline').click();
    await settle();

    expect(service.decline).toHaveBeenCalledWith('c1');
  });

  it('says so when the answer could not be passed on', async () => {
    await create(confirmation());
    service.allow.and.resolveTo(false);

    button('Allow').click();
    await settle();

    expect(text()).toContain('Your answer could not be passed on.');
  });
});
