import { Component, input, output, ChangeDetectionStrategy } from '@angular/core';

export interface SplashStep {
  label: string;
  state: 'done' | 'current' | 'pending';
}

@Component({
  selector: 'app-splash',
  standalone: true,
  imports: [],
  templateUrl: './splash.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./splash.component.scss']
})
export class SplashComponent {
  readonly status = input('');
  readonly progress = input<number | null>(null);
  readonly details = input<readonly string[]>([]);
  readonly steps = input<readonly SplashStep[]>([]);
  readonly overlay = input(false);
  readonly cancelLabel = input<string | null>(null);
  readonly cancel = output<void>();
}
