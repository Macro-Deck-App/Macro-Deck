import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { CheckboxComponent } from './checkbox.component';

@Component({
  standalone: true,
  imports: [FormsModule, CheckboxComponent],
  template: `<shared-checkbox label="Enabled" [ngModel]="value()" (ngModelChange)="value.set($event)" />`,
})
class HostComponent {
  value = signal(false);
}

describe('CheckboxComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function box(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input.cb-box');
  }

  it('renders the label text', () => {
    expect(fixture.nativeElement.querySelector('.cb-label').textContent).toContain('Enabled');
  });

  it('reflects the bound value', async () => {
    fixture.componentInstance.value.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(box().checked).toBeTrue();
  });

  it('propagates user toggles through ngModel', () => {
    box().click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBeTrue();
  });

  it('disables the native input via setDisabledState', () => {
    const instance = fixture.debugElement.children[0].componentInstance as CheckboxComponent;
    instance.setDisabledState(true);
    fixture.detectChanges();
    expect(box().disabled).toBeTrue();
  });
});
