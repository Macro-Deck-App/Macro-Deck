import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DropdownMenuComponent } from './dropdown-menu.component';

describe('DropdownMenuComponent', () => {
  let fixture: ComponentFixture<DropdownMenuComponent>;
  let component: DropdownMenuComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DropdownMenuComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(DropdownMenuComponent);
    component = fixture.componentInstance;
  });

  it('does not react to hover unless openOnHover is set', () => {
    component.onMouseEnter();
    expect(component.isOpen).toBeFalse();
  });

  it('opens on hover and closes after the grace delay when openOnHover is set', () => {
    jasmine.clock().install();
    try {
      component.openOnHover = true;

      component.onMouseEnter();
      expect(component.isOpen).toBeTrue();

      component.onMouseLeave();
      expect(component.isOpen).toBeTrue();

      jasmine.clock().tick(150);
      expect(component.isOpen).toBeFalse();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('cancels the pending close when the pointer re-enters within the grace delay', () => {
    jasmine.clock().install();
    try {
      component.openOnHover = true;

      component.onMouseEnter();
      component.onMouseLeave();
      component.onMouseEnter();

      jasmine.clock().tick(150);
      expect(component.isOpen).toBeTrue();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('keeps the panel open on a trigger click while in hover mode', () => {
    component.openOnHover = true;
    component.open();

    component.toggle();

    expect(component.isOpen).toBeTrue();
  });

  it('still toggles on click when not in hover mode', () => {
    component.toggle();
    expect(component.isOpen).toBeTrue();

    component.toggle();
    expect(component.isOpen).toBeFalse();
  });
});
