import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryApiService, InventoryTag } from './inventory-api.service';

@Component({
  selector: 'app-settings-tags-page',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule],
  templateUrl: './settings-tags-page.component.html',
  styleUrl: './settings-tags-page.component.scss'
})
export class SettingsTagsPageComponent {
  protected readonly tags = signal<InventoryTag[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly draftName = signal('');
  protected readonly draftColor = signal('#48ffb0');

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fetchTags().pipe(
      tap((response) => this.tags.set(response.tags)),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudieron cargar los tags.');
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected createTag(): void {
    const name = this.draftName().trim();
    if (!name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.api.createTag({ name, color: this.draftColor() }).pipe(
      tap((tag) => {
        this.upsertTag(tag);
        this.draftName.set('');
        this.draftColor.set('#48ffb0');
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo crear el tag.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected updateTag(tag: InventoryTag, patch: Partial<InventoryTag>): void {
    const next = { ...tag, ...patch };
    this.upsertTag(next);
    this.saving.set(true);
    this.api.updateTag(next.id, { name: next.name, color: next.color }).pipe(
      tap((updated) => this.upsertTag(updated)),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo guardar el tag.');
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected setDraftName(value: string): void {
    this.draftName.set(value);
  }

  protected setDraftColor(value: string): void {
    this.draftColor.set(value);
  }

  private upsertTag(tag: InventoryTag): void {
    this.tags.update((current) => [...current.filter((item) => item.id !== tag.id), tag].sort((left, right) => left.name.localeCompare(right.name)));
  }
}
