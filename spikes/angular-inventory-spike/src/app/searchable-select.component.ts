import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';

export type SearchableSelectValue = number | string | null;

export interface SearchableSelectOption {
  value: number | string;
  label: string;
  hint?: string | null;
}

@Component({
  selector: 'app-searchable-select',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatSelectModule],
  templateUrl: './searchable-select.component.html',
  styleUrl: './searchable-select.component.scss'
})
export class SearchableSelectComponent {
  @Input() options: SearchableSelectOption[] = [];
  @Input() value: SearchableSelectValue | SearchableSelectValue[] = null;
  @Input() multiple = false;
  @Input() placeholder = 'Buscar...';
  @Input() noneLabel: string | null = null;
  @Input() disabled = false;
  @Output() readonly valueChange = new EventEmitter<SearchableSelectValue | SearchableSelectValue[]>();

  protected readonly query = signal('');
  protected readonly filteredOptions = computed(() => {
    const tokens = this.normalize(this.query()).split(' ').filter(Boolean);
    if (tokens.length === 0) {
      return this.options;
    }

    return this.options.filter((option) => {
      const haystack = this.normalize(`${option.label} ${option.hint ?? ''}`);
      return tokens.every((token) => haystack.includes(token));
    });
  });

  protected updateValue(value: SearchableSelectValue | SearchableSelectValue[]): void {
    this.valueChange.emit(value);
  }

  protected clearSearch(): void {
    this.query.set('');
  }

  protected stopPanelEvent(event: Event): void {
    event.stopPropagation();
  }

  private normalize(value: string): string {
    return value
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLowerCase()
      .trim();
  }
}
