import { ChangeDetectionStrategy, Component, computed, effect, input, signal } from '@angular/core';

export type AvatarSize = 'sm' | 'md' | 'lg';

function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return '';
  }
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }
  return (words[0][0] + words[1][0]).toUpperCase();
}

@Component({
  selector: 'shared-avatar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './avatar.component.html',
  styleUrls: ['./avatar.component.scss'],
})
export class AvatarComponent {
  readonly src = input<string | null>(null);
  readonly name = input.required<string>();
  readonly size = input<AvatarSize>('md');

  protected readonly imageFailed = signal(false);

  protected readonly initials = computed(() => initialsOf(this.name()));

  constructor() {
    effect(() => {
      this.src();
      this.imageFailed.set(false);
    });
  }

  protected onImageError(): void {
    this.imageFailed.set(true);
  }
}
