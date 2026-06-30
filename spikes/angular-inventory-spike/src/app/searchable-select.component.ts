import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelect, MatSelectModule } from '@angular/material/select';

export type SearchableSelectValue = number | string | null;

export interface SearchableSelectOption {
  value: number | string;
  label: string;
  hint?: string | null;
  imageUrl?: string | null;
  rotationDegrees?: number;
  placeholder?: string | null;
  icon?: string | null;
}

@Component({
  selector: 'app-searchable-select',
  standalone: true,
  imports: [CommonModule, FormsModule, MatFormFieldModule, MatIconModule, MatSelectModule],
  templateUrl: './searchable-select.component.html',
  styleUrl: './searchable-select.component.scss'
})
export class SearchableSelectComponent implements OnChanges {
  @Input() options: SearchableSelectOption[] = [];
  @Input() value: SearchableSelectValue | SearchableSelectValue[] = null;
  @Input() multiple = false;
  @Input() label = '';
  @Input() appearance: 'fill' | 'outline' = 'outline';
  @Input() placeholder = 'Buscar...';
  @Input() noneLabel: string | null = null;
  @Input() disabled = false;
  @Output() readonly valueChange = new EventEmitter<SearchableSelectValue | SearchableSelectValue[]>();
  @ViewChild(MatSelect) private readonly matSelect?: MatSelect;
  @ViewChild('searchInput') private readonly searchInput?: ElementRef<HTMLInputElement>;

  protected readonly query = signal('');
  private readonly optionsVersion = signal(0);
  protected readonly filteredOptions = computed(() => {
    this.optionsVersion();
    const tokens = this.normalize(this.query()).split(' ').filter(Boolean);
    if (tokens.length === 0) {
      return this.options;
    }

    const matches = this.options.filter((option) => {
      const haystack = this.normalize(`${option.label} ${option.hint ?? ''}`);
      return tokens.every((token) => haystack.includes(token));
    });

    if (!this.multiple || !Array.isArray(this.value) || this.value.length === 0) {
      return matches;
    }

    const selected = new Set(this.value);
    const pinned = this.options.filter((option) => selected.has(option.value) && !matches.includes(option));
    return [...pinned, ...matches];
  });
  protected readonly selectedSummary = computed(() => {
    this.optionsVersion();
    if (Array.isArray(this.value)) {
      if (this.value.length === 0) {
        return '';
      }

      if (this.value.length === 1) {
        return this.findOption(this.value[0])?.label ?? String(this.value[0]);
      }

      return `${this.value.length} seleccionados`;
    }

    if (this.value === null) {
      return this.noneLabel ?? '';
    }

    return this.findOption(this.value)?.label ?? String(this.value);
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['options'] || changes['value']) {
      this.optionsVersion.update((version) => version + 1);
    }
  }

  focus(): void {
    this.matSelect?.focus();
  }

  open(): void {
    if (!this.disabled) {
      this.matSelect?.open();
      this.focusSearch();
    }
  }

  protected updateValue(value: SearchableSelectValue | SearchableSelectValue[]): void {
    this.valueChange.emit(value);
  }

  protected clearSearch(): void {
    this.query.set('');
  }

  protected handleOpenedChange(opened: boolean): void {
    this.clearSearch();
    if (opened) {
      this.focusSearch();
    }
  }

  protected stopPanelEvent(event: Event): void {
    event.stopPropagation();
  }

  private focusSearch(): void {
    setTimeout(() => this.searchInput?.nativeElement.focus());
  }

  protected optionIcon(option: SearchableSelectOption): string {
    return option.icon ?? 'inventory_2';
  }

  protected optionPlaceholder(option: SearchableSelectOption): string {
    return (option.placeholder ?? option.label).trim().slice(0, 2).toUpperCase();
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    if (path.startsWith('http://') || path.startsWith('https://')) {
      return path;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  private findOption(value: SearchableSelectValue): SearchableSelectOption | undefined {
    return this.options.find((option) => option.value === value);
  }

  private normalize(value: string): string {
    return value
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLowerCase()
      .trim();
  }
}
