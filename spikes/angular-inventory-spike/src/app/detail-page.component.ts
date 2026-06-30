import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { catchError, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { EntityMiniCardComponent } from './entity-mini-card.component';
import { ColorPickerComponent } from './color-picker.component';
import { HierarchyTrailComponent, HierarchyTrailNode } from './hierarchy-trail.component';
import { InventoryCodePipe } from './inventory-code.pipe';
import { InventoryAction, InventoryApiService, InventoryBoxDetail, InventoryBoxUpdate, InventoryHierarchyNode, InventoryItem, InventoryItemDetail, InventoryItemUpdate, InventoryOptionsResponse, InventoryPhoto } from './inventory-api.service';
import { legacyUrl } from './legacy-url';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';
import { TagPickerComponent } from './tag-picker.component';

type DetailKind = 'item' | 'box';
type BoxItemsView = 'list' | 'gallery' | 'table';
type BoxItemSortKey = 'code' | 'name' | 'tags' | 'quantity';

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
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    ColorPickerComponent,
    InventoryCodePipe,
    TagPickerComponent,
    SearchableSelectComponent,
    EntityMiniCardComponent,
    HierarchyTrailComponent
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
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], locations: [], boxes: [] });
  protected readonly itemForm = signal<InventoryItemUpdate>(this.emptyItemForm());
  protected readonly boxForm = signal<InventoryBoxUpdate>(this.emptyBoxForm());
  protected readonly newTagName = signal('');
  protected readonly newTagColor = signal('#48ffb0');
  protected readonly activePhotoIndex = signal(0);
  protected readonly modalPhoto = signal<InventoryPhoto | null>(null);
  protected readonly boxItemsView = signal<BoxItemsView>('list');
  protected readonly boxItemSortKey = signal<BoxItemSortKey>('name');
  protected readonly boxItemSortDirection = signal<'asc' | 'desc'>('asc');
  protected readonly uploadCaption = signal('');
  protected readonly newActionTitle = signal('');
  protected readonly newActionDescription = signal('');
  protected readonly newActionPriority = signal(3);
  protected readonly newCommentText = signal('');
  protected readonly editingActionId = signal<number | null>(null);
  protected readonly editingActionTitle = signal('');
  protected readonly editingActionDescription = signal('');
  protected readonly editingActionPriority = signal(3);
  protected readonly editingCommentId = signal<number | null>(null);
  protected readonly editingCommentText = signal('');
  protected readonly zooming = signal(false);
  protected readonly zoomOriginX = signal('50%');
  protected readonly zoomOriginY = signal('50%');
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
  protected readonly containerTypeOptions: SearchableSelectOption[] = this.containerTypes.map((type) => ({ value: type.value, label: type.label }));
  protected readonly boxStatusOptions: SearchableSelectOption[] = this.boxStatuses.map((status) => ({ value: status, label: status }));
  protected readonly title = computed(() => this.item()?.name ?? this.box()?.name ?? 'Detalle');
  protected readonly currentPhotos = computed(() => this.item()?.photos ?? this.box()?.photos ?? []);
  protected readonly activePhoto = computed(() => this.currentPhotos()[this.activePhotoIndex()] ?? null);
  protected readonly linkedActions = computed(() => this.item()?.actions ?? this.box()?.actions ?? []);
  protected readonly linkedComments = computed(() => this.item()?.comments ?? this.box()?.comments ?? []);
  protected readonly tagOptions = computed<SearchableSelectOption[]>(() =>
    this.options().tags.map((tag) => ({ value: tag.id, label: tag.name, hint: tag.color })));
  protected readonly conditionOptions = computed<SearchableSelectOption[]>(() =>
    this.options().conditions.map((condition) => ({ value: condition.name, label: condition.name, hint: condition.color })));
  protected readonly locationOptions = computed<SearchableSelectOption[]>(() =>
    this.options().locations.map((location) => ({ value: location.id, label: location.name })));
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: `${box.code} · ${box.name}`,
      hint: [box.containerTypeLabel, box.locationName, box.path].filter(Boolean).join(' · '),
      imageUrl: box.coverUrl,
      rotationDegrees: box.rotationDegrees,
      placeholder: box.code
    })));
  protected readonly parentBoxOptions = computed<SearchableSelectOption[]>(() => {
    const currentBoxId = this.box()?.id;
    return this.boxOptions().filter((option) => option.value !== currentBoxId);
  });
  protected readonly sortedBoxItems = computed(() => {
    const items = [...(this.box()?.items ?? [])];
    const key = this.boxItemSortKey();
    const direction = this.boxItemSortDirection() === 'asc' ? 1 : -1;
    return items.sort((left, right) => {
      const leftValue = this.boxItemSortValue(left, key);
      const rightValue = this.boxItemSortValue(right, key);
      return leftValue.localeCompare(rightValue, undefined, { numeric: true, sensitivity: 'base' }) * direction;
    });
  });
  protected readonly itemHierarchyNodes = computed<HierarchyTrailNode[]>(() => {
    const item = this.item();
    if (!item) {
      return [];
    }

    if (item.box) {
      return item.box.hierarchy.map((node) => this.toHierarchyNode(node));
    }

    const nodes: HierarchyTrailNode[] = [
      {
        label: 'Sin contenedor',
        sublabel: 'Pendiente de clasificar',
        icon: 'location_off',
        tone: 'muted'
      },
      {
        label: item.name,
        sublabel: this.tagSummary(item),
        icon: 'category',
        tone: 'item'
      }
    ];
    return nodes;
  });
  protected readonly boxHierarchyNodes = computed<HierarchyTrailNode[]>(() => {
    const box = this.box();
    if (!box) {
      return [];
    }

    return box.hierarchy.map((node) => this.toHierarchyNode(node));
  });
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
  private readonly titleService = inject(Title);
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
        this.activePhotoIndex.set(0);
        this.modalPhoto.set(null);
        this.zooming.set(false);
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
            this.loading.set(false);
            return EMPTY;
          }

          return this.api.fetchItem(id).pipe(
            tap((item) => {
              this.item.set(item);
              this.titleService.setTitle(`${item.code} · ${item.name} · Junkyard`);
            }),
            catchError((error: unknown) => {
              this.error.set(error instanceof Error ? error.message : 'No se pudo cargar el detalle.');
              return EMPTY;
            }),
            finalize(() => this.loading.set(false))
          );
        }

        if (!value.trim()) {
          this.error.set('Contenedor no válido.');
          this.loading.set(false);
          return EMPTY;
        }

          return this.api.fetchBox(value).pipe(
            tap((box) => {
              this.box.set(box);
              this.titleService.setTitle(`${box.code} · ${box.name} · Junkyard`);
            }),
          catchError((error: unknown) => {
            this.error.set(error instanceof Error ? error.message : 'No se pudo cargar el detalle.');
            return EMPTY;
          }),
          finalize(() => this.loading.set(false))
        );
      }),
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
      code: item.code,
      category: item.category,
      tagIds: item.tags.map((tag) => tag.id),
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

    if (!form.tagIds.length) {
      this.formError.set('Selecciona o crea al menos un tag.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.saveMessage.set(null);
    this.api.updateItem(item.id, {
      ...form,
      code: form.code.trim(),
      name: form.name.trim(),
      category: this.primaryTagName(form.tagIds),
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
          code: updated.code,
          category: updated.category,
          tagIds: updated.tags.map((tag) => tag.id),
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

  protected createLinkedAction(): void {
    const title = this.newActionTitle().trim();
    if (!title || this.saving()) {
      this.formError.set('El título de la tarea es obligatorio.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    const input = {
      title,
      description: this.newActionDescription().trim(),
      priority: this.newActionPriority()
    };
    const item = this.item();
    const request = item
      ? this.api.createItemAction(item.id, input)
      : this.box()
        ? this.api.createBoxAction(this.box()!.id, input)
        : null;
    if (!request) {
      this.saving.set(false);
      return;
    }

    request.pipe(
      tap((action) => {
        this.appendLinkedAction(action);
        this.newActionTitle.set('');
        this.newActionDescription.set('');
        this.newActionPriority.set(3);
        this.saveMessage.set('Tarea añadida.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo crear la tarea.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected addLinkedComment(): void {
    const text = this.newCommentText().trim();
    if (!text || this.saving()) {
      this.formError.set('Escribe un comentario antes de guardar.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    const item = this.item();
    const request = item
      ? this.api.createItemComment(item.id, { text })
      : this.box()
        ? this.api.createBoxComment(this.box()!.id, { text })
        : null;
    if (!request) {
      this.saving.set(false);
      return;
    }

    request.pipe(
      tap((comment) => {
        this.appendLinkedComment(comment);
        this.newCommentText.set('');
        this.saveMessage.set('Comentario guardado.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo guardar el comentario.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected completeLinkedAction(action: InventoryAction): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.completeAction(action.id).pipe(
      tap((updated) => {
        this.replaceOrRemoveLinkedAction(updated);
        this.saveMessage.set('Tarea completada.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo completar la tarea.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected startActionEdit(action: InventoryAction): void {
    this.editingActionId.set(action.id);
    this.editingActionTitle.set(action.title);
    this.editingActionDescription.set(action.description ?? '');
    this.editingActionPriority.set(action.priority);
    this.formError.set(null);
  }

  protected cancelActionEdit(): void {
    this.editingActionId.set(null);
    this.editingActionTitle.set('');
    this.editingActionDescription.set('');
    this.editingActionPriority.set(3);
  }

  protected saveActionEdit(action: InventoryAction): void {
    const title = this.editingActionTitle().trim();
    if (!title || this.saving()) {
      this.formError.set('El título de la tarea es obligatorio.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.updateAction(action.id, {
      title,
      description: this.editingActionDescription().trim(),
      priority: this.editingActionPriority()
    }).pipe(
      tap((updated) => {
        this.replaceLinkedAction(updated);
        this.cancelActionEdit();
        this.saveMessage.set('Tarea actualizada.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo actualizar la tarea.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected startCommentEdit(comment: InventoryAction): void {
    this.editingCommentId.set(comment.id);
    this.editingCommentText.set(comment.description ?? '');
    this.formError.set(null);
  }

  protected cancelCommentEdit(): void {
    this.editingCommentId.set(null);
    this.editingCommentText.set('');
  }

  protected saveCommentEdit(comment: InventoryAction): void {
    const text = this.editingCommentText().trim();
    if (!text || this.saving()) {
      this.formError.set('El comentario no puede quedar vacío.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.updateComment(comment.id, { text }).pipe(
      tap((updated) => {
        this.replaceLinkedComment(updated);
        this.cancelCommentEdit();
        this.saveMessage.set('Comentario actualizado.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo actualizar el comentario.');
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

  protected itemPhotosDownloadUrl(): string {
    const item = this.item();
    return item ? this.api.itemPhotosDownloadUrl(item.id) : '#';
  }

  protected photoTrack(_index: number, photo: InventoryPhoto): number {
    return photo.id;
  }

  protected selectPhoto(index: number): void {
    this.activePhotoIndex.set(index);
    this.zooming.set(false);
  }

  protected isItemCover(photo: InventoryPhoto): boolean {
    const item = this.item();
    return Boolean(item?.photos[0]?.id === photo.id);
  }

  protected isCoverPhoto(photo: InventoryPhoto): boolean {
    return this.currentPhotos()[0]?.id === photo.id;
  }

  protected setCoverPhoto(photo: InventoryPhoto): void {
    const item = this.item();
    if (item) {
      this.setItemCover(photo);
      return;
    }

    const box = this.box();
    if (!box || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.setBoxCoverPhoto(box.id, photo.id).pipe(
      tap((updated) => {
        this.box.set(updated);
        this.activePhotoIndex.set(0);
        this.saveMessage.set('Foto principal actualizada.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo fijar la foto principal.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected rotatePhoto(photo: InventoryPhoto, delta: number): void {
    const item = this.item();
    if (item) {
      this.rotateItemPhoto(photo, delta);
      return;
    }

    const box = this.box();
    if (!box || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.rotateBoxPhoto(box.id, photo.id, delta).pipe(
      tap((updated) => {
        this.box.set(updated);
        const nextIndex = Math.max(0, updated.photos.findIndex((candidate) => candidate.id === photo.id));
        this.activePhotoIndex.set(nextIndex);
        this.saveMessage.set('Foto girada.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo girar la foto.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected archivePhoto(photo: InventoryPhoto): void {
    if (this.saving() || !window.confirm('¿Archivar esta foto de la ficha?')) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    const item = this.item();
    if (item) {
      this.api.archiveItemPhoto(item.id, photo.id).pipe(
        tap((updated) => {
          this.item.set(updated);
          this.activePhotoIndex.set(0);
          this.saveMessage.set('Foto archivada.');
        }),
        catchError((error: unknown) => {
          this.formError.set(error instanceof Error ? error.message : 'No se pudo archivar la foto.');
          return EMPTY;
        }),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe();
      return;
    }

    const box = this.box();
    if (box) {
      this.api.archiveBoxPhoto(box.id, photo.id).pipe(
        tap((updated) => {
          this.box.set(updated);
          this.activePhotoIndex.set(0);
          this.saveMessage.set('Foto archivada.');
        }),
        catchError((error: unknown) => {
          this.formError.set(error instanceof Error ? error.message : 'No se pudo archivar la foto.');
          return EMPTY;
        }),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe();
      return;
    }

    this.saving.set(false);
  }

  protected returnPhotoToInbox(photo: InventoryPhoto): void {
    if (this.saving() || !window.confirm('¿Devolver esta foto a la bandeja de revisión?')) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    const item = this.item();
    if (item) {
      this.api.returnItemPhotoToInbox(item.id, photo.id).pipe(
        tap((response) => {
          this.item.set(response.detail);
          this.activePhotoIndex.set(0);
          this.saveMessage.set(response.inboxId ? `Foto devuelta a revisión (#${response.inboxId}).` : 'Foto devuelta a revisión.');
        }),
        catchError((error: unknown) => {
          this.formError.set(error instanceof Error ? error.message : 'No se pudo devolver la foto.');
          return EMPTY;
        }),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe();
      return;
    }

    const box = this.box();
    if (box) {
      this.api.returnBoxPhotoToInbox(box.id, photo.id).pipe(
        tap((response) => {
          this.box.set(response.detail);
          this.activePhotoIndex.set(0);
          this.saveMessage.set(response.inboxId ? `Foto devuelta a revisión (#${response.inboxId}).` : 'Foto devuelta a revisión.');
        }),
        catchError((error: unknown) => {
          this.formError.set(error instanceof Error ? error.message : 'No se pudo devolver la foto.');
          return EMPTY;
        }),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe();
      return;
    }

    this.saving.set(false);
  }

  protected setItemCover(photo: InventoryPhoto): void {
    const item = this.item();
    if (!item || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.setItemCoverPhoto(item.id, photo.id).pipe(
      tap((updated) => {
        this.item.set(updated);
        this.activePhotoIndex.set(0);
        this.saveMessage.set('Foto principal actualizada.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo fijar la foto principal.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected rotateItemPhoto(photo: InventoryPhoto, delta: number): void {
    const item = this.item();
    if (!item || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.rotateItemPhoto(item.id, photo.id, delta).pipe(
      tap((updated) => {
        this.item.set(updated);
        const nextIndex = Math.max(0, updated.photos.findIndex((candidate) => candidate.id === photo.id));
        this.activePhotoIndex.set(nextIndex);
        this.saveMessage.set('Foto girada.');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo girar la foto.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected uploadDetailPhotos(event: Event): void {
    const input = event.target as HTMLInputElement | null;
    const files = Array.from(input?.files ?? []);
    if (files.length === 0 || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    const caption = this.uploadCaption();
    const item = this.item();
    if (item) {
      this.api.uploadItemPhotos(item.id, files, caption).pipe(
        tap((updated) => {
          this.item.set(updated);
          this.finishPhotoUpload(input);
        }),
        catchError((error: unknown) => {
          this.formError.set(error instanceof Error ? error.message : 'No se pudieron subir las fotos.');
          return EMPTY;
        }),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe();
      return;
    }

    const box = this.box();
    if (box) {
      this.api.uploadBoxPhotos(box.id, files, caption).pipe(
        tap((updated) => {
          this.box.set(updated);
          this.finishPhotoUpload(input);
        }),
        catchError((error: unknown) => {
          this.formError.set(error instanceof Error ? error.message : 'No se pudieron subir las fotos.');
          return EMPTY;
        }),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe();
      return;
    }

    this.saving.set(false);
  }

  private finishPhotoUpload(input: HTMLInputElement | null): void {
    this.activePhotoIndex.set(0);
    this.uploadCaption.set('');
    if (input) {
      input.value = '';
    }
    this.saveMessage.set('Fotos subidas.');
  }

  private appendLinkedAction(action: InventoryAction): void {
    this.item.update((item) => item ? { ...item, actions: [action, ...item.actions] } : item);
    this.box.update((box) => box ? { ...box, actions: [action, ...box.actions] } : box);
  }

  private appendLinkedComment(comment: InventoryAction): void {
    this.item.update((item) => item ? { ...item, comments: [comment, ...item.comments] } : item);
    this.box.update((box) => box ? { ...box, comments: [comment, ...box.comments] } : box);
  }

  private replaceOrRemoveLinkedAction(action: InventoryAction): void {
    const replace = (actions: InventoryAction[]) =>
      action.status === 'Open'
        ? actions.map((candidate) => candidate.id === action.id ? action : candidate)
        : actions.filter((candidate) => candidate.id !== action.id);
    this.item.update((item) => item ? { ...item, actions: replace(item.actions) } : item);
    this.box.update((box) => box ? { ...box, actions: replace(box.actions) } : box);
  }

  private replaceLinkedAction(action: InventoryAction): void {
    const replace = (actions: InventoryAction[]) => actions.map((candidate) => candidate.id === action.id ? action : candidate);
    this.item.update((item) => item ? { ...item, actions: replace(item.actions) } : item);
    this.box.update((box) => box ? { ...box, actions: replace(box.actions) } : box);
  }

  private replaceLinkedComment(comment: InventoryAction): void {
    const replace = (comments: InventoryAction[]) => comments.map((candidate) => candidate.id === comment.id ? comment : candidate);
    this.item.update((item) => item ? { ...item, comments: replace(item.comments) } : item);
    this.box.update((box) => box ? { ...box, comments: replace(box.comments) } : box);
  }

  protected activePhotoTransform(photo: InventoryPhoto): string {
    const scale = this.zooming() ? 1.85 : 1;
    return `rotate(${photo.rotationDegrees}deg) scale(${scale})`;
  }

  protected updateZoomOrigin(event: MouseEvent): void {
    const target = event.currentTarget as HTMLElement | null;
    if (!target) {
      return;
    }

    const rect = target.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * 100;
    const y = ((event.clientY - rect.top) / rect.height) * 100;
    this.zoomOriginX.set(`${Math.max(0, Math.min(100, x))}%`);
    this.zoomOriginY.set(`${Math.max(0, Math.min(100, y))}%`);
    this.zooming.set(true);
  }

  protected stopZoom(): void {
    this.zooming.set(false);
    this.zoomOriginX.set('50%');
    this.zoomOriginY.set('50%');
  }

  protected openPhotoModal(photo: InventoryPhoto | null): void {
    if (!photo) {
      return;
    }

    this.modalPhoto.set(photo);
    this.zooming.set(false);
  }

  protected closePhotoModal(): void {
    this.modalPhoto.set(null);
  }

  protected quickCreateTag(): void {
    const name = this.newTagName().trim();
    if (!name || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.formError.set(null);
    this.api.createTag({ name, color: this.newTagColor() }).pipe(
      tap((tag) => {
        this.options.update((current) => ({
          ...current,
          tags: [...current.tags.filter((existing) => existing.id !== tag.id), tag].sort((left, right) => left.name.localeCompare(right.name))
        }));
        this.itemForm.update((current) => ({
          ...current,
          tagIds: Array.from(new Set([...current.tagIds, tag.id]))
        }));
        this.newTagName.set('');
        this.newTagColor.set('#48ffb0');
      }),
      catchError((error: unknown) => {
        this.formError.set(error instanceof Error ? error.message : 'No se pudo crear el tag.');
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected setNewTagName(value: string): void {
    this.newTagName.set(value);
  }

  protected setNewTagColor(value: string): void {
    this.newTagColor.set(value);
  }

  protected tagSummary(item: InventoryItemDetail | InventoryItem): string {
    return item.tags.length ? item.tags.map((tag) => tag.name).join(', ') : item.category;
  }

  protected setBoxItemsView(view: BoxItemsView): void {
    this.boxItemsView.set(view);
  }

  protected sortBoxItems(key: BoxItemSortKey): void {
    if (this.boxItemSortKey() === key) {
      this.boxItemSortDirection.update((direction) => direction === 'asc' ? 'desc' : 'asc');
      return;
    }

    this.boxItemSortKey.set(key);
    this.boxItemSortDirection.set('asc');
  }

  protected sortLabel(key: BoxItemSortKey): string {
    if (this.boxItemSortKey() !== key) {
      return '';
    }

    return this.boxItemSortDirection() === 'asc' ? 'arrow_upward' : 'arrow_downward';
  }

  protected primaryTagName(tagIds: number[]): string {
    const first = this.options().tags.find((tag) => tagIds.includes(tag.id));
    return first?.name ?? 'Otros';
  }

  private emptyItemForm(): InventoryItemUpdate {
    return {
      code: '',
      name: '',
      category: 'Otros',
      tagIds: [],
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

  private boxItemSortValue(item: InventoryItem, key: BoxItemSortKey): string {
    switch (key) {
      case 'code':
        return item.code;
      case 'tags':
        return this.tagSummary(item);
      case 'quantity':
        return item.quantityLabel;
      case 'name':
      default:
        return item.name;
    }
  }

  private toHierarchyNode(node: InventoryHierarchyNode): HierarchyTrailNode {
    return {
      label: node.label,
      sublabel: node.sublabel,
      icon: node.icon,
      routerLink: node.routerLink,
      tone: node.tone,
      coverUrl: node.coverUrl,
      rotationDegrees: node.rotationDegrees
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
