import { DescriptionEditorComponent } from './description-editor.component';
import { RelatedItemsPickerComponent } from './related-items-picker.component';
import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, forkJoin, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { formatInventoryCode } from './inventory-code.pipe';
import { InventoryApiService, InventoryItemUpdate, InventoryMode, InventoryOptionsResponse, ItemClass, ItemSubtype } from './inventory-api.service';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';
import { TagPickerComponent } from './tag-picker.component';

@Component({
  selector: 'app-item-create-page',
  standalone: true,
  imports: [
    DescriptionEditorComponent, RelatedItemsPickerComponent,
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    SearchableSelectComponent,
    TagPickerComponent
  ],
  templateUrl: './item-create-page.component.html',
  styleUrl: './item-create-page.component.scss'
})
export class ItemCreatePageComponent {
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], itemClasses: [], itemSubtypes: [], locations: [], boxes: [] });
  protected readonly itemClasses = signal<ItemClass[]>([]);
  protected readonly itemSubtypes = signal<ItemSubtype[]>([]);
  protected readonly form = signal<InventoryItemUpdate>(this.emptyForm());

  protected readonly tagOptions = computed<SearchableSelectOption[]>(() =>
    this.options().tags.map((tag) => ({ value: tag.id, label: tag.name, hint: tag.color })));
  protected readonly conditionOptions = computed<SearchableSelectOption[]>(() =>
    this.options().conditions.map((condition) => ({ value: condition.name, label: condition.name, hint: condition.color })));
  protected readonly itemClassOptions = computed<SearchableSelectOption[]>(() =>
    this.itemClasses().map((itemClass) => ({
      value: itemClass.id,
      label: itemClass.name,
      hint: this.inventoryModeLabel(itemClass.inventoryMode),
      icon: itemClass.icon ?? 'category'
    })));
  protected readonly itemSubtypeOptions = computed<SearchableSelectOption[]>(() =>
    this.itemSubtypes().map((subtype) => ({
      value: subtype.id,
      label: subtype.name,
      hint: [subtype.unit, subtype.description].filter(Boolean).join(' · ') || null,
      icon: 'label'
    })));
  protected readonly selectedItemClass = computed(() =>
    this.itemClasses().find((itemClass) => itemClass.id === this.form().itemClassId) ?? null);
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: `${formatInventoryCode(box.code)} · ${box.name}`,
      hint: [box.containerTypeLabel, box.locationName, box.path].filter(Boolean).join(' · '),
      imageUrl: box.coverUrl,
      rotationDegrees: box.rotationDegrees,
      placeholder: formatInventoryCode(box.code)
    })));

  private readonly api = inject(InventoryApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    const queryBoxId = Number(this.route.snapshot.queryParamMap.get('boxId'));
    if (Number.isFinite(queryBoxId) && queryBoxId > 0) {
      this.patchForm({ boxId: queryBoxId });
    }

    forkJoin({
      options: this.api.fetchOptions(),
      classes: this.api.fetchItemClasses()
    }).pipe(
      tap(({ options, classes }) => {
        this.options.set(options);
        this.itemClasses.set(classes.itemClasses);
        this.loading.set(false);
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo cargar el formulario de alta.'));
        this.loading.set(false);
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected patchForm(patch: Partial<InventoryItemUpdate>): void {
    this.form.update((current) => ({ ...current, ...patch }));
  }

  protected setItemClassId(value: unknown): void {
    const itemClassId = typeof value === 'number' ? value : null;
    this.patchForm({ itemClassId, itemSubtypeId: null });
    this.loadItemSubtypes(itemClassId);
  }

  protected setItemSubtypeId(value: unknown): void {
    this.patchForm({ itemSubtypeId: typeof value === 'number' ? value : null });
  }

  protected createItem(): void {
    const input = {
      ...this.form(),
      code: this.form().code.trim(),
      name: this.form().name.trim(),
      category: this.primaryTagName(this.form().tagIds),
      unit: this.form().unit.trim(),
      retention: this.form().retention.trim(),
      notes: this.form().notes.trim()
    };
    if (!input.name) {
      this.error.set('El nombre es obligatorio.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api.createItem(input).pipe(
      tap((item) => {
        this.router.navigate(['/item', item.id]);
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo crear el ítem.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected inventoryModeLabel(mode: InventoryMode | null): string {
    switch (mode) {
      case 'Individual':
        return 'Individual';
      case 'Fungible':
        return 'Fungible';
      case 'Kit':
        return 'Kit';
      case 'Lot':
        return 'Lote';
      default:
        return '';
    }
  }

  private loadItemSubtypes(itemClassId: number | null): void {
    if (!itemClassId) {
      this.itemSubtypes.set([]);
      return;
    }

    this.api.fetchItemSubtypes(itemClassId).pipe(
      tap((response) => this.itemSubtypes.set(response.itemSubtypes)),
      catchError(() => {
        this.itemSubtypes.set([]);
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private primaryTagName(tagIds: number[]): string {
    const first = this.options().tags.find((tag) => tagIds.includes(tag.id));
    return first?.name ?? '';
  }

  private errorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error;
      if (body && typeof body === 'object' && 'error' in body && typeof body.error === 'string') {
        return body.error;
      }
      if (typeof body === 'string' && body.trim() && !body.trimStart().startsWith('<!DOCTYPE html>')) {
        return body;
      }
    }
    return fallback;
  }

  private emptyForm(): InventoryItemUpdate {
    return {
      code: '',
      name: '',
      category: '',
      itemClassId: null,
      itemSubtypeId: null,
      tagIds: [],
      quantity: 1,
      unit: '',
      minQuantity: null,
      condition: '',
      retention: '',
      consumable: false,
      isQuarantined: false,
      needsReview: false,
      sentimental: false,
      obsolete: false,
      notes: '',
      descriptionMarkdown: true,
      relatedItemIds: [],
      boxId: null
    };
  }
}
