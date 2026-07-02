import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { InventoryTag } from './inventory-api.service';

@Component({
  selector: 'app-tag-picker',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule],
  templateUrl: './tag-picker.component.html',
  styleUrl: './tag-picker.component.scss'
})
export class TagPickerComponent implements OnChanges {
  @Input() tags: InventoryTag[] = [];
  @Input() value: number[] = [];
  @Input() label = 'Tags';
  @Input() compact = false;
  @Input() placeholder = 'Buscar tag...';
  @Output() readonly valueChange = new EventEmitter<number[]>();

  protected readonly query = signal('');
  private readonly tagsState = signal<InventoryTag[]>([]);
  private readonly valueState = signal<number[]>([]);
  protected readonly normalizedQuery = computed(() => this.normalize(this.query()));
  protected readonly sortedTags = computed(() =>
    [...this.tagsState()].sort((left, right) => left.name.localeCompare(right.name, 'es', { sensitivity: 'base' }))
  );
  protected readonly selected = computed(() => {
    const ids = new Set(this.valueState());
    return this.sortedTags().filter((tag) => ids.has(tag.id));
  });
  protected readonly filtered = computed(() => {
    const terms = this.normalizedQuery().split(' ').filter(Boolean);
    const candidates = this.sortedTags();
    if (!terms.length) {
      return candidates;
    }

    return candidates
      .filter((tag) => terms.every((term) => this.normalize(tag.name).includes(term)));
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['tags']) {
      this.tagsState.set(this.tags ?? []);
    }

    if (changes['value']) {
      this.valueState.set(this.value ?? []);
    }
  }

  protected toggle(id: number): void {
    const current = new Set(this.valueState());
    if (current.has(id)) {
      current.delete(id);
    } else {
      current.add(id);
    }
    const next = [...current].sort((left, right) => left - right);
    this.valueChange.emit(next);
    this.valueState.set(next);
  }

  protected clear(): void {
    this.valueChange.emit([]);
    this.valueState.set([]);
  }

  protected remove(id: number): void {
    const next = this.valueState().filter((tagId) => tagId !== id);
    this.valueChange.emit(next);
    this.valueState.set(next);
  }

  protected isSelected(id: number): boolean {
    return this.valueState().includes(id);
  }

  protected resultLabel(): string {
    const count = this.filtered().length;
    if (!this.normalizedQuery()) {
      return `${count} tags disponibles`;
    }

    return count === 1 ? '1 resultado' : `${count} resultados`;
  }

  private normalize(value: string): string {
    return value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
  }
}
