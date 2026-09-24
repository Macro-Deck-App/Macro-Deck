import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetStoreCreatorGuidelinesResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { StoreGuidelinesModalComponent } from './store-guidelines-modal.component';

describe('StoreGuidelinesModalComponent', () => {
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  async function createFixture(
    ...responses: Array<() => Promise<GetStoreCreatorGuidelinesResponse>>
  ): Promise<ComponentFixture<StoreGuidelinesModalComponent>> {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getStoreCreatorGuidelines', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    let call = 0;
    api.getStoreCreatorGuidelines.and.callFake(() => responses[Math.min(call++, responses.length - 1)]());

    await TestBed.configureTestingModule({
      imports: [StoreGuidelinesModalComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(StoreGuidelinesModalComponent);
    await settle(fixture);
    return fixture;
  }

  async function settle(fixture: ComponentFixture<StoreGuidelinesModalComponent>): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function retryButton(fixture: ComponentFixture<StoreGuidelinesModalComponent>): HTMLButtonElement {
    const button = fixture.nativeElement.querySelector('.guidelines-failure button');
    if (!(button instanceof HTMLButtonElement)) throw new Error('no retry button');
    return button;
  }

  it('shows the guidelines the Platform publishes', async () => {
    const fixture = await createFixture(() => Promise.resolve({
      available: true,
      markdown: '## 1. Open Source\n\nPlugins must be open source.',
    }));

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Store guidelines');
    expect(text).toContain('1. Open Source');
    expect(text).toContain('Plugins must be open source.');
  });

  it('says the guidelines could not be loaded and loads them again on retry', async () => {
    const fixture = await createFixture(
      () => Promise.resolve({ available: false }),
      () => Promise.resolve({ available: true, markdown: 'Be nice.' }),
    );

    expect(fixture.nativeElement.textContent).toContain('The Store guidelines could not be loaded.');

    retryButton(fixture).click();
    await settle(fixture);

    expect(fixture.nativeElement.textContent).toContain('Be nice.');
    expect(fixture.nativeElement.textContent).not.toContain('could not be loaded');
  });

  it('treats an unreachable host like unavailable guidelines', async () => {
    const fixture = await createFixture(() => Promise.reject(new Error('offline')));

    expect(fixture.nativeElement.textContent).toContain('The Store guidelines could not be loaded.');
  });
});
