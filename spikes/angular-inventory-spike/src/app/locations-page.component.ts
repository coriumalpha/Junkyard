import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, forkJoin, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { EntityMiniCardComponent } from './entity-mini-card.component';
import { InventoryCodePipe } from './inventory-code.pipe';
import { InventoryApiService, InventoryBoxOption, InventoryLocation, InventoryOptionsResponse } from './inventory-api.service';

interface LocationCard {
  location: InventoryLocation;
  boxes: InventoryBoxOption[];
}

@Component({
  selector: 'app-locations-page',
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
    InventoryCodePipe,
    RouterLink,
    EntityMiniCardComponent
  ],
  templateUrl: './locations-page.component.html',
  styleUrl: './locations-page.component.scss'
})
export class LocationsPageComponent {
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], locations: [], boxes: [] });
  protected readonly locations = signal<InventoryLocation[]>([]);
  protected readonly draftName = signal('');
  protected readonly draftDescription = signal('');
  protected readonly editingId = signal<number | null>(null);
  protected readonly editName = signal('');
  protected readonly editDescription = signal('');
  protected readonly cards = computed<LocationCard[]>(() => {
    const boxes = this.options().boxes;
    return this.locations().map((location) => ({
      location,
      boxes: boxes.filter((box) => box.locationName === location.name)
    }));
  });

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      options: this.api.fetchOptions(),
      locations: this.api.fetchLocations()
    }).pipe(
      tap(({ options, locations }) => {
        this.options.set(options);
        this.locations.set(locations.locations);
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudieron cargar las ubicaciones.'));
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected createLocation(): void {
    const name = this.draftName().trim();
    if (!name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.api.createLocation({ name, description: this.draftDescription() }).pipe(
      tap((location) => {
        this.locations.update((current) => [...current, location].sort((left, right) => left.name.localeCompare(right.name)));
        this.options.update((current) => ({
          ...current,
          locations: [...current.locations, { id: location.id, name: location.name }].sort((left, right) => left.name.localeCompare(right.name))
        }));
        this.draftName.set('');
        this.draftDescription.set('');
        this.notice.set('Ubicación creada.');
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo crear la ubicación.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected startEdit(location: InventoryLocation): void {
    this.editingId.set(location.id);
    this.editName.set(location.name);
    this.editDescription.set(location.description ?? '');
    this.error.set(null);
    this.notice.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.editName.set('');
    this.editDescription.set('');
  }

  protected saveEdit(location: InventoryLocation): void {
    const name = this.editName().trim();
    if (!name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.api.updateLocation(location.id, { name, description: this.editDescription() }).pipe(
      tap((updated) => {
        this.locations.update((current) => current.map((candidate) => candidate.id === updated.id ? updated : candidate).sort((left, right) => left.name.localeCompare(right.name)));
        this.options.update((current) => ({
          ...current,
          locations: current.locations.map((candidate) => candidate.id === updated.id ? { id: updated.id, name: updated.name } : candidate).sort((left, right) => left.name.localeCompare(right.name))
        }));
        this.cancelEdit();
        this.notice.set('Ubicación actualizada.');
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo actualizar la ubicación.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected archiveLocation(location: InventoryLocation): void {
    if (this.saving()) {
      return;
    }

    const suffix = location.boxesCount > 0
      ? ` Se moverán ${location.boxesCount} cajas directas a "Ubicación no asignada".`
      : '';
    if (!window.confirm(`Archivar ubicación "${location.name}"?${suffix}`)) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.api.archiveLocation(location.id).pipe(
      tap((response) => {
        this.locations.update((current) => current.filter((candidate) => candidate.id !== location.id));
        this.options.update((current) => ({
          ...current,
          locations: current.locations.filter((candidate) => candidate.id !== location.id)
        }));
        this.cancelEdit();
        this.notice.set(response.movedBoxes > 0
          ? `Ubicación archivada. ${response.movedBoxes} cajas movidas a Ubicación no asignada.`
          : 'Ubicación archivada.');
        this.reload();
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo archivar la ubicación.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  private errorMessage(error: unknown, fallback: string): string {
    const candidate = error as { error?: { error?: string }; message?: string };
    return candidate.error?.error ?? candidate.message ?? fallback;
  }
}
