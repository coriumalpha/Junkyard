import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { InventoryApiService, InventoryBoxDetail, InventoryBoxUpdate, InventoryItem, InventoryItemDetail, InventoryItemUpdate, InventoryOptionsResponse, InventoryPhoto } from './inventory-api.service';
import { legacyUrl } from './legacy-url';

type DetailKind = 'item' | 'box';

@Component({
  selector: 'app-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatDividerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule
    ,
    MatSelectModule,
    MatSlideToggleModule
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
  protected readonly editingItem = signal(false);
  protected readonly editingBox = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveMessage = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], locations: [], boxes: [] });
  protected readonly itemForm = signal<InventoryItemUpdate>(this.emptyItemForm());
  protected readonly boxForm = signal<InventoryBoxUpdate>(this.emptyBoxForm());
  protected readonly containerTypes = [
    { value: 'box', label: 'Caja' },
    { value: 'subbox', label: 'Subcaja' },
    { value: 'shelf', label: 'Balda' },
    { value: 'drawer', label: 'Cajón' },
    { value: 'rack', label: 'Estantería' },
    { value: 'bag', label: 'Bolsa' },
    { value: 'case', label: 'Maletín' },
    { value: 'binder', label: 'Archivador' },
    { value: 'lot', label: 'Lote temporal' },
    { value: 'zone', label: 'Zona física' },
    { value: 'other', label: 'Otro soporte' }
  ];
  protected readonly boxStatuses = ['Active', 'Quarantine', 'Archived'];
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
        this.editingItem.set(false);
        this.editingBox.set(false);
        this.saveMessage.set(null);
        this.formError.set(null);
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

    this.api.fetchOptions().pipe(
      tap((options) => this.options.set(options)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected startItemEdit(): void {
    const item = this.item();
    if (!item) {
      return;
    }

    this.itemForm.set({
      name: item.name,
      category: item.category,
      quantity: item.quantity,
      unit: item.unit,
      minQuantity: item.minQuantity,
      condition: item.condition ?? '',
      retention: item.retention ?? '',
      consumable: item.consumable,
      sentimental: item.sentimental,
      obsolete: item.obsolete,
      notes: item.notes ?? '',
      boxId: item.box?.id ?? null
    });
    this.formError.set(null);
    this.saveMessage.set(null);
    this.editingItem.set(true);
  }

  protected cancelItemEdit(): void {
    this.editingItem.set(false);
    this.formError.set(null);
  }

  protected startBoxEdit(): void {
    const box = this.box();
    if (!box) {
      return;
    }

    this.boxForm.set({
      code: box.code,
      name: box.name,
      containerType: box.containerType,
      description: box.description ?? '',
      locationId: box.locationId,
      parentBoxId: box.parent?.id ?? null,
      status: box.status
    });
    this.formError.set(null);
    this.saveMessage.set(null);
    this.editingBox.set(true);
  }

  protected cancelBoxEdit(): void {
    this.editingBox.set(false);
    this.formError.set(null);
  }

  protected patchBoxForm(patch: Partial<InventoryBoxUpdate>): void {
    this.boxForm.update((current) => ({ ...current, ...patch }));
  }

  protected patchItemForm(patch: Partial<InventoryItemUpdate>): void {
    this.itemForm.update((current) => ({ ...current, ...patch }));
  }

  protected saveItem(): void {
    const item = this.item();
    if (!item || this.saving()) {
      return;
    }

    const form = this.itemForm();
    if (!form.name.trim()) {
      this.formError.set('El nombre es obligatorio.');
      return;
    }

    if (!form.category.trim()) {
      this.formError.set('La categoría es obligatoria.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.saveMessage.set(null);
    this.api.updateItem(item.id, {
      ...form,
      name: form.name.trim(),
      category: form.category.trim(),
      unit: form.unit.trim(),
      condition: form.condition.trim(),
      retention: form.retention.trim(),
      notes: form.notes.trim(),
      boxId: form.boxId && form.boxId > 0 ? form.boxId : null
    }).pipe(
      tap((updated) => {
        this.item.set(updated);
        this.itemForm.set({
          name: updated.name,
          category: updated.category,
          quantity: updated.quantity,
          unit: updated.unit,
          minQuantity: updated.minQuantity,
          condition: updated.condition ?? '',
          retention: updated.retention ?? '',
          consumable: updated.consumable,
          sentimental: updated.sentimental,
          obsolete: updated.obsolete,
          notes: updated.notes ?? '',
          boxId: updated.box?.id ?? null
        });
        this.editingItem.set(false);
        this.saveMessage.set('Ítem guardado.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo guardar el ítem.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected saveBox(): void {
    const box = this.box();
    if (!box || this.saving()) {
      return;
    }

    const form = this.boxForm();
    if (!form.code.trim()) {
      this.formError.set('El CT es obligatorio.');
      return;
    }

    if (!form.name.trim()) {
      this.formError.set('El nombre es obligatorio.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.saveMessage.set(null);
    this.api.updateBox(box.id, {
      ...form,
      code: form.code.trim(),
      name: form.name.trim(),
      description: form.description.trim(),
      parentBoxId: form.parentBoxId && form.parentBoxId > 0 ? form.parentBoxId : null
    }).pipe(
      tap((updated) => {
        this.box.set(updated);
        this.boxForm.set({
          code: updated.code,
          name: updated.name,
          containerType: updated.containerType,
          description: updated.description ?? '',
          locationId: updated.locationId,
          parentBoxId: updated.parent?.id ?? null,
          status: updated.status
        });
        this.editingBox.set(false);
        this.saveMessage.set('Contenedor guardado.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo guardar el contenedor.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
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

  private emptyItemForm(): InventoryItemUpdate {
    return {
      name: '',
      category: 'Otros',
      quantity: 1,
      unit: '',
      minQuantity: null,
      condition: '',
      retention: '',
      consumable: false,
      sentimental: false,
      obsolete: false,
      notes: '',
      boxId: null
    };
  }

  private emptyBoxForm(): InventoryBoxUpdate {
    return {
      code: '',
      name: '',
      containerType: 'box',
      description: '',
      locationId: 0,
      parentBoxId: null,
      status: 'Active'
    };
  }
}
