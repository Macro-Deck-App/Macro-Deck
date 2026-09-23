import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { IconPackModel } from '../../../services/icon-pack.service';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';
import { PackEditDialogComponent, PackEditResult } from './pack-edit-dialog.component';

function pack(overrides: Partial<IconPackModel> = {}): IconPackModel {
  return {
    id: 'pack-1',
    name: 'Line Icons',
    isDefault: false,
    isReadOnly: false,
    sourceType: 'User',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    iconCount: 0,
    ownerKind: 'User',
    canDelete: true,
    aiAssets: 'NotDeclared',
    ...overrides,
  };
}

describe('PackEditDialogComponent', () => {
  let fixture: ComponentFixture<PackEditDialogComponent>;

  async function open(mode: 'create' | 'edit', existing: IconPackModel | null = null): Promise<void> {
    TestBed.configureTestingModule({
      imports: [PackEditDialogComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(PackEditDialogComponent);
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('pack', existing);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  const text = (key: string) => TestBed.inject(LocalizationService).translateKey(key);
  const aiSelect = () => document.querySelector<HTMLElement>('shared-select[name="packAiAssets"]')!;

  function submitAndCapture(): Promise<PackEditResult> {
    const saved = new Promise<PackEditResult>(resolve => fixture.componentInstance.save.subscribe(resolve));
    document.querySelector<HTMLFormElement>('.pack-form')!.requestSubmit();
    return saved;
  }

  it('creates a pack that declares nothing about AI unless the author chooses to', async () => {
    await open('create');
    (document.querySelector('.pack-form input') as HTMLInputElement).value = 'New Pack';
    document.querySelector('.pack-form input')!.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const result = await submitAndCapture();

    expect(result.aiAssets).toBe('NotDeclared');
  });

  it('shows the stored declaration and saves the one the author picks', async () => {
    await open('edit', pack({ aiAssets: 'Generated' }));
    expect(aiSelect().querySelector('button.control')!.textContent).toContain(text(AppStrings.IconPacks.AiGenerated));

    aiSelect().querySelector<HTMLButtonElement>('button.control')!.click();
    fixture.detectChanges();
    Array.from(document.querySelectorAll<HTMLButtonElement>('.sel-option'))
      .find(option => option.textContent?.trim() === text(AppStrings.IconPacks.AiNone))!
      .click();
    fixture.detectChanges();

    const result = await submitAndCapture();

    expect(result.aiAssets).toBe('None');
  });
});
