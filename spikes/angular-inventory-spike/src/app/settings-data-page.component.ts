import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { CsvInventoryRow, InventoryApiService } from './inventory-api.service';

@Component({
  selector: 'app-settings-data-page',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatDividerModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './settings-data-page.component.html',
  styleUrl: './settings-data-page.component.scss'
})
export class SettingsDataPageComponent {
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly previewRows = signal<CsvInventoryRow[]>([]);
  protected readonly pendingKey = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly visibleRows = computed(() => this.previewRows().slice(0, 25));
  protected readonly remainingRows = computed(() => Math.max(0, this.previewRows().length - this.visibleRows().length));

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected chooseFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.selectedFile.set(file);
    this.previewRows.set([]);
    this.pendingKey.set(null);
    this.notice.set(null);
    this.error.set(null);
  }

  protected preview(): void {
    const file = this.selectedFile();
    if (!file || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.api.previewCsvImport(file).pipe(
      tap((response) => {
        this.previewRows.set(response.rows);
        this.pendingKey.set(response.key);
        this.notice.set(`${response.count} filas listas para importar.`);
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo previsualizar el CSV.'));
        return EMPTY;
      }),
      finalize(() => this.busy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected confirm(): void {
    const key = this.pendingKey();
    if (!key || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.api.confirmCsvImport(key).pipe(
      tap((response) => {
        this.notice.set(`Importadas ${response.imported} filas.`);
        this.previewRows.set([]);
        this.pendingKey.set(null);
        this.selectedFile.set(null);
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo confirmar la importación.'));
        return EMPTY;
      }),
      finalize(() => this.busy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private errorMessage(error: unknown, fallback: string): string {
    const candidate = error as { error?: { error?: string }; message?: string };
    return candidate.error?.error ?? candidate.message ?? fallback;
  }
}
