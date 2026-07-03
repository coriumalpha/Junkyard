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

import { ArchiveBox, ArchiveItem, ArchivePhoto, InventoryApiService } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';
import { legacyUrl } from './legacy-url';

@Component({
  selector: 'app-archive-page',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatCardModule, MatChipsModule, MatIconModule, MatProgressSpinnerModule, InventoryCodePipe],
  templateUrl: './archive-page.component.html',
  styleUrl: './archive-page.component.scss'
})
export class ArchivePageComponent {
  protected readonly loading = signal(true);
  protected readonly savingId = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly boxes = signal<ArchiveBox[]>([]);
  protected readonly items = signal<ArchiveItem[]>([]);
  protected readonly photos = signal<ArchivePhoto[]>([]);
  protected readonly photoCount = computed(() => this.photos().length);
  protected readonly totalCount = computed(() => this.boxes().length + this.items().length + this.photos().length);

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.load();
  }

  protected restoreItem(item: ArchiveItem): void {
    this.savingId.set(`item-${item.id}`);
    this.error.set(null);
    this.api.restoreItem(item.id).pipe(
      tap(() => {
        this.items.update((items) => items.filter((candidate) => candidate.id !== item.id));
        this.message.set(`${item.code} restaurado.`);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo restaurar el ítem.');
        return EMPTY;
      }),
      finalize(() => this.savingId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected restoreBox(box: ArchiveBox): void {
    this.savingId.set(`box-${box.id}`);
    this.error.set(null);
    this.api.restoreBox(box.id).pipe(
      tap(() => {
        this.boxes.update((boxes) => boxes.filter((candidate) => candidate.id !== box.id));
        this.message.set(`${box.code} restaurado.`);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo restaurar el contenedor.');
        return EMPTY;
      }),
      finalize(() => this.savingId.set(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected assetUrl(path: string | null | undefined): string | null {
    return path ? (path.startsWith('/') ? path : `/${path}`) : null;
  }

  protected legacyUrl(path: string): string {
    return legacyUrl(path);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fetchArchive().pipe(
      tap((archive) => {
        this.boxes.set(archive.boxes);
        this.items.set(archive.items);
        this.photos.set(archive.photos);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo cargar el archivo.');
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }
}
