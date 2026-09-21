import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetThirdPartyNoticesResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ExternalLinkService } from '../../../../services/external-link.service';
import { ThirdPartyNoticesModalComponent } from './third-party-notices-modal.component';

describe('ThirdPartyNoticesModalComponent', () => {
  let api: jasmine.SpyObj<ApiService>;
  let externalLinks: jasmine.SpyObj<ExternalLinkService>;

  const notices: GetThirdPartyNoticesResponse = {
    components: [
      {
        name: 'Cronos',
        ecosystem: 'nuget',
        licenses: ['MIT'],
        declared: ['MIT'],
        url: 'https://github.com/HangfireIO/Cronos',
        platforms: [],
        note: null,
        textIds: [1],
      },
      {
        name: 'HarfBuzzSharp.NativeAssets.Linux',
        ecosystem: 'nuget',
        licenses: ['MIT'],
        declared: ['MIT'],
        url: null,
        platforms: ['Linux'],
        note: null,
        textIds: [1],
      },
      {
        name: 'rxjs',
        ecosystem: 'npm',
        licenses: ['Apache-2.0'],
        declared: ['Apache-2.0'],
        url: 'https://rxjs.dev',
        platforms: [],
        note: 'Copyright (c) RxJS contributors',
        textIds: [2],
      },
      {
        name: 'serde',
        ecosystem: 'cargo',
        licenses: ['MIT'],
        declared: ['MIT OR Apache-2.0'],
        url: null,
        platforms: [],
        note: null,
        textIds: [1],
      },
    ],
    texts: [
      { id: 1, content: 'MIT License\n\nPermission is hereby granted, free of charge' },
      { id: 2, content: 'Apache License\nVersion 2.0, January 2004' },
    ],
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  async function createFixture(
    load: () => Promise<GetThirdPartyNoticesResponse>,
    showAppImageNote = false,
  ): Promise<ComponentFixture<ThirdPartyNoticesModalComponent>> {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getThirdPartyNotices', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    api.getThirdPartyNotices.and.callFake(load);
    externalLinks = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);

    await TestBed.configureTestingModule({
      imports: [ThirdPartyNoticesModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ExternalLinkService, useValue: externalLinks },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ThirdPartyNoticesModalComponent);
    fixture.componentRef.setInput('showAppImageNote', showAppImageNote);
    await settle(fixture);
    return fixture;
  }

  async function settle(fixture: ComponentFixture<ThirdPartyNoticesModalComponent>): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function componentNames(fixture: ComponentFixture<ThirdPartyNoticesModalComponent>): string[] {
    return (Array.from(fixture.nativeElement.querySelectorAll('.notices__name')) as HTMLElement[])
      .map(node => node.textContent?.trim() ?? '');
  }

  async function search(fixture: ComponentFixture<ThirdPartyNoticesModalComponent>, text: string): Promise<void> {
    const input = fixture.nativeElement.querySelector('input[type="search"]') as HTMLInputElement;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  function toggleFor(fixture: ComponentFixture<ThirdPartyNoticesModalComponent>, name: string): HTMLButtonElement {
    const toggles = Array.from(fixture.nativeElement.querySelectorAll('.notices__toggle')) as HTMLElement[];
    const toggle = toggles.find(node => node.querySelector('.notices__name')?.textContent?.trim() === name);
    if (!(toggle instanceof HTMLButtonElement)) throw new Error(`no toggle for ${name}`);
    return toggle;
  }

  it('groups the components by ecosystem with a count per group', async () => {
    const fixture = await createFixture(() => Promise.resolve(notices));

    const headings = (Array.from(fixture.nativeElement.querySelectorAll('.notices__group-heading')) as HTMLElement[])
      .map(node => Array.from(node.children).map(part => part.textContent?.trim()));
    expect(headings).toEqual([
      ['NuGet packages', '2 components'],
      ['npm packages', '1 component'],
      ['Rust crates', '1 component'],
    ]);
    expect(fixture.nativeElement.textContent).toContain('Platforms: Linux');
  });

  it('filters by component name and by license', async () => {
    const fixture = await createFixture(() => Promise.resolve(notices));

    await search(fixture, 'harfbuzz');
    expect(componentNames(fixture)).toEqual(['HarfBuzzSharp.NativeAssets.Linux']);

    await search(fixture, 'apache');
    expect(componentNames(fixture)).toEqual(['rxjs', 'serde']);

    await search(fixture, 'no such component');
    expect(componentNames(fixture)).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('No components match your search.');
  });

  it('shows the license text and notice only once a row is expanded', async () => {
    const fixture = await createFixture(() => Promise.resolve(notices));
    const toggle = toggleFor(fixture, 'rxjs');

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-label')).toBe('Show license texts for rxjs');
    expect(fixture.nativeElement.querySelector('.notices__text')).toBeNull();

    toggle.click();
    await settle(fixture);

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.getAttribute('aria-label')).toBe('Hide license texts for rxjs');
    const texts = Array.from(fixture.nativeElement.querySelectorAll('.notices__text')) as HTMLElement[];
    expect(texts.map(node => node.textContent)).toEqual(['Apache License\nVersion 2.0, January 2004']);
    expect(fixture.nativeElement.querySelector('.notices__note')?.textContent).toBe('Copyright (c) RxJS contributors');

    toggle.click();
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('.notices__text')).toBeNull();
  });

  it('opens a component website through the external link service', async () => {
    const fixture = await createFixture(() => Promise.resolve(notices));

    const link = fixture.nativeElement.querySelector('[aria-label="Open the website of Cronos"]') as HTMLButtonElement;
    link.click();

    expect(externalLinks.open).toHaveBeenCalledWith('https://github.com/HangfireIO/Cronos');
  });

  it('offers a retry after a failed load and shows the licenses once it succeeds', async () => {
    let fail = true;
    const fixture = await createFixture(() => fail ? Promise.reject(new Error('404')) : Promise.resolve(notices));

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Could not load the open source licenses from the host.');
    expect(componentNames(fixture)).toEqual([]);

    fail = false;
    (alert.querySelector('button') as HTMLButtonElement).click();
    await settle(fixture);

    expect(api.getThirdPartyNotices).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(componentNames(fixture)).toContain('Cronos');
  });

  it('points Linux users at the AppImage library notices', async () => {
    const fixture = await createFixture(() => Promise.resolve(notices), true);

    expect(fixture.nativeElement.querySelector('.notices__footnote')?.textContent)
      .toContain('Linux AppImage builds include an additional notices file');
  });

  it('does not mention the AppImage notices on other platforms', async () => {
    const fixture = await createFixture(() => Promise.resolve(notices));

    expect(fixture.nativeElement.querySelector('.notices__footnote')).toBeNull();
  });
});
