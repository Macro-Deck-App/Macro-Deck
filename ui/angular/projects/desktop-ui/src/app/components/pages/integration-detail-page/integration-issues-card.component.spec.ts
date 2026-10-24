import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { IpcIntegrationIssue } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ExternalLinkService } from '../../../services/external-link.service';
import { IntegrationIssuesCardComponent } from './integration-issues-card.component';

describe('IntegrationIssuesCardComponent help link', () => {
  let open: jasmine.Spy;

  beforeEach(() => {
    open = jasmine.createSpy('open');
    TestBed.configureTestingModule({
      imports: [IntegrationIssuesCardComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLinkService, useValue: { open } },
        {
          provide: ApiService,
          useValue: {
            getLocalization: async () => ({ culture: 'en', fallbackCulture: 'en', translations: {}, availableCultures: ['en'] }),
            onNotification: () => EMPTY,
            connectionStateSignal: signal('connected'),
          },
        },
      ],
    });
  });

  async function render(issues: IpcIntegrationIssue[]): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(IntegrationIssuesCardComponent);
    fixture.componentRef.setInput('issues', issues);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function helpButtons(element: HTMLElement): HTMLButtonElement[] {
    return [...element.querySelectorAll<HTMLButtonElement>('.issue-help button')];
  }

  it('opens the issue help page in the browser', async () => {
    const element = await render([{
      id: 'not-found',
      title: 'AutoHotkey was not found',
      severity: 'error',
      helpUrl: 'https://www.autohotkey.com/download/',
    }]);

    const buttons = helpButtons(element);
    expect(buttons.length).toBe(1);
    buttons[0].click();

    expect(open).toHaveBeenCalledOnceWith('https://www.autohotkey.com/download/');
  });

  it('offers no help link for an issue without one', async () => {
    const element = await render([{ id: 'plain', title: 'Something is off', severity: 'warning' }]);

    expect(helpButtons(element)).toEqual([]);
  });
});
