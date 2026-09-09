import { Component, computed, input, signal } from '@angular/core';
import { Marked } from 'marked';

export function renderDescription(text: string): string {
  // No raw HTML or remote image requests. Angular sanitizes the final HTML binding.
  return new Marked({ async: false, gfm: true, breaks: true,
    renderer: { html: () => '', image: ({text}) => text.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!)) }
  }).parse(text, {async:false}) as string;
}
@Component({
  selector: 'app-description-view', standalone: true,
  template: `@if (markdown() && text()) { <button class="reading-toggle" type="button" (click)="plain.set(!plain())" [attr.aria-pressed]="plain()">{{ plain() ? 'Ver con formato' : 'Ver texto sin formato' }}</button> }
    @if (markdown() && !plain()) { <div class="prose" [innerHTML]="html()"></div> }
    @else { <div class="prose plain">{{ text() }}</div> }`,
  styleUrl: './description-view.component.scss'
})
export class DescriptionViewComponent {
  readonly text = input<string>('');
  readonly markdown = input(false);
  protected readonly plain = signal(false);
  protected readonly html = computed(() => renderDescription(this.text()));
}
