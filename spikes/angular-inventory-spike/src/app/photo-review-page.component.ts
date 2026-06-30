import { CommonModule } from '@angular/common';
import { Component, DestroyRef, ElementRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { InventoryApiService, InventoryItem, InventoryOptionsResponse, PhotoReviewPhoto, PhotoReviewResponse } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';
import { TagPickerComponent } from './tag-picker.component';

type ReviewPanel = 'none' | 'create' | 'assignItem' | 'assignBox';

@Component({
  selector: 'app-photo-review-page',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, InventoryCodePipe, SearchableSelectComponent, TagPickerComponent],
  templateUrl: './photo-review-page.component.html',
  styleUrl: './photo-review-page.component.scss'
})
export class PhotoReviewPageComponent {
  protected readonly review = signal<PhotoReviewResponse | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], locations: [], boxes: [] });
  protected readonly items = signal<InventoryItem[]>([]);
  protected readonly selectedIds = signal<number[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly panel = signal<ReviewPanel>('none');
  protected readonly lastAffectedIds = signal<number[]>([]);
  protected readonly assignBoxId = signal<number | null>(null);
  protected readonly assignItemId = signal<number | null>(null);
  protected readonly draftName = signal('');
  protected readonly draftNotes = signal('');
  protected readonly draftQuantity = signal(1);
  protected readonly draftUnit = signal('uds');
  protected readonly draftTagIds = signal<number[]>([]);
  protected readonly draftBoxId = signal<number | null>(null);
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: `${box.code} · ${box.name}`,
      hint: [box.containerTypeLabel, box.locationName, box.path].filter(Boolean).join(' · '),
      imageUrl: box.coverUrl,
      rotationDegrees: box.rotationDegrees,
      placeholder: box.code
    })));
  protected readonly tagOptions = computed<SearchableSelectOption[]>(() =>
    this.options().tags.map((tag) => ({ value: tag.id, label: tag.name, hint: tag.color })));
  protected readonly itemOptions = computed<SearchableSelectOption[]>(() =>
    this.items().map((item) => ({
      value: item.id,
      label: `${item.code} · ${item.name}`,
      hint: [item.category, item.boxCode].filter(Boolean).join(' · '),
      imageUrl: item.coverUrl,
      rotationDegrees: item.rotationDegrees,
      placeholder: item.code
    })));
  protected readonly actionSelectionSize = computed(() => this.selectedIds().length || (this.current() ? 1 : 0));

  private readonly api = inject(InventoryApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  @ViewChild('createNameInput') private readonly createNameInput?: ElementRef<HTMLInputElement>;
  @ViewChild('assignItemSelect') private readonly assignItemSelect?: SearchableSelectComponent;
  @ViewChild('assignBoxSelect') private readonly assignBoxSelect?: SearchableSelectComponent;

  constructor() {
    const id = Number.parseInt(this.route.snapshot.queryParamMap.get('id') ?? '', 10);
    this.load(Number.isInteger(id) ? id : null);
    this.api.fetchOptions().pipe(
      tap((options) => this.options.set(options)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
    this.api.fetchInventory({
      q: '',
      category: '',
      tagIds: [],
      box: '',
      boxIds: [],
      locationId: null,
      includeChildren: false,
      onlyConsumable: false,
      onlyOrphans: false,
      layout: 'flat',
      view: 'flat'
    }).pipe(
      tap((response) => this.items.set(response.items)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected current(): PhotoReviewPhoto | null {
    return this.review()?.current ?? null;
  }

  protected assetUrl(path: string | null | undefined): string | null {
    return path ? (path.startsWith('/') ? path : `/${path}`) : null;
  }

  protected isSelected(photo: PhotoReviewPhoto): boolean {
    return this.selectedIds().includes(photo.id);
  }

  protected toggleSelected(photo: PhotoReviewPhoto): void {
    this.selectedIds.update((current) => current.includes(photo.id) ? current.filter((id) => id !== photo.id) : [...current, photo.id]);
  }

  protected onFilmClick(photo: PhotoReviewPhoto, event: MouseEvent): void {
    if (event.ctrlKey || event.metaKey || event.shiftKey) {
      this.toggleSelected(photo);
      return;
    }

    this.selectOnly(photo);
  }

  protected selectOnly(photo: PhotoReviewPhoto): void {
    this.selectedIds.set([photo.id]);
    this.navigateTo(photo.id);
  }

  protected openPanel(panel: ReviewPanel): void {
    const current = this.current();
    if (!current) {
      return;
    }

    if (panel === 'create') {
      this.draftName.set('');
      this.draftNotes.set('');
      this.draftQuantity.set(1);
      this.draftUnit.set('uds');
      this.draftBoxId.set(current.sourceBox?.id ?? null);
      this.draftTagIds.set(this.options().tags.find((tag) => tag.name === 'Otros') ? [this.options().tags.find((tag) => tag.name === 'Otros')!.id] : []);
    }

    if (panel === 'assignBox') {
      this.assignBoxId.set(current.sourceBox?.id ?? null);
    }

    this.panel.set(panel);
    this.focusPanel(panel);
  }

  protected closePanel(): void {
    this.panel.set('none');
  }

  protected rotate(delta: number): void {
    const current = this.current();
    if (!current) {
      return;
    }

    this.mutate(() => this.api.rotateReviewPhotos(current.id, this.selection(current.id), delta), 'Foto girada.');
  }

  protected discard(): void {
    const current = this.current();
    if (!current) {
      return;
    }

    this.mutate(() => this.api.discardReviewPhotos(current.id, this.selection(current.id)), 'Foto descartada.');
  }

  protected assignBox(): void {
    const current = this.current();
    const boxId = this.assignBoxId();
    if (!current || !boxId) {
      this.error.set('Selecciona un contenedor.');
      return;
    }

    this.mutate(() => this.api.assignReviewPhotosToBox(current.id, this.selection(current.id), boxId), 'Foto asignada al contenedor.');
  }

  protected assignItem(): void {
    const current = this.current();
    const itemId = this.assignItemId();
    if (!current || !itemId) {
      this.error.set('Selecciona un ítem.');
      return;
    }

    this.mutate(() => this.api.assignReviewPhotosToItem(current.id, this.selection(current.id), itemId), 'Foto asignada al ítem.');
  }

  protected createItem(): void {
    const current = this.current();
    if (!current || !this.draftName().trim()) {
      this.error.set('El nombre es obligatorio.');
      return;
    }

    this.mutate(() => this.api.createItemFromReviewPhotos(current.id, {
      ids: this.selection(current.id),
      boxId: this.draftBoxId(),
      name: this.draftName().trim(),
      notes: this.draftNotes().trim(),
      quantity: this.draftQuantity(),
      unit: this.draftUnit().trim(),
      tagIds: this.draftTagIds()
    }), 'Ítem creado desde foto.');
  }

  protected undo(): void {
    const ids = this.lastAffectedIds();
    if (ids.length === 0 || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.api.undoReviewPhotos(ids).pipe(
      tap((review) => {
        this.applyReview(review);
        this.lastAffectedIds.set([]);
        this.message.set('Acción deshecha.');
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo deshacer.');
        return EMPTY;
      }),
      finalize(() => this.busy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected skip(delta: number): void {
    const review = this.review();
    const nextId = delta < 0 ? review?.previousId : review?.nextId;
    if (nextId) {
      this.navigateTo(nextId);
    }
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, textarea, select, button, .mat-mdc-select-panel')) {
      return;
    }

    const key = event.key.toLowerCase();
    if (event.ctrlKey && key === 'z') {
      event.preventDefault();
      this.undo();
    } else if (key === 'q') {
      event.preventDefault();
      this.rotate(-90);
    } else if (key === 'e') {
      event.preventDefault();
      this.rotate(90);
    } else if (key === 'd') {
      event.preventDefault();
      this.discard();
    } else if (key === 's' || event.key === 'ArrowRight') {
      event.preventDefault();
      this.skip(1);
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.skip(-1);
    } else if (key === 'n') {
      event.preventDefault();
      this.openPanel('create');
    } else if (key === 'a') {
      event.preventDefault();
      this.openPanel('assignItem');
    } else if (key === 'b') {
      event.preventDefault();
      this.openPanel('assignBox');
    } else if (event.key === ' ') {
      event.preventDefault();
      const current = this.current();
      if (current) {
        this.toggleSelected(current);
      }
    } else if (event.key === 'Escape') {
      this.closePanel();
    }
  }

  private load(id: number | null): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.fetchPhotoReview(id).pipe(
      tap((review) => this.applyReview(review)),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo cargar la revisión.');
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private navigateTo(id: number): void {
    this.router.navigate(['/photos/review'], { queryParams: { id } }).then(() => this.load(id)).catch(() => this.load(id));
  }

  private selection(currentId: number): number[] {
    const selected = this.selectedIds();
    return selected.length ? selected : [currentId];
  }

  private mutate(request: () => ReturnType<InventoryApiService['rotateReviewPhotos']>, success: string): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    request().pipe(
      tap((response) => {
        this.applyReview(response.review);
        this.lastAffectedIds.set(response.affectedIds);
        this.message.set(success);
        this.panel.set('none');
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'No se pudo completar la acción.');
        return EMPTY;
      }),
      finalize(() => this.busy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private applyReview(review: PhotoReviewResponse): void {
    this.review.set(review);
    const pendingIds = new Set(review.pending.map((photo) => photo.id));
    this.selectedIds.update((current) => current.filter((id) => pendingIds.has(id)));
  }

  private focusPanel(panel: ReviewPanel): void {
    setTimeout(() => {
      if (panel === 'create') {
        this.createNameInput?.nativeElement.focus();
        this.createNameInput?.nativeElement.select();
      } else if (panel === 'assignItem') {
        this.assignItemSelect?.focus();
        this.assignItemSelect?.open();
      } else if (panel === 'assignBox') {
        this.assignBoxSelect?.focus();
        this.assignBoxSelect?.open();
      }
    });
  }
}
