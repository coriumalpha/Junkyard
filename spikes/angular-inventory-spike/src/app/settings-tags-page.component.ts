import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { ColorPickerComponent } from './color-picker.component';
import { InventoryApiService, InventoryTag } from './inventory-api.service';

@Component({
  selector: 'app-settings-tags-page',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, ColorPickerComponent],
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
  protected readonly editingTag = signal<InventoryTag | null>(null);
  protected readonly editName = signal('');
  protected readonly editColor = signal('#48ffb0');

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
        this.error.set(this.describeError(error, 'No se pudieron cargar los tags.'));
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
        this.error.set(this.describeError(error, 'No se pudo crear el tag.'));
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
        this.error.set(this.describeError(error, 'No se pudo guardar el tag.'));
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected startEdit(tag: InventoryTag): void {
    this.editingTag.set(tag);
    this.editName.set(tag.name);
    this.editColor.set(tag.color);
    this.error.set(null);
  }

  protected cancelEdit(): void {
    this.editingTag.set(null);
    this.editName.set('');
    this.editColor.set('#48ffb0');
  }

  protected saveEdit(): void {
    const tag = this.editingTag();
    const name = this.editName().trim();
    if (!tag || !name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api.renameTag(tag.id, name).pipe(
      switchMap((renamed) => {
        const color = this.editColor();
        return color === renamed.color
          ? [renamed]
          : this.api.updateTag(renamed.id, { name: renamed.name, color });
      }),
      tap((updated) => {
        this.upsertTag(updated);
        this.cancelEdit();
      }),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo guardar el tag.'));
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected deleteTag(tag: InventoryTag): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api.deleteTag(tag.id).pipe(
      tap(() => this.tags.update((current) => current.filter((item) => item.id !== tag.id))),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo eliminar el tag. Sólo se pueden borrar tags sin ítems asignados.'));
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

  private describeError(error: unknown, fallback: string): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = (error as { error?: { error?: string } }).error;
      if (body?.error) {
        return body.error;
      }
    }

    return error instanceof Error ? error.message : fallback;
  }
}
