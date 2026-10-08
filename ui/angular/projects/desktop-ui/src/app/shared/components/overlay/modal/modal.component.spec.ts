import { Component, provideZonelessChangeDetection, ChangeDetectionStrategy, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ModalComponent, dismissModal } from './modal.component';
import { provideLocalizationTesting } from '../../../localization/localization-test-support';
import { OverlayPanelComponent } from '../overlay-panel/overlay-panel.component';

describe('ModalComponent closing animation', () => {
  let fixture: ComponentFixture<ModalComponent>;
  let component: ModalComponent;

  beforeEach(() => {
    jasmine.clock().install();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(ModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  function overlay(): HTMLElement {
    return fixture.nativeElement.querySelector('.modal-overlay') as HTMLElement;
  }

  it('plays the exit animation before emitting close on the close button', () => {
    const closeSpy = jasmine.createSpy('close');
    component.close.subscribe(closeSpy);

    component.onCloseClick();
    fixture.detectChanges();

    expect(overlay().classList).toContain('closing');
    expect(closeSpy).not.toHaveBeenCalled();

    jasmine.clock().tick(150);
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('dismiss() plays the exit animation, then runs the action without emitting close', () => {
    const closeSpy = jasmine.createSpy('close');
    const action = jasmine.createSpy('action');
    component.close.subscribe(closeSpy);

    component.dismiss(action);
    fixture.detectChanges();

    expect(overlay().classList).toContain('closing');
    expect(action).not.toHaveBeenCalled();

    jasmine.clock().tick(150);
    expect(action).toHaveBeenCalledTimes(1);
    expect(closeSpy).not.toHaveBeenCalled();
  });

  it('dismiss() runs the action immediately when the exit animation already played', () => {
    component.onCloseClick();
    jasmine.clock().tick(150);

    const action = jasmine.createSpy('action');
    component.dismiss(action);

    expect(action).toHaveBeenCalledTimes(1);
  });

  it('ignores repeated close requests while the exit animation is playing', () => {
    const closeSpy = jasmine.createSpy('close');
    component.close.subscribe(closeSpy);

    component.onCloseClick();
    component.onCloseClick();
    jasmine.clock().tick(150);

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('closes with the exit animation on Escape', () => {
    const closeSpy = jasmine.createSpy('close');
    component.close.subscribe(closeSpy);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(overlay().classList).toContain('closing');
    expect(closeSpy).not.toHaveBeenCalled();

    jasmine.clock().tick(150);
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('adds the flush modifier to the body only when flush is set', () => {
    const content = (): HTMLElement =>
      fixture.nativeElement.querySelector('.modal-content') as HTMLElement;

    expect(content().classList).not.toContain('modal-content--flush');

    fixture.componentRef.setInput('flush', true);
    fixture.detectChanges();

    expect(content().classList).toContain('modal-content--flush');
  });
});

describe('dismissModal', () => {
  it('runs the action immediately when no modal instance is available', () => {
    const action = jasmine.createSpy('action');

    dismissModal(undefined, action);

    expect(action).toHaveBeenCalledTimes(1);
  });
});

@Component({
  standalone: true,
  imports: [ModalComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<shared-modal heading="Add action">Body</shared-modal>`,
})
class StaticHeadingHostComponent {}

describe('ModalComponent heading input (regression: no native title tooltip)', () => {
  let fixture: ComponentFixture<StaticHeadingHostComponent>;

  function host(): HTMLElement {
    return fixture.nativeElement.querySelector('shared-modal') as HTMLElement;
  }

  function dialog(): HTMLElement {
    return fixture.nativeElement.querySelector('.modal-dialog') as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StaticHeadingHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(StaticHeadingHostComponent);
    fixture.detectChanges();
  });

  it('renders the heading text', () => {
    const title = host().querySelector('.modal-title') as HTMLElement;
    expect(title.textContent?.trim()).toBe('Add action');
  });

  it('does not leak the heading onto the host as a native title tooltip', () => {
    // A static `title` attribute on <shared-modal> would both bind the input and stay on the
    // host element, showing a browser tooltip over the whole modal subtree; the input is named
    // `heading` precisely to avoid that (issue #233).
    expect(host().hasAttribute('title')).toBe(false);
  });

  it('exposes the visible heading as the dialog accessible name via aria-labelledby', () => {
    const labelledBy = dialog().getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();

    const labelElement = fixture.nativeElement.querySelector(`#${labelledBy}`) as HTMLElement;
    expect(labelElement).toBeTruthy();
    expect(labelElement.textContent?.trim()).toBe('Add action');
    expect(dialog().getAttribute('role')).toBe('dialog');
  });
});

@Component({
  standalone: true,
  imports: [ModalComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <shared-modal heading="Outer" [style.--modal-height]="'min(48.75rem, 90vh)'">
      <shared-modal heading="Inner" size="small">
        <p style="height: 40px; margin: 0">short</p>
      </shared-modal>
    </shared-modal>
  `,
})
class NestedModalHostComponent {}

describe('ModalComponent nested inside a sized modal', () => {
  it('does not inherit the outer dialog height', () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()] });
    const fixture = TestBed.createComponent(NestedModalHostComponent);
    fixture.detectChanges();

    const hosts = Array.from(fixture.nativeElement.querySelectorAll('shared-modal')) as HTMLElement[];
    expect(hosts.length).toBe(2);

    const [outer, inner] = hosts;
    expect(getComputedStyle(outer).getPropertyValue('--modal-height').trim()).toBe('min(48.75rem, 90vh)');
    expect(getComputedStyle(inner).getPropertyValue('--modal-height').trim()).toBe('auto');
  });
});

@Component({
  standalone: true,
  imports: [ModalComponent, OverlayPanelComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <button #anchor type="button">Add</button>
    <shared-overlay-panel [anchor]="anchor" [isOpen]="menuOpen()" (dismissed)="menuOpen.set(false)">
      Menu
    </shared-overlay-panel>
    @if (dialogOpen()) {
      <shared-modal heading="Dialog" (close)="dialogOpen.set(false)">
        @if (confirmOpen()) {
          <shared-modal heading="Confirm" (close)="confirmOpen.set(false)">Sure?</shared-modal>
        }
      </shared-modal>
    }
  `,
})
class DialogWithMenuHostComponent {
  readonly dialogOpen = signal(false);
  readonly confirmOpen = signal(false);
  readonly menuOpen = signal(false);
}

describe('ModalComponent Escape with other overlays open', () => {
  let fixture: ComponentFixture<DialogWithMenuHostComponent>;
  let host: DialogWithMenuHostComponent;

  beforeEach(() => {
    jasmine.clock().install();
    TestBed.configureTestingModule({
      imports: [DialogWithMenuHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(DialogWithMenuHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    jasmine.clock().uninstall();
  });

  function set(change: () => void): void {
    change();
    fixture.detectChanges();
  }

  function pressEscape(): void {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    jasmine.clock().tick(150);
    fixture.detectChanges();
  }

  it('closes a menu opened from the dialog first, even when the menu is rendered outside it', () => {
    set(() => host.dialogOpen.set(true));
    set(() => host.menuOpen.set(true));

    pressEscape();

    expect(host.menuOpen()).toBeFalse();
    expect(host.dialogOpen()).toBeTrue();

    pressEscape();

    expect(host.dialogOpen()).toBeFalse();
  });

  it('is not held open by a menu that was already open before the dialog', () => {
    set(() => host.menuOpen.set(true));
    set(() => host.dialogOpen.set(true));

    pressEscape();

    expect(host.dialogOpen()).toBeFalse();
  });

  it('closes only a confirmation opened inside the dialog', () => {
    set(() => host.dialogOpen.set(true));
    set(() => host.confirmOpen.set(true));

    pressEscape();

    expect(host.confirmOpen()).toBeFalse();
    expect(host.dialogOpen()).toBeTrue();
  });
});
