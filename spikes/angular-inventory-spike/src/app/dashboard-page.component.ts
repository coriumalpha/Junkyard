import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';

import { DashboardConsumableGroup, DashboardMetric, DashboardResponse, InventoryApiService } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';

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
    MatProgressSpinnerModule,
    InventoryCodePipe
  ],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss'
})
export class DashboardPageComponent {
  protected readonly data = signal<DashboardResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly classedPercent = computed(() => {
    const data = this.data();
    return data && data.itemCount > 0 ? Math.round((data.classedItemCount / data.itemCount) * 100) : 0;
  });

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

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  protected firstLetter(value: string): string {
    return value.trim().slice(0, 1).toUpperCase() || '?';
  }

  protected consumableQuantity(group: DashboardConsumableGroup): string {
    return `${group.totalQuantity} ${group.unit ?? ''}`.trim();
  }

  protected consumableTargets(group: DashboardConsumableGroup): string {
    return `Min ${group.minStock ?? '-'} · Obj ${group.targetStock ?? '-'}`;
  }

  protected consumableTone(group: DashboardConsumableGroup): string {
    return group.status === 'Bajo mínimo' ? 'danger' : 'warn';
  }

  protected metricPercent(metric: DashboardMetric, metrics: DashboardMetric[]): number {
    const total = metrics.reduce((sum, row) => sum + row.count, 0);
    return total > 0 ? Math.max(3, Math.round((metric.count / total) * 100)) : 0;
  }

  protected modeLabel(label: string): string {
    switch (label) {
      case 'Individual':
        return 'Individual';
      case 'Fungible':
        return 'Fungible';
      case 'Kit':
        return 'Kit';
      case 'Lot':
        return 'Lote';
      default:
        return label;
    }
  }

  protected statusLabel(label: string): string {
    switch (label) {
      case 'Active':
        return 'Activos';
      case 'Quarantine':
        return 'Cuarentena';
      case 'Archived':
        return 'Archivados';
      default:
        return label;
    }
  }
}
