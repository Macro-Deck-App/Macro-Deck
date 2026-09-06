import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import { ScreenCursorService } from '../../services/screen-cursor.service';
import { ShellCursorPosition } from '../../util/shell-bridge';
import { CursorPositionComponent } from './cursor-position.component';

describe('CursorPositionComponent', () => {
  let position: ReturnType<typeof signal<ShellCursorPosition | null>>;
  let release: jasmine.Spy;
  let cursor: Pick<ScreenCursorService, 'supported' | 'position' | 'track'>;

  function createFixture(supported: boolean): ComponentFixture<CursorPositionComponent> {
    cursor = { supported, position: position.asReadonly(), track: () => release };
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [CursorPositionComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ScreenCursorService, useValue: cursor },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    const fixture = TestBed.createComponent(CursorPositionComponent);
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<CursorPositionComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.trim() ?? '';
  }

  beforeEach(() => {
    position = signal<ShellCursorPosition | null>(null);
    release = jasmine.createSpy('release');
  });

  it('renders the screen coordinates', () => {
    const fixture = createFixture(true);
    position.set({ x: 1920, y: 540 });
    fixture.detectChanges();

    expect(text(fixture)).toBe('1920, 540');
  });

  it('renders negative coordinates of a monitor left of the primary one', () => {
    const fixture = createFixture(true);
    position.set({ x: -1280, y: -140 });
    fixture.detectChanges();

    expect(text(fixture)).toBe('-1280, -140');
  });

  it('shows a placeholder instead of collapsing while no position is known', () => {
    const fixture = createFixture(true);

    expect(text(fixture)).toBe('-, -');
  });

  it('renders nothing outside the desktop shell', () => {
    const fixture = createFixture(false);

    expect((fixture.nativeElement as HTMLElement).querySelector('.cursor-position')).toBeNull();
  });

  it('releases the tracker when it goes away', () => {
    const fixture = createFixture(true);

    expect(release).not.toHaveBeenCalled();

    fixture.destroy();

    expect(release).toHaveBeenCalled();
  });
});
