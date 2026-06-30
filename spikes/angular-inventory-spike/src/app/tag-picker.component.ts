import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
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
export class TagPickerComponent {
  @Input() tags: InventoryTag[] = [];
  @Input() value: number[] = [];
  @Input() label = 'Tags';
  @Input() compact = false;
  @Output() readonly valueChange = new EventEmitter<number[]>();

  protected readonly query = signal('');
  protected readonly selected = computed(() => {
    const ids = new Set(this.value);
    return this.tags.filter((tag) => ids.has(tag.id));
  });
  protected readonly filtered = computed(() => {
    const selectedIds = new Set(this.value);
    const terms = this.normalize(this.query()).split(' ').filter(Boolean);
    const candidates = this.tags.filter((tag) => !selectedIds.has(tag.id));
    if (!terms.length) {
      return candidates.slice(0, this.compact ? 10 : 18);
    }

    return candidates
      .filter((tag) => terms.every((term) => this.normalize(tag.name).includes(term)))
      .slice(0, this.compact ? 12 : 24);
  });

  protected toggle(id: number): void {
    const current = new Set(this.value);
    if (current.has(id)) {
      current.delete(id);
    } else {
      current.add(id);
    }
    this.valueChange.emit([...current].sort((left, right) => left - right));
  }

  protected clear(): void {
    this.valueChange.emit([]);
  }

  private normalize(value: string): string {
    return value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
  }
}
