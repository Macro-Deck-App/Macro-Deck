import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import { IconModel } from '../../services/icon-pack.service';
import { IconTileComponent } from './icon-tile.component';

describe('IconTileComponent', () => {
  let fixture: ComponentFixture<IconTileComponent>;

  const icon: IconModel = {
    id: 'icon-1',
    packId: 'pack',
    name: 'wide icon',
    isAnimated: false,
    processingState: 'Ready',
    availableSizes: [128],
  };

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconImageUrl', 'onNotification']);
    apiSpy.getIconImageUrl.and.returnValue('http://host/icon.webp');
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [IconTileComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    fixture = TestBed.createComponent(IconTileComponent);
    fixture.componentRef.setInput('icon', icon);
    fixture.componentRef.setInput('size', 96);
    fixture.detectChanges();
  });

  it('renders the thumbnail as a 1:1 square regardless of the image aspect ratio', () => {
    const thumb = fixture.nativeElement.querySelector('.thumb') as HTMLElement;
    const rect = thumb.getBoundingClientRect();
    expect(rect.width).toBe(96);
    expect(rect.height).toBe(96);
  });

  it('fills the square thumbnail like an action button (cover, no letterboxing)', () => {
    const img = fixture.nativeElement.querySelector('.thumb img') as HTMLImageElement;
    expect(getComputedStyle(img).objectFit).toBe('cover');
  });

  it('rounds the thumbnail with the action-button radius token', () => {
    const thumb = fixture.nativeElement.querySelector('.thumb') as HTMLElement;
    document.documentElement.style.setProperty('--radius-lg', '12px');
    expect(getComputedStyle(thumb).borderRadius).toBe('12px');
    document.documentElement.style.removeProperty('--radius-lg');
  });

  it('keeps the fixed tile width for long icon names so grid rows cannot overflow', () => {
    document.documentElement.style.setProperty('--space-1', '4px');
    fixture.componentRef.setInput('icon', {
      ...icon,
      name: 'a very long icon name that must not stretch the tile beyond its fixed width',
    });
    fixture.detectChanges();

    const tile = fixture.nativeElement.querySelector('.tile') as HTMLElement;
    expect(tile.getBoundingClientRect().width).toBe(104);
    document.documentElement.style.removeProperty('--space-1');
  });
});
