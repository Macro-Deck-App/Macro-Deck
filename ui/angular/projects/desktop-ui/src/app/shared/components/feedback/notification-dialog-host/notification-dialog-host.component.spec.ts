import { ApplicationRef, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import {
  ActiveNotification,
  NotificationClosedEvent,
  NotificationShownEvent,
  NotificationUpdatedEvent,
  UiNode,
} from '@macro-deck/runtime';

import { provideLocalizationTesting } from '../../../localization/localization-test-support';
import { ApiService } from '../../../transport';
import { NotificationDialogHostComponent } from './notification-dialog-host.component';

const FORM: UiNode = {
  id: 'form',
  type: 'ui.stack',
  children: [{ id: 'name', type: 'ui.text-field', properties: { text: 'Ada', events: ['change'] } }],
} as UiNode;

function dialog(overrides: Partial<ActiveNotification> = {}): ActiveNotification {
  return {
    id: 'n1',
    revision: 1,
    sourceCategory: 'plugin',
    sourceId: 'lights',
    sourceName: 'Lights',
    title: 'Rename the scene',
    message: 'Pick a name',
    severity: 'info',
    popup: 'dialog',
    inbox: false,
    dismissible: true,
    createdAt: '2026-07-28T10:00:00.000Z',
    actions: [{ id: 'save', label: 'Save', style: 'primary' }],
    content: FORM,
    ...overrides,
  };
}

describe('NotificationDialogHostComponent', () => {
  let shown: Subject<NotificationShownEvent>;
  let updated: Subject<NotificationUpdatedEvent>;
  let closed: Subject<NotificationClosedEvent>;
  let respond: jasmine.Spy;
  let fixture: ComponentFixture<NotificationDialogHostComponent>;

  const html = () => fixture.nativeElement as HTMLElement;
  const input = () => html().querySelector('.widget-text-field') as HTMLInputElement;
  const saveButton = () => html().querySelector('.nd-action button') as HTMLButtonElement;

  // The widget tree is drawn by an application-level render hook that detectChanges does not run.
  async function settle(): Promise<void> {
    const appRef = TestBed.inject(ApplicationRef);
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      appRef.tick();
      await fixture.whenStable();
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    }
  }

  beforeEach(async () => {
    shown = new Subject();
    updated = new Subject();
    closed = new Subject();

    TestBed.configureTestingModule({
      imports: [NotificationDialogHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });

    const api = TestBed.inject(ApiService);
    spyOn(api, 'onNotificationShown').and.returnValue(shown.asObservable());
    spyOn(api, 'onNotificationUpdated').and.returnValue(updated.asObservable());
    spyOn(api, 'onNotificationClosed').and.returnValue(closed.asObservable());
    spyOn(api, 'onActiveNotifications').and.returnValue(EMPTY);
    respond = spyOn(api, 'respondToNotification').and.resolveTo({ status: 'accepted' });

    fixture = TestBed.createComponent(NotificationDialogHostComponent);
    fixture.detectChanges();
  });

  it('keeps what was typed across a repaint and sends it with the pressed action', async () => {
    shown.next({ notification: dialog() });
    await settle();

    input().value = 'Grace';
    input().dispatchEvent(new Event('blur'));
    updated.next({ notification: dialog({ revision: 2, progress: { processed: 1, total: 4 } }) });
    await settle();

    expect(input().value).toBe('Grace');

    saveButton().click();
    await settle();

    expect(respond).toHaveBeenCalledOnceWith({ id: 'n1', actionId: 'save', values: { name: 'Grace' } });
  });

  it('stays open when the host calls the answer invalid and closes only on the closed event', async () => {
    shown.next({ notification: dialog() });
    await settle();
    respond.and.resolveTo({ status: 'invalid', message: 'host diagnostic text' });

    saveButton().click();
    await settle();

    expect(html().textContent).toContain('Rename the scene');
    expect(saveButton().disabled).toBeFalse();
    expect(html().querySelector('[role="alert"]')?.textContent)
      .toContain('This answer was not accepted. Check your input and try again.');
    expect(html().textContent).not.toContain('host diagnostic text');

    closed.next({ id: 'n1', revision: 2, reason: 'action', actionId: 'save' });
    await settle();

    expect(html().querySelector('shared-modal')).toBeNull();
  });

  it('clears the rejection once the user edits, and after an accepted answer', async () => {
    shown.next({ notification: dialog() });
    await settle();
    respond.and.resolveTo({ status: 'invalid' });
    saveButton().click();
    await settle();
    expect(html().querySelector('[role="alert"]')).not.toBeNull();

    input().value = 'Grace';
    input().dispatchEvent(new Event('blur'));
    await settle();
    expect(html().querySelector('[role="alert"]')).toBeNull();

    respond.and.resolveTo({ status: 'invalid' });
    saveButton().click();
    await settle();
    respond.and.resolveTo({ status: 'accepted' });
    saveButton().click();
    await settle();
    expect(html().querySelector('[role="alert"]')).toBeNull();
  });

  it('is not dismissed by a click on the overlay, only by its close button', async () => {
    shown.next({ notification: dialog({ content: undefined }) });
    await settle();

    // Past the modal's own open grace, which would swallow the click anyway.
    await new Promise<void>(resolve => setTimeout(resolve, 400));
    const overlay = html().querySelector('.modal-overlay') as HTMLElement;
    overlay.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    overlay.click();
    await new Promise<void>(resolve => setTimeout(resolve, 500));

    expect(respond).not.toHaveBeenCalled();
  });

  it('offers no way out of a dialog that is not dismissible', async () => {
    shown.next({ notification: dialog({ dismissible: false, content: undefined }) });
    await settle();

    expect(html().querySelector('.modal-close-btn')).toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await new Promise<void>(resolve => setTimeout(resolve, 250));

    expect(respond).not.toHaveBeenCalled();
  });

  it('dismisses with the close button as an answer without an action', async () => {
    shown.next({ notification: dialog({ content: undefined }) });
    await settle();

    (html().querySelector('.modal-close-btn') as HTMLButtonElement).click();
    await new Promise<void>(resolve => setTimeout(resolve, 250));

    expect(respond).toHaveBeenCalledOnceWith({ id: 'n1', actionId: null, values: {} });
  });
});
