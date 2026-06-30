import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryAction, InventoryActionsResponse, InventoryApiService, InventoryItem, InventoryOptionsResponse } from './inventory-api.service';
import { legacyUrl } from './legacy-url';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';

type LinkMode = 'none' | 'box' | 'item';

@Component({
  selector: 'app-actions-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    SearchableSelectComponent,
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
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], locations: [], boxes: [] });
  protected readonly items = signal<InventoryItem[]>([]);
  protected readonly newTitle = signal('');
  protected readonly newDescription = signal('');
  protected readonly newPriority = signal(3);
  protected readonly linkMode = signal<LinkMode>('none');
  protected readonly linkedBoxId = signal<number | null>(null);
  protected readonly linkedItemId = signal<number | null>(null);
  protected readonly editingId = signal<number | null>(null);
  protected readonly editTitle = signal('');
  protected readonly editDescription = signal('');
  protected readonly editPriority = signal(3);
  protected readonly openActions = computed(() => this.data()?.actions.filter((action) => action.status === 'Open') ?? []);
  protected readonly completedActions = computed(() => this.data()?.actions.filter((action) => action.status === 'Completed') ?? []);
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: `${box.code} · ${box.name}`,
      hint: [box.containerTypeLabel, box.locationName, box.path].filter(Boolean).join(' · '),
      imageUrl: box.coverUrl,
      rotationDegrees: box.rotationDegrees,
      placeholder: box.code
    })));
  protected readonly itemOptions = computed<SearchableSelectOption[]>(() =>
    this.items().map((item) => ({
      value: item.id,
      label: `${item.code} · ${item.name}`,
      hint: [item.category, item.boxCode].filter(Boolean).join(' · '),
      imageUrl: item.coverUrl,
      rotationDegrees: item.rotationDegrees,
      placeholder: item.code
    })));
  protected readonly legacyActionsUrl = legacyUrl('/Pendientes');

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.load();
    this.api.fetchOptions().pipe(
      tap((options) => this.options.set(options)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
    this.api.fetchInventory({
      q: '',
      category: '',
      tagIds: [],
      box: '',
      boxIds: [],
      locationId: null,
      includeChildren: false,
      onlyConsumable: false,
      onlyOrphans: false,
      layout: 'flat',
      view: 'flat'
    }).pipe(
      tap((response) => this.items.set(response.items)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected setLinkMode(mode: LinkMode): void {
    this.linkMode.set(mode);
    if (mode !== 'box') {
      this.linkedBoxId.set(null);
    }
    if (mode !== 'item') {
      this.linkedItemId.set(null);
    }
  }

  protected createAction(): void {
    const title = this.newTitle().trim();
    if (!title || this.busyId()) {
      this.error.set('El título de la tarea es obligatorio.');
      return;
    }

    const input = {
      title,
      description: this.newDescription().trim(),
      priority: this.newPriority()
    };
    const mode = this.linkMode();
    const request = mode === 'box' && this.linkedBoxId()
      ? this.api.createBoxAction(this.linkedBoxId()!, input)
      : mode === 'item' && this.linkedItemId()
        ? this.api.createItemAction(this.linkedItemId()!, input)
        : this.api.createAction(input);

    this.busyId.set(-1);
    request.pipe(
      tap((action) => {
        this.data.update((current) => current ? {
          openCount: current.openCount + 1,
          completedCount: current.completedCount,
          actions: [action, ...current.actions]
        } : current);
        this.newTitle.set('');
        this.newDescription.set('');
        this.newPriority.set(3);
        this.setLinkMode('none');
        this.error.set(null);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo crear la tarea.');
        return EMPTY;
      }),
      finalize(() => this.busyId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
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

  protected startEdit(action: InventoryAction): void {
    this.editingId.set(action.id);
    this.editTitle.set(action.title);
    this.editDescription.set(action.description ?? '');
    this.editPriority.set(action.priority);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.editTitle.set('');
    this.editDescription.set('');
    this.editPriority.set(3);
  }

  protected saveEdit(action: InventoryAction): void {
    const title = this.editTitle().trim();
    if (!title || this.busyId()) {
      this.error.set('El título de la tarea es obligatorio.');
      return;
    }

    this.busyId.set(action.id);
    this.api.updateAction(action.id, {
      title,
      description: this.editDescription().trim(),
      priority: this.editPriority()
    }).pipe(
      tap((updated) => {
        this.replaceAction(updated);
        this.cancelEdit();
        this.error.set(null);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo actualizar la tarea.');
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
