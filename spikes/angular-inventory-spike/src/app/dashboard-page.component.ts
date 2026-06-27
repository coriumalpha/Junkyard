import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';

import { DashboardResponse, InventoryApiService } from './inventory-api.service';
import { legacyUrl } from './legacy-url';

@Component({
  selector: 'app-dashboard-page',
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
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss'
})
export class DashboardPageComponent {
  protected readonly data = signal<DashboardResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.api.fetchDashboard().pipe(
      tap((response) => this.data.set(response)),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo cargar el dashboard.');
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected backendUrl(path: string): string {
    return legacyUrl(path);
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  protected firstLetter(value: string): string {
    return value.trim().slice(0, 1).toUpperCase() || '?';
  }
}
