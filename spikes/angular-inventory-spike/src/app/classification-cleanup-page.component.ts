import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryApiService, ItemClassificationCleanupItem, ItemClassificationCleanupReport } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';

type CleanupBlock = 'lotKit' | 'untyped' | 'quarantine';

@Component({
  selector: 'app-classification-cleanup-page',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatCardModule, MatChipsModule, MatIconModule, MatProgressSpinnerModule, InventoryCodePipe],
  templateUrl: './classification-cleanup-page.component.html',
  styleUrl: './classification-cleanup-page.component.scss'
})
export class ClassificationCleanupPageComponent {
  protected readonly report = signal<ItemClassificationCleanupReport | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly activeBlock = signal<CleanupBlock>('lotKit');

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fetchItemClassificationCleanup().pipe(
      tap((report) => this.report.set(report)),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo cargar el informe de limpieza.'));
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected classLabel(item: ItemClassificationCleanupItem): string {
    return [item.itemClassName, item.itemSubtypeName].filter(Boolean).join(' / ') || 'Sin clase';
  }

  protected setBlock(block: CleanupBlock): void {
    this.activeBlock.set(block);
  }

  protected isPriorityBatterySuggestion(item: ItemClassificationCleanupItem): boolean {
    return ['Pila / AA', 'Pila / AAA', 'Pila / CR2032', 'Pila / 18650']
      .some((needle) => item.suggestion?.includes(needle));
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
