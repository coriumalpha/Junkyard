import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, distinctUntilChanged, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryApiService, InventoryOptionsResponse, PhotoInboxItem, PhotoInboxResponse, PhotoInboxStatus } from './inventory-api.service';
import { legacyUrl } from './legacy-url';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';

interface StatusOption {
  value: PhotoInboxStatus;
  label: string;
  icon: string;
}

const STATUS_OPTIONS: StatusOption[] = [
  { value: 'Pending', label: 'Pendientes', icon: 'pending_actions' },
  { value: 'Assigned', label: 'Asignadas', icon: 'task_alt' },
  { value: 'Discarded', label: 'Descartadas', icon: 'delete' },
  { value: 'All', label: 'Todas', icon: 'photo_library' }
];

@Component({
  selector: 'app-photo-inbox-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    SearchableSelectComponent,
    MatProgressSpinnerModule
  ],
  templateUrl: './photo-inbox-page.component.html',
  styleUrl: './photo-inbox-page.component.scss'
})
export class PhotoInboxPageComponent {
  protected readonly data = signal<PhotoInboxResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly busyPhotoId = signal<number | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadSourceBoxId = signal<number | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], locations: [], boxes: [] });
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: `${box.code} · ${box.name}`,
      hint: [box.containerTypeLabel, box.locationName, box.path].filter(Boolean).join(' · ')
    })));
  protected readonly statusOptions = STATUS_OPTIONS;
  private readonly currentStatus = signal<PhotoInboxStatus>('Pending');

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.route.queryParamMap.pipe(
      map((params) => this.parseStatus(params.get('status'))),
      distinctUntilChanged(),
      tap((status) => {
        this.currentStatus.set(status);
        this.loading.set(true);
        this.error.set(null);
      }),
      switchMap((status) => this.api.fetchPhotoInbox(status).pipe(
        tap((response) => this.data.set(response)),
        catchError((error: unknown) => {
          this.error.set(error instanceof Error ? error.message : 'No se pudo cargar la bandeja.');
          this.data.set(null);
          return EMPTY;
        }),
        finalize(() => this.loading.set(false))
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();

    this.api.fetchOptions().pipe(
      tap((options) => this.options.set(options)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected setStatus(status: PhotoInboxStatus): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { status }
    }).catch(() => undefined);
  }

  protected countFor(status: PhotoInboxStatus): number {
    const data = this.data();
    if (!data) {
      return 0;
    }

    if (status === 'Pending') {
      return data.pendingCount;
    }

    if (status === 'Assigned') {
      return data.assignedCount;
    }

    if (status === 'Discarded') {
      return data.discardedCount;
    }

    return data.photos.length;
  }

  protected isActive(status: PhotoInboxStatus): boolean {
    return (this.data()?.currentStatus ?? 'Pending') === status;
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  protected legacyUrl(path: string | null | undefined): string {
    return legacyUrl(path);
  }

  protected statusClass(photo: PhotoInboxItem): string {
    return `status status-${photo.status.toLowerCase()}`;
  }

  protected discard(photo: PhotoInboxItem): void {
    if (this.busyPhotoId()) {
      return;
    }

    this.busyPhotoId.set(photo.id);
    this.actionMessage.set(null);
    this.api.discardInboxPhoto(photo.id).pipe(
      tap(() => {
        this.actionMessage.set('Foto descartada.');
        this.reload();
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo descartar la foto.');
        return EMPTY;
      }),
      finalize(() => this.busyPhotoId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected restore(photo: PhotoInboxItem): void {
    if (this.busyPhotoId()) {
      return;
    }

    this.busyPhotoId.set(photo.id);
    this.actionMessage.set(null);
    this.api.restoreInboxPhoto(photo.id).pipe(
      tap(() => {
        this.actionMessage.set('Foto devuelta a pendientes.');
        this.reload();
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo restaurar la foto.');
        return EMPTY;
      }),
      finalize(() => this.busyPhotoId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected uploadFiles(event: Event): void {
    const input = event.target as HTMLInputElement | null;
    const files = Array.from(input?.files ?? []);
    if (files.length === 0 || this.uploading()) {
      return;
    }

    this.uploading.set(true);
    this.error.set(null);
    this.actionMessage.set(null);
    this.api.uploadInboxPhotos(files, this.uploadSourceBoxId()).pipe(
      tap((response) => {
        this.data.set(response.inbox);
        this.actionMessage.set(`Importadas ${response.imported} fotos.${response.rejected.length ? ` Rechazadas: ${response.rejected.length}.` : ''}`);
        if (input) {
          input.value = '';
        }
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudieron subir las fotos.');
        return EMPTY;
      }),
      finalize(() => this.uploading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private parseStatus(value: string | null): PhotoInboxStatus {
    return value === 'Assigned' || value === 'Discarded' || value === 'All' ? value : 'Pending';
  }

  private reload(): void {
    this.api.fetchPhotoInbox(this.currentStatus()).pipe(
      tap((response) => this.data.set(response)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }
}
