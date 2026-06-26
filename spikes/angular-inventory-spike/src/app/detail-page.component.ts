import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryApiService, InventoryBoxDetail, InventoryItem, InventoryItemDetail, InventoryPhoto } from './inventory-api.service';
import { legacyUrl } from './legacy-url';

type DetailKind = 'item' | 'box';

@Component({
  selector: 'app-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatDividerModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './detail-page.component.html',
  styleUrl: './detail-page.component.scss'
})
export class DetailPageComponent {
  protected readonly kind = signal<DetailKind>('item');
  protected readonly item = signal<InventoryItemDetail | null>(null);
  protected readonly box = signal<InventoryBoxDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly title = computed(() => this.item()?.name ?? this.box()?.name ?? 'Detalle');
  protected readonly subtitle = computed(() => {
    const item = this.item();
    if (item) {
      return item.box ? `${item.box.code} · ${item.box.path}` : 'Ítem sin caja';
    }

    const box = this.box();
    return box ? `${box.code} · ${box.path}` : '';
  });

  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.route.paramMap.pipe(
      map((params) => {
        const id = params.get('id');
        const code = params.get('code');
        return id ? { kind: 'item' as const, value: id } : { kind: 'box' as const, value: code ?? '' };
      }),
      tap(({ kind }) => {
        this.kind.set(kind);
        this.loading.set(true);
        this.error.set(null);
        this.item.set(null);
        this.box.set(null);
      }),
      switchMap(({ kind, value }) => {
        if (kind === 'item') {
          const id = Number.parseInt(value, 10);
          if (!Number.isInteger(id) || id < 1) {
            this.error.set('Ítem no válido.');
            return EMPTY;
          }

          return this.api.fetchItem(id).pipe(tap((item) => this.item.set(item)));
        }

        if (!value.trim()) {
          this.error.set('Contenedor no válido.');
          return EMPTY;
        }

        return this.api.fetchBox(value).pipe(tap((box) => this.box.set(box)));
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo cargar el detalle.');
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected legacyUrl(path: string | null | undefined): string {
    return legacyUrl(path);
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  protected firstLetter(value: string | null | undefined): string {
    return value?.trim().slice(0, 1).toUpperCase() || '?';
  }

  protected itemFlags(item: InventoryItemDetail | InventoryItem): string[] {
    const flags: string[] = [];
    if (item.consumable) {
      flags.push('Consumible');
    }
    if (item.lowStock) {
      flags.push('Bajo stock');
    }
    if (item.sentimental) {
      flags.push('Sentimental');
    }
    if (item.obsolete) {
      flags.push('Obsoleto');
    }
    return flags;
  }

  protected boxLegacyUrl(): string {
    return this.legacyUrl(this.box()?.legacyUrl);
  }

  protected itemLegacyUrl(): string {
    return this.legacyUrl(this.item()?.legacyUrl);
  }

  protected photoTrack(_index: number, photo: InventoryPhoto): number {
    return photo.id;
  }
}
