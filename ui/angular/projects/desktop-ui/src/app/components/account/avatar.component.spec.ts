import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AvatarComponent } from './avatar.component';

describe('AvatarComponent', () => {
  let fixture: ComponentFixture<AvatarComponent>;

  async function create(src: string | null, name: string): Promise<ComponentFixture<AvatarComponent>> {
    const f = TestBed.createComponent(AvatarComponent);
    f.componentRef.setInput('src', src);
    f.componentRef.setInput('name', name);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AvatarComponent],
      providers: [provideZonelessChangeDetection()],
    });
  });

  it('renders the fallback immediately when there is no source', async () => {
    fixture = await create(null, 'Jane Doe');

    expect(fixture.nativeElement.querySelectorAll('img').length).toBe(0);
    expect(fixture.nativeElement.querySelector('.avatar__initials')?.textContent).toContain('JD');
  });

  it('falls back to initials instead of a broken image when the avatar 404s', async () => {
    fixture = await create('https://host/api/connect/avatar', 'Jane Doe');

    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    expect(img).toBeTruthy();

    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img[src="https://host/api/connect/avatar"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('.avatar__initials')?.textContent).toContain('JD');
  });

  it('renders the image (not the fallback) while a source is present and has not failed', async () => {
    fixture = await create('https://host/api/connect/avatar', 'Jane Doe');

    expect(fixture.nativeElement.querySelector('img')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.avatar__initials')).toBeNull();
  });

  it('resets the failed flag when the source changes', async () => {
    fixture = await create('https://host/api/connect/avatar', 'Jane Doe');
    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.componentRef.setInput('src', 'https://host/api/connect/avatar?v=2');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeTruthy();
  });

  it('falls back to a single two-letter initial pair for a one-word name', async () => {
    fixture = await create(null, 'Cher');

    expect(fixture.nativeElement.querySelector('.avatar__initials')?.textContent).toContain('CH');
  });
});
