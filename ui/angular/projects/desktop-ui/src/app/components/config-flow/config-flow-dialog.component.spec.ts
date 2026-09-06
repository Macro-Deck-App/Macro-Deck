import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfigFlowStepDto } from '@macro-deck/runtime';
import { HOST_URL_RESOLVER } from '@shared';
import type { UiNode } from '@macro-deck/runtime';
import { ConfigFlowService } from '../../services/config-flow.service';
import { ExternalLinkService } from '../../services/external-link.service';
import { ConfigFlowDialogComponent } from './config-flow-dialog.component';

let setValues: Array<[string, unknown]>;

function seedEnglishChrome(): void {
  localStorage.setItem('md.localization.translations', JSON.stringify({
    'macrodeck:Common.Continue': 'Continue',
    'macrodeck:ConfigFlow.SetUpIntegration': 'Set up integration',
  }));
}

function makeFlowStub(step: ConfigFlowStepDto | null) {
  const values = signal<Record<string, unknown>>({});
  const storedSecretFields = signal<ReadonlySet<string>>(new Set());
  return {
    starting: signal(false),
    notSupported: signal(false),
    done: signal(false),
    waitingForAuth: signal(false),
    submitting: signal(false),
    step: signal(step),
    message: signal<string | null>(null),
    values,
    fieldErrors: signal<Record<string, string>>({}),
    storedSecretFields,
    canSubmit: signal(true),
    setValue: (name: string, value: unknown) => {
      setValues.push([name, value]);
      values.update(current => ({ ...current, [name]: value }));
    },
    clearStoredSecret: (name: string) => {
      storedSecretFields.update(current => {
        const next = new Set(current);
        next.delete(name);
        return next;
      });
    },
    submit: async () => {},
    start: async () => {},
    reset: () => {},
  };
}

function stepWith(links: ConfigFlowStepDto['links'], overrides: Partial<ConfigFlowStepDto> = {}): ConfigFlowStepDto {
  return { stepId: 'credentials', title: 'Connect', links, fields: [], ...overrides };
}

let openedLinks: string[];

function stepWithAdvanced(): ConfigFlowStepDto {
  return {
    stepId: 'link',
    title: 'Connect a Twitch account',
    fields: [],
    advancedFields: [{ name: 'clientId', type: 'string', label: 'Own Client ID (advanced)' } as never],
  };
}

async function renderWith(step: ConfigFlowStepDto | null): Promise<ComponentFixture<ConfigFlowDialogComponent>> {
  openedLinks = [];
  setValues = [];
  seedEnglishChrome();
  TestBed.configureTestingModule({
    imports: [ConfigFlowDialogComponent],
    providers: [
      provideZonelessChangeDetection(),
      { provide: ConfigFlowService, useValue: makeFlowStub(step) },
      { provide: ExternalLinkService, useValue: { open: (url: string) => openedLinks.push(url) } },
      { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
    ],
  });
  const fixture = TestBed.createComponent(ConfigFlowDialogComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  return fixture;
}

async function renderWithRoot(
  step: ConfigFlowStepDto | null,
  root: UiNode,
  canSubmit = true,
): Promise<{ fixture: ComponentFixture<ConfigFlowDialogComponent>; flow: ReturnType<typeof makeFlowStub> }> {
  openedLinks = [];
  setValues = [];
  seedEnglishChrome();
  const flow = makeFlowStub(step);
  flow.canSubmit = signal(canSubmit);
  TestBed.configureTestingModule({
    imports: [ConfigFlowDialogComponent],
    providers: [
      provideZonelessChangeDetection(),
      { provide: ConfigFlowService, useValue: flow },
      { provide: ExternalLinkService, useValue: { open: (url: string) => openedLinks.push(url) } },
      { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
    ],
  });
  const fixture = TestBed.createComponent(ConfigFlowDialogComponent);
  fixture.componentRef.setInput('root', root);
  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, flow };
}

describe('ConfigFlowDialogComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders no shared-ui-tree when no root is supplied (legacy path unchanged)', async () => {
    const fixture = await renderWith(stepWith([]));
    expect(fixture.nativeElement.querySelector('shared-ui-tree')).toBeNull();
  });

  it('renders a config tree in place of the legacy fields when a root is supplied', async () => {
    const root: UiNode = {
      id: 'flow',
      type: 'flow',
      properties: { title: 'Connect an account', events: ['submit', 'cancel'] },
      children: [
        {
          id: 'step',
          type: 'step',
          children: [
            { id: 'clientId', type: 'string', properties: { label: 'Client ID', literalOnly: true } },
            { id: 'note', type: 'prose', properties: { text: 'Use the app you registered.' } },
          ],
        },
      ],
    };

    const { fixture, flow } = await renderWithRoot(stepWith([], { fields: [] }), root, false);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('shared-config-field').length).toBe(0);
    expect(host.querySelector('[data-node-id="clientId"] input')).not.toBeNull();
    expect(host.textContent).toContain('Use the app you registered.');

    const cancelButton = Array.from(host.querySelectorAll('shared-button')).find(
      b => b.textContent?.trim() === 'Cancel',
    ) as HTMLElement;
    const continueButton = Array.from(host.querySelectorAll('shared-button')).find(
      b => b.textContent?.trim() === 'Continue',
    ) as HTMLElement;
    expect(cancelButton).toBeTruthy();
    expect(continueButton).toBeTruthy();
    expect(continueButton.querySelector('button')?.disabled).toBeTrue();

    flow.canSubmit = signal(true);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(continueButton.querySelector('button')?.disabled).toBeFalse();
  });

  it('renders one link per config-flow link using its own label', async () => {
    const fixture = await renderWith(stepWith([
      { label: 'Open Spotify Developer Dashboard', url: 'https://developer.spotify.com/dashboard' },
      { label: 'Documentation', url: 'https://example.com/docs' },
    ]));

    const links = fixture.nativeElement.querySelectorAll('a.cfd-doclink');
    expect(links.length).toBe(2);
    expect(links[0].textContent).toContain('Open Spotify Developer Dashboard');
    expect(links[0].getAttribute('href')).toBe('https://developer.spotify.com/dashboard');
    expect(links[1].textContent).toContain('Documentation');
    expect(links[1].getAttribute('href')).toBe('https://example.com/docs');
  });

  it('opens a link through the external-link service instead of navigating', async () => {
    // The desktop shell's WebView drops a target="_blank" navigation, so the click has to
    // be taken over and routed to the OS browser (issue #133).
    const fixture = await renderWith(stepWith([
      { label: 'Open Spotify Developer Dashboard', url: 'https://developer.spotify.com/dashboard' },
    ]));

    const link = fixture.nativeElement.querySelector('a.cfd-doclink') as HTMLAnchorElement;
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    link.dispatchEvent(event);

    expect(openedLinks).toEqual(['https://developer.spotify.com/dashboard']);
    expect(event.defaultPrevented).toBeTrue();
  });

  it('renders no link container when the step has no links', async () => {
    const fixture = await renderWith(stepWith([]));

    expect(fixture.nativeElement.querySelector('.cfd-links')).toBeNull();
    expect(fixture.nativeElement.querySelector('a.cfd-doclink')).toBeNull();
  });

  it('hides advanced fields behind a switch and shows them once it is on', async () => {
    const fixture = await renderWith(stepWithAdvanced());

    expect(fixture.nativeElement.querySelector('shared-toggle-switch')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('shared-config-field').length).toBe(0);

    fixture.componentInstance.onToggleAdvanced(true);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('shared-config-field').length).toBe(1);
  });

  it('shows no switch when the step declares no advanced fields', async () => {
    const fixture = await renderWith(stepWith([]));

    expect(fixture.nativeElement.querySelector('shared-toggle-switch')).toBeNull();
  });

  it('opens the section on its own when an advanced field already carries a value', async () => {
    const fixture = await renderWith(stepWithAdvanced());
    expect(fixture.nativeElement.querySelectorAll('shared-config-field').length).toBe(0);

    fixture.componentInstance['flow'].setValue('clientId', 'my-own-id');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('shared-config-field').length).toBe(1);
  });

  // Collapsing means "use the defaults", so what was typed must not be submitted invisibly.
  it('clears advanced values when the section is closed again', async () => {
    const fixture = await renderWith(stepWithAdvanced());
    fixture.componentInstance.onToggleAdvanced(true);
    fixture.componentInstance['flow'].setValue('clientId', 'my-own-id');
    setValues = [];

    fixture.componentInstance.onToggleAdvanced(false);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(setValues).toEqual([['clientId', '']]);
    expect(fixture.nativeElement.querySelectorAll('shared-config-field').length).toBe(0);
  });

  // Backward compat: a step with only a description (no values/instructions) must still render
  // exactly as it did before those fields existed.
  it('still renders a description-only step as a plain paragraph with no instructions component', async () => {
    const fixture = await renderWith(stepWith([], { description: 'Do the thing on the other site.' }));

    const description = fixture.nativeElement.querySelector('p.cfd-description');
    expect(description.textContent).toBe('Do the thing on the other site.');
    expect(fixture.nativeElement.querySelector('shared-config-flow-instructions')).toBeNull();
    expect(fixture.nativeElement.querySelector('shared-copy-value')).toBeNull();
  });

  it('passes standalone values and instructions through to their components', async () => {
    const fixture = await renderWith(stepWith([], {
      values: [{ label: 'Redirect URI', value: 'http://192.168.1.5:8080/callback' }],
      instructions: [
        { text: 'Open the developer dashboard.' },
        { text: 'Paste the redirect URI above into the app settings.' },
      ],
    }));

    expect(fixture.nativeElement.querySelectorAll('.cfd-values shared-copy-value').length).toBe(1);
    const instructionsEl = fixture.nativeElement.querySelector('shared-config-flow-instructions');
    expect(instructionsEl).not.toBeNull();
    expect(instructionsEl.querySelectorAll('li.cfi-item').length).toBe(2);
  });

  it('shows no Configuration label for a fields-only step (unchanged from before setup content existed)', async () => {
    const field = { name: 'clientId', type: 'string', label: 'Client ID' } as never;
    const fixture = await renderWith(stepWith([], { fields: [field] }));

    expect(fixture.nativeElement.querySelector('.cfd-config-label')).toBeNull();
    expect(fixture.nativeElement.querySelector('.cfd-config-separator')).toBeNull();
  });

  it('shows no Configuration label when the step has setup content but no fields', async () => {
    const fixture = await renderWith(stepWith([], { description: 'Do the thing.' }));

    expect(fixture.nativeElement.querySelector('.cfd-config-label')).toBeNull();
  });

  it('shows the Configuration label when the step has both setup content and fields', async () => {
    const field = { name: 'clientId', type: 'string', label: 'Client ID' } as never;
    const fixture = await renderWith(stepWith([], { description: 'Do the thing.', fields: [field] }));

    const label = fixture.nativeElement.querySelector('.cfd-config-label');
    expect(label).not.toBeNull();
    expect(label.textContent).toBe('Configuration');
    expect(fixture.nativeElement.querySelector('.cfd-config-separator')).not.toBeNull();
  });
});
