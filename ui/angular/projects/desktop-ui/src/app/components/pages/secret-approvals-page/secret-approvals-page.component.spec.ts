import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject, Subject } from 'rxjs';
import {
  ApproveSecretBlockResponse,
  GetSecretApprovalsResponse,
  SecretApprovalItem
} from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { SecretApprovalService } from '../../../services/secret-approval.service';
import { SecretApprovalsPageComponent } from './secret-approvals-page.component';

function changedAutomationStep(overrides: Partial<SecretApprovalItem> = {}): SecretApprovalItem {
  return {
    ownerKind: 'automation',
    ownerId: 'automation-1',
    ownerName: 'Morning lights',
    ownerContext: null,
    blockId: 'block-1',
    integrationId: 'http',
    actionId: 'http.request',
    actionName: 'Send HTTP request',
    fingerprint: 'fp-1',
    approvedBefore: true,
    parameters: [
      {
        name: 'url',
        label: 'URL',
        secret: false,
        approved: 'https://lights.local/on',
        current: 'https://elsewhere.example/collect',
        changed: true,
        alsoUsedBy: []
      },
      {
        name: 'method',
        label: null,
        secret: false,
        approved: 'POST',
        current: 'POST',
        changed: false,
        alsoUsedBy: []
      },
      {
        name: 'token',
        label: 'API token',
        secret: true,
        approved: null,
        current: null,
        changed: false,
        alsoUsedBy: ['Evening lights', 'Office lamp']
      }
    ],
    ...overrides
  };
}

describe('SecretApprovalsPageComponent', () => {
  let fixture: ComponentFixture<SecretApprovalsPageComponent>;
  let api: jasmine.SpyObj<ApiService>;

  async function render(...responses: GetSecretApprovalsResponse[]): Promise<void> {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getSecretApprovals', 'approveSecretBlock', 'onNotification']);
    api.getSecretApprovals.and.returnValues(...responses.map(response => Promise.resolve(response)));
    api.onNotification.and.callFake(() => new Subject());
    Object.assign(api, { connectionState$: new BehaviorSubject('disconnected') });

    TestBed.configureTestingModule({
      imports: [SecretApprovalsPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api }
      ]
    });

    fixture = TestBed.createComponent(SecretApprovalsPageComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function cards(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-testid="approval-card"]'));
  }

  function rows(card: HTMLElement): HTMLElement[] {
    return Array.from(card.querySelectorAll('[data-testid="approval-row"]'));
  }

  function approveButton(card: HTMLElement): HTMLButtonElement {
    return card.querySelector('.approval-card-footer button') as HTMLButtonElement;
  }

  it('says so when no step waits for approval', async () => {
    await render({ count: 0, items: [] });

    expect(cards().length).toBe(0);
    expect(text()).toContain('Nothing waiting for approval');
  });

  it('marks a step that was found without any record as not reviewed yet', async () => {
    await render({ count: 1, items: [changedAutomationStep({ approvedBefore: false, unknown: true })] });

    const [card] = cards();
    expect(card.textContent).toContain('Not reviewed yet');
    expect(card.textContent).not.toContain('New step');
  });

  it('shows who owns a changed step, what changed and that a secret stays hidden', async () => {
    await render({
      count: 2,
      items: [
        changedAutomationStep(),
        changedAutomationStep({
          ownerKind: 'widget',
          ownerId: 'widget-1',
          ownerName: 'Home',
          ownerContext: 'Streaming',
          blockId: 'block-2',
          approvedBefore: false,
          parameters: []
        })
      ]
    });

    const [automation, widget] = cards();
    expect(automation.textContent).toContain('Automation');
    expect(automation.textContent).toContain('Morning lights');
    expect(automation.textContent).toContain('Send HTTP request');
    expect(automation.textContent).toContain('Changed since approval');
    expect(widget.textContent).toContain('Widget');
    expect(widget.textContent).toContain('Home in Streaming');
    expect(widget.textContent).toContain('New step');

    const [url, method, token] = rows(automation);
    expect(url.classList).toContain('approval-row-changed');
    expect(url.querySelector('[data-testid="approval-approved"]')?.textContent?.trim()).toBe('https://lights.local/on');
    expect(url.querySelector('[data-testid="approval-current"]')?.textContent?.trim())
      .toBe('https://elsewhere.example/collect');
    expect(method.classList).not.toContain('approval-row-changed');
    expect(method.textContent).toContain('method');
    expect(token.querySelector('[data-testid="approval-current"]')?.textContent).toContain('Secret');
    expect(token.textContent).toContain('Also used by Evening lights and Office lamp');
  });

  it('never shows a value for a secret setting, even if one is sent', async () => {
    const step = changedAutomationStep();
    step.parameters[2] = { ...step.parameters[2], approved: 'old-token-value', current: 'new-token-value', changed: true };
    await render({ count: 1, items: [step] });

    expect(text()).not.toContain('old-token-value');
    expect(text()).not.toContain('new-token-value');
    const token = rows(cards()[0])[2];
    expect(token.querySelector('[data-testid="approval-approved"]')?.textContent?.trim()).toBe('Secret');
    expect(token.querySelector('[data-testid="approval-current"]')?.textContent).toContain('Secret');
  });

  it('approves the step as it was shown and removes it from the list', async () => {
    const step = changedAutomationStep();
    await render({ count: 1, items: [step] }, { count: 0, items: [] });
    api.approveSecretBlock.and.resolveTo({ success: true, error: null });

    approveButton(cards()[0]).click();
    await settle();

    expect(api.approveSecretBlock).toHaveBeenCalledOnceWith({
      ownerKind: 'automation',
      ownerId: 'automation-1',
      blockId: 'block-1',
      fingerprint: 'fp-1'
    });
    expect(cards().length).toBe(0);
    expect(TestBed.inject(SecretApprovalService).count()).toBe(0);
  });

  it('explains a step that changed while it was on screen and shows it again as it is now', async () => {
    const changedAgain = changedAutomationStep({
      fingerprint: 'fp-2',
      parameters: [{ ...changedAutomationStep().parameters[0], current: 'https://third.example/' }]
    });
    await render({ count: 1, items: [changedAutomationStep()] }, { count: 1, items: [changedAgain] });
    const rejection: ApproveSecretBlockResponse = {
      success: false,
      error: {
        code: 'APPROVAL_CHANGED',
        message: { $localized: { scope: 'macrodeck.app', key: 'Errors.Secrets.ApprovalChanged' } }
      }
    };
    api.approveSecretBlock.and.resolveTo(rejection);

    approveButton(cards()[0]).click();
    await settle();

    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent)
      .toContain('This step changed in the meantime. Review it again.');
    expect(api.getSecretApprovals).toHaveBeenCalledTimes(2);
    expect(cards().length).toBe(1);
    expect(text()).toContain('https://third.example/');
  });
});
