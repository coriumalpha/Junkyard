import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryAction, InventoryActionsResponse, InventoryApiService } from './inventory-api.service';
import { legacyUrl } from './legacy-url';

@Component({
  selector: 'app-actions-page',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    RouterLink
  ],
  templateUrl: './actions-page.component.html',
  styleUrl: './actions-page.component.scss'
})
export class ActionsPageComponent {
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly busyId = signal<number | null>(null);
  protected readonly data = signal<InventoryActionsResponse | null>(null);
  protected readonly openActions = computed(() => this.data()?.actions.filter((action) => action.status === 'Open') ?? []);
  protected readonly completedActions = computed(() => this.data()?.actions.filter((action) => action.status === 'Completed') ?? []);
  protected readonly legacyActionsUrl = legacyUrl('/Pendientes');

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.load();
  }

  protected complete(action: InventoryAction): void {
    if (this.busyId()) {
      return;
    }

    this.busyId.set(action.id);
    this.api.completeAction(action.id).pipe(
      tap((updated) => this.replaceAction(updated)),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo completar la acción.');
        return EMPTY;
      }),
      finalize(() => this.busyId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected reopen(action: InventoryAction): void {
    if (this.busyId()) {
      return;
    }

    this.busyId.set(action.id);
    this.api.reopenAction(action.id).pipe(
      tap((updated) => this.replaceAction(updated)),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo reabrir la acción.');
        return EMPTY;
      }),
      finalize(() => this.busyId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected legacyUrl(path: string | null | undefined): string {
    return legacyUrl(path);
  }

  private load(): void {
    this.api.fetchActions().pipe(
      tap((data) => {
        this.data.set(data);
        this.loading.set(false);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudieron cargar las acciones.');
        this.loading.set(false);
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private replaceAction(updated: InventoryAction): void {
    this.data.update((current) => {
      if (!current) {
        return current;
      }

      const actions = current.actions.map((action) => action.id === updated.id ? updated : action);
      return {
        openCount: actions.filter((action) => action.status === 'Open').length,
        completedCount: actions.filter((action) => action.status === 'Completed').length,
        actions
      };
    });
  }
}
