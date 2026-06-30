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

import { ColorPickerComponent } from './color-picker.component';
import { InventoryApiService, InventoryCondition } from './inventory-api.service';

@Component({
  selector: 'app-settings-conditions-page',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, ColorPickerComponent],
  templateUrl: './settings-conditions-page.component.html',
  styleUrl: './settings-conditions-page.component.scss'
})
export class SettingsConditionsPageComponent {
  protected readonly conditions = signal<InventoryCondition[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly draftName = signal('');
  protected readonly draftColor = signal('#8ad6ff');

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fetchConditions().pipe(
      tap((response) => this.conditions.set(response.conditions)),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudieron cargar los estados.'));
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected createCondition(): void {
    const name = this.draftName().trim();
    if (!name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.api.createCondition({ name, color: this.draftColor() }).pipe(
      tap((condition) => {
        this.upsertCondition(condition);
        this.draftName.set('');
        this.draftColor.set('#8ad6ff');
      }),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo crear el estado.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected updateCondition(condition: InventoryCondition, patch: Partial<InventoryCondition>): void {
    const next = { ...condition, ...patch };
    this.upsertCondition(next);
    this.saving.set(true);
    this.api.updateCondition(next.id, { name: next.name, color: next.color }).pipe(
      tap((updated) => this.upsertCondition(updated)),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo guardar el estado.'));
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected deleteCondition(condition: InventoryCondition): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api.deleteCondition(condition.id).pipe(
      tap(() => this.conditions.update((current) => current.filter((item) => item.id !== condition.id))),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo eliminar el estado. Sólo se pueden borrar estados sin ítems asignados.'));
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private upsertCondition(condition: InventoryCondition): void {
    this.conditions.update((current) => [...current.filter((item) => item.id !== condition.id), condition].sort((left, right) => left.name.localeCompare(right.name)));
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
