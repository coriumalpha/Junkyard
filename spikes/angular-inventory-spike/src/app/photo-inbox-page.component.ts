import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, distinctUntilChanged, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryApiService, PhotoInboxItem, PhotoInboxResponse, PhotoInboxStatus } from './inventory-api.service';
import { legacyUrl } from './legacy-url';

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
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './photo-inbox-page.component.html',
  styleUrl: './photo-inbox-page.component.scss'
})
export class PhotoInboxPageComponent {
  protected readonly data = signal<PhotoInboxResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly statusOptions = STATUS_OPTIONS;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.route.queryParamMap.pipe(
      map((params) => this.parseStatus(params.get('status'))),
      distinctUntilChanged(),
      tap(() => {
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

  private parseStatus(value: string | null): PhotoInboxStatus {
    return value === 'Assigned' || value === 'Discarded' || value === 'All' ? value : 'Pending';
  }
}
