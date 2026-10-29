import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiService } from '@shared';
import { EMPTY } from 'rxjs';
import { AiClientsSettingsComponent } from './ai-clients-settings.component';

const ENDPOINT = 'http://127.0.0.1:5291/mcp';
const PLAINTEXT = 'mdmcp_0123456789abcdef';

describe('AiClientsSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getMcpEndpoint',
      'getMcpTokens',
      'getMcpConnections',
      'createMcpToken',
      'revokeMcpToken',
      'deleteMcpToken',
      'getMcpPolicy',
      'setMcpPolicy',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    api.getMcpEndpoint.and.resolveTo({ available: true, url: ENDPOINT, contractVersion: '1' });
    api.getMcpTokens.and.resolveTo({
      tokens: [
        { id: 'live', name: 'Claude', createdAt: '2026-10-01T10:00:00Z', connectionCount: 1 },
        { id: 'old', name: 'Old laptop', createdAt: '2026-09-01T10:00:00Z', revokedAt: '2026-09-02T10:00:00Z', connectionCount: 0 },
      ],
    });
    api.getMcpConnections.and.resolveTo({
      connections: [{ tokenId: 'live', tokenName: 'Claude', clientName: 'claude-code', clientVersion: '2.1', connectedAt: '2026-10-28T09:00:00Z', lastSeenAt: '2026-10-28T09:05:00Z' }],
    });
    api.createMcpToken.and.resolveTo({
      success: true,
      token: { id: 'new', name: 'VS Code', createdAt: '2026-10-28T10:00:00Z', connectionCount: 0 },
      plaintext: PLAINTEXT,
    });
    api.revokeMcpToken.and.resolveTo({ success: true });
    api.getMcpPolicy.and.resolveTo({
      providers: [
        { integrationId: 'app.macro-deck.system', name: 'System', defaultMode: 'Allow', confirmedActions: ['Run command', 'Shut down'] },
        { integrationId: 'com.example.plugin', name: 'Plugin', defaultMode: 'Confirm', confirmedActions: [] },
      ],
    });
    api.setMcpPolicy.and.resolveTo({ success: true });
    api.deleteMcpToken.and.resolveTo({ success: true });

    await TestBed.configureTestingModule({
      imports: [AiClientsSettingsComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  async function settle(f: ComponentFixture<AiClientsSettingsComponent>): Promise<void> {
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  }

  async function create(): Promise<ComponentFixture<AiClientsSettingsComponent>> {
    const f = TestBed.createComponent(AiClientsSettingsComponent);
    await settle(f);
    await settle(f);
    return f;
  }

  function query(f: ComponentFixture<AiClientsSettingsComponent>, testId: string): HTMLElement[] {
    return Array.from(f.nativeElement.querySelectorAll(`[data-testid="${testId}"]`));
  }

  function click(element: HTMLElement): void {
    (element.querySelector('button') ?? element).click();
  }

  it('shows the server address, the tokens and the connected clients', async () => {
    const f = await create();

    expect(query(f, 'mcp-endpoint')[0]?.textContent).toContain(ENDPOINT);
    expect(query(f, 'mcp-token-row').map(row => row.textContent)).toEqual([
      jasmine.stringContaining('Claude'),
      jasmine.stringContaining('Old laptop'),
    ]);
    expect(query(f, 'mcp-connection-row')[0]?.textContent).toContain('claude-code 2.1');
  });

  it('offers revoke for a live token and remove only for a revoked one', async () => {
    const f = await create();

    expect(query(f, 'mcp-revoke-token').length).toBe(1);
    expect(query(f, 'mcp-remove-token').length).toBe(1);
  });

  it('shows a created token once, with a client configuration that carries it', async () => {
    const f = await create();
    f.componentInstance.createName.set('VS Code');
    await f.componentInstance.submitCreate();
    await settle(f);

    expect(api.createMcpToken).toHaveBeenCalledWith({ name: 'VS Code' });
    expect(f.componentInstance.revealed()?.plaintext).toBe(PLAINTEXT);
    expect(JSON.parse(f.componentInstance.configSnippet()).mcpServers['macro-deck']).toEqual({
      type: 'http',
      url: ENDPOINT,
      headers: { Authorization: `Bearer ${PLAINTEXT}` },
    });

    f.componentInstance.acknowledgeReveal();
    await settle(f);
    expect(f.componentInstance.revealed()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain(PLAINTEXT);
  });

  it('lists every provider and stores a changed policy', async () => {
    const f = await create();
    expect(query(f, 'mcp-policy-row').length).toBe(2);
    expect(query(f, 'mcp-policy-row')[0].textContent).toContain('Run command, Shut down');

    await f.componentInstance.changePolicy(f.componentInstance.mcp.providers()[1], 'Allow');
    expect(api.setMcpPolicy).toHaveBeenCalledWith({ integrationId: 'com.example.plugin', mode: 'Allow' });

    await f.componentInstance.changePolicy(f.componentInstance.mcp.providers()[1], '');
    expect(api.setMcpPolicy).toHaveBeenCalledWith({ integrationId: 'com.example.plugin', mode: null });
  });

  it('revokes a token only after confirmation', async () => {
    const f = await create();
    click(query(f, 'mcp-revoke-token')[0]);
    await settle(f);
    expect(api.revokeMcpToken).not.toHaveBeenCalled();

    await f.componentInstance.confirmRevoke();
    expect(api.revokeMcpToken).toHaveBeenCalledWith('live');
  });
});
