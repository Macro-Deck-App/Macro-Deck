import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiService, ToastService } from '@shared';
import { EMPTY } from 'rxjs';
import { FileSaveService } from '../../../../services/file-save.service';
import { AiClientsSettingsComponent } from './ai-clients-settings.component';

const ENDPOINT = 'http://127.0.0.1:5291/mcp';
const PLAINTEXT = 'mdmcp_0123456789abcdef';
const EXTENSION = new Blob(['zip'], { type: 'application/zip' });

describe('AiClientsSettingsComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let fileSave: jasmine.SpyObj<FileSaveService>;
  let toast: jasmine.SpyObj<ToastService>;

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
      'downloadClaudeDesktopExtension',
    ]);
    fileSave = jasmine.createSpyObj<FileSaveService>('FileSaveService', ['save']);
    fileSave.save.and.resolveTo({ status: 'saved', viaDialog: true });
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
    api.onNotification.and.returnValue(EMPTY);
    api.getMcpEndpoint.and.resolveTo({ available: true, url: ENDPOINT, contractVersion: '1', extensionAvailable: true });
    api.downloadClaudeDesktopExtension.and.resolveTo({ ok: true, blob: EXTENSION });
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
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: FileSaveService, useValue: fileSave },
        { provide: ToastService, useValue: toast },
      ],
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

  async function reveal(): Promise<ComponentFixture<AiClientsSettingsComponent>> {
    const f = await create();
    f.componentInstance.openCreateModal();
    f.componentInstance.createName.set('VS Code');
    await f.componentInstance.submitCreate();
    await settle(f);
    return f;
  }

  it('shows a created token once, with a client configuration that carries it', async () => {
    const f = await reveal();

    expect(api.createMcpToken).toHaveBeenCalledWith({ name: 'VS Code' });
    expect(f.componentInstance.revealed()?.plaintext).toBe(PLAINTEXT);
    f.componentInstance.selectClient('other');
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

  it('gives Claude Code a command and VS Code an mcp.json servers entry', async () => {
    const f = await reveal();

    expect(f.componentInstance.configSnippet()).toBe(
      `claude mcp add --transport http macro-deck ${ENDPOINT} --header "Authorization: Bearer ${PLAINTEXT}"`);

    f.componentInstance.selectClient('vscode');
    expect(JSON.parse(f.componentInstance.configSnippet())).toEqual({
      servers: { 'macro-deck': { type: 'http', url: ENDPOINT, headers: { Authorization: `Bearer ${PLAINTEXT}` } } },
    });
  });

  it('saves the Claude Desktop extension for the revealed token as macro-deck.mcpb', async () => {
    const f = await reveal();
    click(query(f, 'mcp-download-extension')[0]);
    await settle(f);

    expect(api.downloadClaudeDesktopExtension).toHaveBeenCalledWith('new', PLAINTEXT);
    expect(fileSave.save).toHaveBeenCalledWith(EXTENSION, 'macro-deck.mcpb');
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('says nothing when saving the extension is canceled and reports a failed save', async () => {
    const f = await reveal();
    fileSave.save.and.resolveTo({ status: 'canceled' });
    await f.componentInstance.downloadExtension();
    expect(toast.show).not.toHaveBeenCalled();

    fileSave.save.and.resolveTo({ status: 'error', message: 'Disk full' });
    await f.componentInstance.downloadExtension();
    expect(toast.show).toHaveBeenCalledWith('Disk full', { variant: 'error' });
  });

  it('reports the host refusing to build the extension without saving anything', async () => {
    const f = await reveal();
    api.downloadClaudeDesktopExtension.and.resolveTo({
      ok: false,
      response: { success: false, error: { code: 'token_revoked', message: 'This access token can no longer be used.' } },
    });

    await f.componentInstance.downloadExtension();

    expect(fileSave.save).not.toHaveBeenCalled();
    expect(toast.show).toHaveBeenCalledWith('This access token can no longer be used.', { variant: 'error' });
  });

  it('offers no Claude Desktop download where the host cannot build the extension', async () => {
    api.getMcpEndpoint.and.resolveTo({ available: true, url: ENDPOINT, contractVersion: '1', extensionAvailable: false });
    const f = await reveal();

    expect(query(f, 'mcp-download-extension').length).toBe(0);
    expect(query(f, 'mcp-client-config').length).toBe(1);
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
