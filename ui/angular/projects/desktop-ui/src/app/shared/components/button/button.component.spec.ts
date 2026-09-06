import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ButtonComponent, ButtonSize } from './button.component';
import { ButtonGroupComponent, ButtonGroupSize } from './button-group.component';

function sizeClassOf(fixture: ComponentFixture<unknown>): string {
  const inner = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
  const variants = ['sb-primary', 'sb-secondary', 'sb-danger', 'sb-danger-ghost', 'sb-ghost', 'sb-add'];
  return [...inner.classList]
    .find(c => c.startsWith('sb-') && c !== 'sb-loading' && c !== 'sb-pressed' && !variants.includes(c))!;
}

describe('ButtonComponent', () => {
  function createFixture(): ComponentFixture<ButtonComponent> {
    TestBed.configureTestingModule({
      imports: [ButtonComponent],
      providers: [provideZonelessChangeDetection()],
    });
    return TestBed.createComponent(ButtonComponent);
  }

  function create(): ButtonComponent {
    return createFixture().componentInstance;
  }

  it('composes the host class from variant and size', () => {
    const button = create();
    button.variant = 'primary';
    button.size = 'compact';

    expect(button.hostClass).toBe('sb sb-primary sb-compact');
  });

  it('supports the dashed add variant', () => {
    const button = create();
    button.variant = 'add';

    expect(button.hostClass).toContain('sb-add');
  });

  it('supports the compact icon size', () => {
    const button = create();
    button.size = 'icon-compact';

    expect(button.hostClass).toContain('sb-icon-compact');
  });

  it('marks the loading state in the host class', () => {
    const button = create();
    button.loading = true;

    expect(button.hostClass).toContain('sb-loading');
  });

  it('falls back to md when no size is given and there is no group', () => {
    expect(create().hostClass).toContain('sb-md');
  });

  it('keeps a standalone icon button at the md icon size', () => {
    const button = create();
    button.size = 'icon';

    expect(button.hostClass).toBe('sb sb-secondary sb-icon');
  });

  it('honours iconOnly without a group', () => {
    const button = create();
    button.iconOnly = true;

    expect(button.hostClass).toBe('sb sb-secondary sb-icon');
  });

  it('marks a filling button on the host so the row can expand it', () => {
    TestBed.configureTestingModule({
      imports: [ButtonComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const fixture = TestBed.createComponent(ButtonComponent);
    fixture.componentRef.setInput('fill', true);
    fixture.detectChanges();

    // fill must not reach the inner <button>: the focus ring, disabled and loading
    // rules all hang off .sb and have to stay identical to a non-filling button.
    const inner = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(fixture.nativeElement.classList).toContain('sb-fill');
    expect(inner.classList).not.toContain('sb-fill');
    expect(sizeClassOf(fixture)).toBe('sb-md');
  });

  it('is not announced as a toggle when pressed is left unset', () => {
    const fixture = createFixture();
    fixture.detectChanges();

    const inner = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(inner.getAttribute('aria-pressed')).toBeNull();
  });

  it('renders aria-pressed="true" when pressed is set true', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('pressed', true);
    fixture.detectChanges();

    const inner = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(inner.getAttribute('aria-pressed')).toBe('true');
  });

  it('renders aria-pressed="false" when pressed is set false', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('pressed', false);
    fixture.detectChanges();

    const inner = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(inner.getAttribute('aria-pressed')).toBe('false');
  });
});

@Component({
  selector: 'app-group-host',
  standalone: true,
  imports: [ButtonComponent, ButtonGroupComponent],
  template: `
    <shared-button-group [size]="size()">
      @if (show()) {
        <shared-button [size]="declared()" [iconOnly]="iconOnly()">Label</shared-button>
      }
    </shared-button-group>
  `,
})
class GroupHostComponent {
  readonly size = signal<ButtonGroupSize>('md');
  readonly declared = signal<ButtonSize | undefined>(undefined);
  readonly iconOnly = signal(false);
  readonly show = signal(true);
}

describe('ButtonComponent inside a shared-button-group', () => {
  function host(): ComponentFixture<GroupHostComponent> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [GroupHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    return TestBed.createComponent(GroupHostComponent);
  }

  function render(setup: (h: GroupHostComponent) => void): string {
    const fixture = host();
    setup(fixture.componentInstance);
    fixture.detectChanges();
    return sizeClassOf(fixture);
  }

  it('renders a text button at the group size', () => {
    expect(render(h => h.size.set('md'))).toBe('sb-md');
    expect(render(h => h.size.set('compact'))).toBe('sb-compact');
  });

  it('renders an iconOnly button as the square form of the group size', () => {
    expect(render(h => { h.size.set('md'); h.iconOnly.set(true); })).toBe('sb-icon');
    expect(render(h => { h.size.set('compact'); h.iconOnly.set(true); })).toBe('sb-icon-compact');
  });

  it('re-maps a legacy size="icon" onto the group family', () => {
    expect(render(h => { h.size.set('compact'); h.declared.set('icon'); })).toBe('sb-icon-compact');
    expect(render(h => { h.size.set('md'); h.declared.set('icon-compact'); })).toBe('sb-icon');
  });

  it('falls back to the group family when a size contradicts it', () => {
    spyOn(console, 'error');

    expect(render(h => { h.size.set('compact'); h.declared.set('lg'); })).toBe('sb-compact');
    expect(console.error).toHaveBeenCalled();
  });

  it('re-renders when the group size changes', () => {
    // The zoneless / OnPush guard: the button is not a view child of the group, so
    // this only works because hostClass reads the group's size as a signal.
    const fixture = host();
    fixture.detectChanges();
    expect(sizeClassOf(fixture)).toBe('sb-md');

    fixture.componentInstance.size.set('compact');
    fixture.detectChanges();
    expect(sizeClassOf(fixture)).toBe('sb-compact');
  });

  it('resolves from inside an embedded view declared in the group', () => {
    expect(render(h => { h.size.set('compact'); h.show.set(true); })).toBe('sb-compact');
  });
});

@Component({
  selector: 'app-projecting-group',
  standalone: true,
  imports: [ButtonGroupComponent],
  template: '<shared-button-group size="compact"><ng-content></ng-content></shared-button-group>',
})
class ProjectingGroupComponent {}

@Component({
  selector: 'app-projection-host',
  standalone: true,
  imports: [ButtonComponent, ProjectingGroupComponent],
  template: '<app-projecting-group><shared-button>Label</shared-button></app-projecting-group>',
})
class ProjectionHostComponent {}

@Component({
  selector: 'app-nested-group-host',
  standalone: true,
  imports: [ButtonComponent, ButtonGroupComponent],
  template: `
    <shared-button-group size="md">
      <shared-button-group size="compact">
        <shared-button>Label</shared-button>
      </shared-button-group>
    </shared-button-group>
  `,
})
class NestedGroupHostComponent {}

describe('ButtonComponent group resolution boundaries', () => {
  function render(type: typeof ProjectionHostComponent | typeof NestedGroupHostComponent): string {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [type],
      providers: [provideZonelessChangeDetection()],
    });
    const fixture = TestBed.createComponent(type);
    fixture.detectChanges();
    return sizeClassOf(fixture);
  }

  it('does not inherit a group the button was only projected into', () => {
    // An element injector chains to where the element was *written*, so a projected
    // button never reaches the group. Documented so nobody drops `host: true`
    // trying to make this case work - it cannot.
    expect(render(ProjectionHostComponent)).toBe('sb-md');
  });

  it('resolves against the nearest group when they nest', () => {
    expect(render(NestedGroupHostComponent)).toBe('sb-compact');
  });
});

@Component({
  standalone: true,
  imports: [ButtonComponent],
  template: `
    <shared-button [disabled]="true" (click)="clicks = clicks + 1">Delete</shared-button>
  `,
})
class DisabledClickHostComponent {
  clicks = 0;
}

describe('ButtonComponent disabled clicks', () => {
  it('keeps the host from passing a click on to the consumer', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [DisabledClickHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const fixture = TestBed.createComponent(DisabledClickHostComponent);
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement.querySelector('shared-button');
    expect(host.classList).toContain('sb-disabled');
    expect(getComputedStyle(host).pointerEvents).toBe('none');
    expect(getComputedStyle(host.querySelector('button')!).pointerEvents).toBe('none');
    expect(fixture.componentInstance.clicks).toBe(0);
  });
});
