import { DescriptionEditorComponent } from './description-editor.component';
import { RelatedItemsPickerComponent } from './related-items-picker.component';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AiAnalysisMode, AiStatus, AiSuggestItemResponse, AiSuggestedTag, AiTechnicalFact, InventoryApiService, InventoryItem, InventoryMode, InventoryOptionsResponse, PhotoReviewPhoto, PhotoReviewResponse, ItemClass, ItemSubtype } from './inventory-api.service';
import { InventoryCodePipe, formatInventoryCode } from './inventory-code.pipe';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';
import { TagPickerComponent } from './tag-picker.component';

type ReviewPanel = 'none' | 'create' | 'assignItem' | 'assignBox';
type ReviewWorkspaceMode = 'quick' | 'detailed';
type AiDescriptionStyle = 'narrative' | 'technicalSheet';
type AiWorkflowStep = 'analyze' | 'review' | 'final';

@Component({
  selector: 'app-photo-review-page',
  standalone: true,
  imports: [DescriptionEditorComponent, RelatedItemsPickerComponent, CommonModule, FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSlideToggleModule, MatTooltipModule, InventoryCodePipe, SearchableSelectComponent, TagPickerComponent],
  templateUrl: './photo-review-page.component.html',
  styleUrl: './photo-review-page.component.scss'
})
export class PhotoReviewPageComponent {
  protected readonly review = signal<PhotoReviewResponse | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], itemClasses: [], itemSubtypes: [], locations: [], boxes: [] });
  protected readonly items = signal<InventoryItem[]>([]);
  protected readonly selectedIds = signal<number[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly aiBusy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly aiStatus = signal<AiStatus | null>(null);
  protected readonly aiSuggestion = signal<AiSuggestItemResponse | null>(null);
  protected readonly aiSuggestionPhotoIds = signal<number[]>([]);
  protected readonly aiUseName = signal(false);
  protected readonly aiUseDescription = signal(false);
  protected readonly aiUseQuantity = signal(false);
  protected readonly aiUseClass = signal(false);
  protected readonly aiSelectedTagKeys = signal<string[]>([]);
  protected readonly aiIncludedTechnicalFactKeys = signal<string[]>([]);
  protected readonly aiSelectedTechnicalFactKeys = signal<string[]>([]);
  protected readonly aiPreviewName = signal('');
  protected readonly aiPreviewDescription = signal('');
  protected readonly aiPreviewQuantity = signal<number | null>(null);
  protected readonly aiHint = signal('');
  protected readonly aiWebSearch = signal(false);
  protected readonly aiMode = signal<AiAnalysisMode>('fast');
  protected readonly aiDescriptionStyle = signal<AiDescriptionStyle>('narrative');
  protected readonly aiStep = signal<AiWorkflowStep>('analyze');
  protected readonly aiModeManuallySelected = signal(false);
  protected readonly aiModeAutoNotice = signal(false);
  protected readonly workspaceMode = signal<ReviewWorkspaceMode>('quick');
  protected readonly dismissedTechnicalFactKeys = signal<string[]>([]);
  protected readonly editedTechnicalFactValues = signal<Record<string, string>>({});
  protected readonly aiHintLength = computed(() => this.aiHint().length);
  protected readonly aiDetailedRecommended = computed(() => this.looksLikeDetailedHint(this.aiHint()));
  protected readonly aiModeHint = computed(() =>
    this.aiMode() === 'pro' ? 'Pro: fotos en alta definición, razonamiento y una descripción más completa. Mayor tiempo y coste.' : this.aiMode() === 'detailed'
      ? 'Detallado: usa derivado grande y detail=high. Más coste, mejor para placas, etiquetas y texto pequeño.'
      : 'Rápido: usa detail=low. Coste mínimo, identificación más genérica.');
  protected readonly acceptedTechnicalFacts = computed(() => {
    const selected = new Set(this.aiSelectedTechnicalFactKeys());
    const dismissed = new Set(this.dismissedTechnicalFactKeys());
    return this.aiSuggestion()?.technicalFacts
      .filter((fact) => selected.has(this.technicalFactKey(fact)) && !dismissed.has(this.technicalFactKey(fact)))
      .map((fact) => ({ ...fact, value: this.technicalFactValue(fact) })) ?? [];
  });
  protected readonly includedTechnicalFacts = computed(() => {
    const included = new Set(this.aiIncludedTechnicalFactKeys());
    const dismissed = new Set(this.dismissedTechnicalFactKeys());
    return this.aiSuggestion()?.technicalFacts
      .filter((fact) => included.has(this.technicalFactKey(fact)) && !dismissed.has(this.technicalFactKey(fact)))
      .map((fact) => ({ ...fact, value: this.technicalFactValue(fact) })) ?? [];
  });
  protected readonly aiPreviewTagNames = computed(() => {
    const names = new Set<string>();
    const selectedTagIds = new Set(this.draftTagIds());
    for (const tag of this.options().tags) {
      if (selectedTagIds.has(tag.id)) {
        names.add(tag.name);
      }
    }
    for (const tag of this.aiSuggestion()?.suggestedTags ?? []) {
      if (this.isAiTagSelected(tag) && tag.tagId && !selectedTagIds.has(tag.tagId)) {
        names.add(tag.tagName);
      }
    }
    return [...names];
  });
  protected readonly aiPreviewClassification = computed(() => {
    const suggestion = this.aiSuggestion();
    if (this.aiUseClass() && suggestion?.suggestedClass) {
      return [suggestion.suggestedClass.name, suggestion.suggestedSubtype?.name].filter(Boolean).join(' · ');
    }

    const itemClass = this.selectedItemClass();
    const subtype = this.itemSubtypes().find((item) => item.id === this.draftItemSubtypeId()) ?? null;
    return [itemClass?.name, subtype?.name].filter(Boolean).join(' · ');
  });
  protected readonly draftBoxLabel = computed(() => {
    const id = this.draftBoxId();
    return this.boxOptions().find((box) => box.value === id)?.label ?? 'Sin contenedor';
  });
  protected readonly finalPreviewName = computed(() =>
    this.workspaceMode() === 'detailed' && this.aiSuggestion() && this.aiStep() !== 'final'
      ? this.aiPreviewName()
      : this.draftName());
  protected readonly finalPreviewDescription = computed(() =>
    this.workspaceMode() === 'detailed' && this.aiSuggestion() && this.aiStep() !== 'final'
      ? this.aiPreviewDescription()
      : this.draftNotes());
  protected readonly finalPreviewQuantity = computed(() =>
    this.workspaceMode() === 'detailed' && this.aiSuggestion() && this.aiStep() !== 'final'
      ? this.aiPreviewQuantity()
      : this.draftQuantity());
  protected readonly selectionSummary = computed(() => {
    const count = this.selectedIds().length;
    if (count === 0) {
      return 'Sin selección explícita: las acciones usarán solo la foto activa.';
    }

    return `${count} ${count === 1 ? 'foto seleccionada para la acción' : 'fotos seleccionadas para la acción'}.`;
  });
  protected readonly aiSelectionMismatch = computed(() => {
    const suggestion = this.aiSuggestion();
    if (!suggestion || this.aiSuggestionPhotoIds().length === 0) {
      return false;
    }

    return !this.sameIdSet(this.aiSuggestionPhotoIds(), this.selection(this.current()?.id ?? 0));
  });
  protected readonly panel = signal<ReviewPanel>('none');
  protected readonly lastAffectedIds = signal<number[]>([]);
  protected readonly assignBoxId = signal<number | null>(null);
  protected readonly assignItemId = signal<number | null>(null);
  protected readonly draftName = signal('');
  protected readonly draftMarkdown = signal(true);
  protected readonly draftRelations = signal<number[]>([]);
  protected readonly draftNotes = signal('');
  protected readonly draftQuantity = signal(1);
  protected readonly draftUnit = signal('uds');
  protected readonly draftTagIds = signal<number[]>([]);
  protected readonly draftItemClassId = signal<number | null>(null);
  protected readonly draftItemSubtypeId = signal<number | null>(null);
  protected readonly draftIsQuarantined = signal(false);
  protected readonly draftNeedsReview = signal(false);
  protected readonly draftBoxId = signal<number | null>(null);
  protected readonly itemClasses = signal<ItemClass[]>([]);
  protected readonly itemSubtypes = signal<ItemSubtype[]>([]);
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: `${formatInventoryCode(box.code)} · ${box.name}`,
      hint: [box.containerTypeLabel, box.locationName, box.path].filter(Boolean).join(' · '),
      imageUrl: box.coverUrl,
      rotationDegrees: box.rotationDegrees,
      placeholder: formatInventoryCode(box.code)
    })));
  protected readonly tagOptions = computed<SearchableSelectOption[]>(() =>
    this.options().tags.map((tag) => ({ value: tag.id, label: tag.name, hint: tag.color })));
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
    this.itemClasses().find((itemClass) => itemClass.id === this.draftItemClassId()) ?? null);
  protected readonly itemOptions = computed<SearchableSelectOption[]>(() =>
    this.items().map((item) => ({
      value: item.id,
      label: `${formatInventoryCode(item.code)} · ${item.name}`,
      hint: [item.category, formatInventoryCode(item.boxCode)].filter(Boolean).join(' · '),
      imageUrl: item.coverUrl,
      rotationDegrees: item.rotationDegrees,
      placeholder: formatInventoryCode(item.code)
    })));
  protected readonly actionSelectionSize = computed(() => this.selectedIds().length || (this.current() ? 1 : 0));
  protected readonly aiUnavailableReason = computed(() => {
    const status = this.aiStatus();
    if (!status) {
      return 'Comprobando IA...';
    }
    if (!status.isUsable) {
      if (!status.enabled) {
        return 'Activa la IA en Configuración → IA.';
      }
      if (!status.hasApiKey) {
        return 'Configura una API key en Configuración → IA.';
      }
      return status.reason ?? 'Revisa Configuración → IA.';
    }
    return null;
  });

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
    this.api.fetchAiStatus().pipe(
      tap((status) => { this.aiStatus.set(status); if (!this.aiModeManuallySelected()) this.aiMode.set(status.defaultMode === 'pro' ? 'pro' : status.defaultMode === 'normal' ? 'detailed' : 'fast'); }),
      catchError(() => {
        this.aiStatus.set({
          enabled: false,
          provider: 'OpenAI',
          model: '',
          cheapModel: '',
          imageDetail: 'low',
          maxImagesPerRequest: 4,
          defaultMode: 'normal',
          hasApiKey: false,
          keySource: 'None',
          maskedApiKey: null,
          isUsable: false,
          reason: 'No se pudo leer Configuración → IA.',
          maxDescriptionLength: 1200,
          storeRawResponse: true,
          allowSuggestedNewTags: true
        });
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
    this.api.fetchItemClasses().pipe(
      tap((response) => this.itemClasses.set(response.itemClasses)),
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
      onlyUntagged: false,
      onlyQuarantined: false,
      onlyNeedsReview: false,
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

    this.navigateTo(photo.id);
  }

  protected selectOnly(photo: PhotoReviewPhoto): void {
    this.selectedIds.set([photo.id]);
    this.navigateTo(photo.id);
  }

  protected selectAllPending(): void {
    this.selectedIds.set(this.review()?.pending.map((photo) => photo.id) ?? []);
  }

  protected clearSelection(): void {
    this.selectedIds.set([]);
  }

  protected openPanel(panel: ReviewPanel): void {
    const current = this.current();
    if (!current) {
      return;
    }

    if (panel === 'create') {
      this.workspaceMode.set('quick');
      this.draftName.set('');
      this.draftNotes.set('');
    this.draftMarkdown.set(true);this.draftRelations.set([]);
      this.draftQuantity.set(1);
      this.draftUnit.set('uds');
      this.draftBoxId.set(current.sourceBox?.id ?? null);
      this.draftTagIds.set([]);
      this.draftItemClassId.set(null);
      this.draftItemSubtypeId.set(null);
      this.draftIsQuarantined.set(false);
      this.draftNeedsReview.set(false);
      this.itemSubtypes.set([]);
      this.clearAiSuggestion(false);
      this.aiHint.set('');
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

  protected setWorkspaceMode(value: string): void {
    const mode: ReviewWorkspaceMode = value === 'detailed' ? 'detailed' : 'quick';
    this.workspaceMode.set(mode);
    if (mode === 'detailed' && this.aiStep() === 'analyze' && this.aiSuggestion()) {
      this.aiStep.set('review');
    }
    if (mode === 'detailed' && this.aiDetailedRecommended() && !this.aiModeManuallySelected()) {
      this.aiMode.set('detailed');
      this.aiModeAutoNotice.set(true);
    }
  }

  protected openDetailedMode(): void {
    if (this.panel() !== 'create') {
      this.openPanel('create');
    }
    this.setWorkspaceMode('detailed');
    this.focusPanel('create');
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

    const ids = this.selection(current.id);
    if (ids.length > 1 && !window.confirm(`Descartar ${ids.length} fotos seleccionadas?`)) {
      return;
    }

    this.mutate(() => this.api.discardReviewPhotos(current.id, ids), ids.length > 1 ? 'Fotos descartadas.' : 'Foto descartada.');
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

    const subtypeId = this.draftItemSubtypeId();
    if (subtypeId !== null && !this.itemSubtypes().some((subtype) => subtype.id === subtypeId)) {
      this.error.set('El subtipo no pertenece a la clase seleccionada.');
      return;
    }

    this.mutate(() => this.api.createItemFromReviewPhotos(current.id, {
      ids: this.selection(current.id),
      boxId: this.draftBoxId(),
      name: this.draftName().trim(),
      notes: this.draftNotes().trim(),
      descriptionMarkdown: this.draftMarkdown(),
      relatedItemIds: this.draftRelations(),
      quantity: this.draftQuantity(),
      unit: this.draftUnit().trim(),
      itemClassId: this.draftItemClassId(),
      itemSubtypeId: this.draftItemSubtypeId(),
      isQuarantined: this.draftIsQuarantined(),
      needsReview: this.draftNeedsReview(),
      tagIds: this.draftTagIds()
    }), 'Ítem creado desde foto.');
  }

  protected createItemNeedsReview(): void {
    this.draftNeedsReview.set(true);
    this.createItem();
  }

  protected suggestWithAi(mode?: AiAnalysisMode): void {
    const current = this.current();
    const status = this.aiStatus();
    if (!current || this.aiBusy()) {
      return;
    }

    const unavailable = this.aiUnavailableReason();
    if (unavailable) {
      this.error.set(unavailable);
      return;
    }

    const ids = this.selection(current.id);
    if (this.selectedIds().length === 0) {
      this.selectedIds.set(ids);
    }
    if (status && ids.length > status.maxImagesPerRequest) {
      this.error.set(`Máximo ${status.maxImagesPerRequest} fotos por petición IA.`);
      return;
    }

    this.aiBusy.set(true);
    this.dismissedTechnicalFactKeys.set([]);
    this.error.set(null);
    this.message.set(null);
    const analysisMode = mode ?? this.aiMode();
    this.api.suggestReviewItem({
      photoIds: ids,
      mode: analysisMode,
      detail: analysisMode !== 'fast' ? 'high' : 'low',
      webSearch: analysisMode === 'pro' && this.aiWebSearch(),
      userHint: this.aiHint().trim() || undefined
    }).pipe(
      tap((suggestion) => {
        this.aiSuggestion.set(suggestion);
        this.aiSuggestionPhotoIds.set(ids);
        this.initializeAiSelection(suggestion);
        if (this.workspaceMode() === 'detailed') {
          this.aiStep.set('review');
        }
      }),
      catchError((error: unknown) => {
        this.error.set(this.errorMessage(error, 'No se pudo generar la sugerencia IA.'));
        return EMPTY;
      }),
      finalize(() => this.aiBusy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected applyAiName(): void {
    const suggestion = this.aiSuggestion();
    if (suggestion?.proposedName) {
      this.draftName.set(suggestion.proposedName);
    }
  }

  protected applyAiDescription(): void {
    const suggestion = this.aiSuggestion();
    if (suggestion?.proposedDescription) {
      this.draftNotes.set(this.descriptionWithTechnicalFacts(suggestion.proposedDescription, this.acceptedTechnicalFacts()));
    }
  }

  protected applyAiQuantity(): void {
    const suggestion = this.aiSuggestion();
    if (suggestion?.proposedQuantity !== null && suggestion?.proposedQuantity !== undefined) {
      this.draftQuantity.set(suggestion.proposedQuantity);
    }
  }

  protected applyAiTag(tag: AiSuggestedTag): void {
    if (!tag.tagId) {
      return;
    }

    this.draftTagIds.update((current) => current.includes(tag.tagId!) ? current : [...current, tag.tagId!]);
  }

  protected applyAllAiTags(): void {
    for (const tag of this.aiSuggestion()?.suggestedTags ?? []) {
      this.applyAiTag(tag);
    }
  }

  protected applyAiClass(): void {
    const suggestion = this.aiSuggestion();
    if (!suggestion?.suggestedClass) {
      return;
    }

    this.setDraftItemClassId(suggestion.suggestedClass.id);
    if (suggestion.suggestedSubtype) {
      this.draftItemSubtypeId.set(suggestion.suggestedSubtype.id);
    }
  }

  protected applyAllAi(): void {
    this.applySelectedAi();
  }

  protected applySelectedAi(): void {
    if (this.aiSelectionMismatch()) {
      this.error.set('La selección cambió desde esta propuesta IA. Restaura la selección analizada o genera una propuesta nueva.');
      return;
    }

    if (this.aiUseName() && this.aiPreviewName().trim()) {
      this.draftName.set(this.aiPreviewName().trim());
    }
    if (this.aiUseDescription() && this.aiPreviewDescription().trim()) {
      this.draftNotes.set(this.aiPreviewDescription().trim());
    }
    if (this.aiUseQuantity() && this.aiPreviewQuantity() !== null) {
      this.draftQuantity.set(this.aiPreviewQuantity()!);
    }
    for (const tag of this.aiSuggestion()?.suggestedTags ?? []) {
      if (this.aiSelectedTagKeys().includes(this.aiTagKey(tag))) {
        this.applyAiTag(tag);
      }
    }
    if (this.aiUseClass()) {
      this.applyAiClass();
    }
    this.markAiAccepted();
    if (this.workspaceMode() === 'detailed') {
      this.aiStep.set('final');
    }
  }

  protected selectAllAi(): void {
    const suggestion = this.aiSuggestion();
    if (!suggestion) {
      return;
    }

    this.aiUseName.set(!!suggestion.proposedName);
    this.aiUseDescription.set(!!suggestion.proposedDescription);
    this.aiUseQuantity.set(suggestion.proposedQuantity !== null && suggestion.proposedQuantity !== undefined);
    this.aiUseClass.set(!!suggestion.suggestedClass);
    this.aiSelectedTagKeys.set(suggestion.suggestedTags.map((tag) => this.aiTagKey(tag)));
    this.aiIncludedTechnicalFactKeys.set(suggestion.technicalFacts.map((fact) => this.technicalFactKey(fact)));
    this.aiSelectedTechnicalFactKeys.set(suggestion.technicalFacts.map((fact) => this.technicalFactKey(fact)));
    this.refreshAiPreview();
  }

  protected deselectAllAi(): void {
    this.aiUseName.set(false);
    this.aiUseDescription.set(false);
    this.aiUseQuantity.set(false);
    this.aiUseClass.set(false);
    this.aiSelectedTagKeys.set([]);
    this.aiIncludedTechnicalFactKeys.set([]);
    this.aiSelectedTechnicalFactKeys.set([]);
    this.refreshAiPreview();
  }

  protected discardAiSuggestion(markRejected = true): void {
    const id = this.aiSuggestion()?.suggestionId;
    this.clearAiSuggestion(false);
    if (markRejected && id) {
      this.api.rejectAiSuggestion(id).pipe(catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef)).subscribe();
    }
  }

  protected restoreAiSelection(): void {
    const ids = this.aiSuggestionPhotoIds();
    if (ids.length) {
      this.selectedIds.set(ids);
    }
  }

  protected setAiFieldSelected(field: 'name' | 'description' | 'quantity' | 'class', checked: boolean): void {
    switch (field) {
      case 'name':
        this.aiUseName.set(checked);
        break;
      case 'description':
        this.aiUseDescription.set(checked);
        break;
      case 'quantity':
        this.aiUseQuantity.set(checked);
        break;
      case 'class':
        this.aiUseClass.set(checked);
        break;
    }
    this.refreshAiPreview();
  }

  protected setAiTagSelected(tag: AiSuggestedTag, checked: boolean): void {
    this.toggleKey(this.aiSelectedTagKeys, this.aiTagKey(tag), checked);
  }

  protected isAiTagSelected(tag: AiSuggestedTag): boolean {
    return this.aiSelectedTagKeys().includes(this.aiTagKey(tag));
  }

  protected setTechnicalFactSelected(fact: AiTechnicalFact, checked: boolean): void {
    const key = this.technicalFactKey(fact);
    if (checked) {
      this.toggleKey(this.aiIncludedTechnicalFactKeys, key, true);
    }
    this.toggleKey(this.aiSelectedTechnicalFactKeys, key, checked);
    this.refreshAiPreview();
  }

  protected isTechnicalFactSelected(fact: AiTechnicalFact): boolean {
    return this.aiSelectedTechnicalFactKeys().includes(this.technicalFactKey(fact));
  }

  protected setTechnicalFactIncluded(fact: AiTechnicalFact, checked: boolean): void {
    const key = this.technicalFactKey(fact);
    this.toggleKey(this.aiIncludedTechnicalFactKeys, key, checked);
    if (!checked) {
      this.aiSelectedTechnicalFactKeys.update((current) => current.filter((item) => item !== key));
    }
    this.refreshAiPreview();
  }

  protected isTechnicalFactIncluded(fact: AiTechnicalFact): boolean {
    return this.aiIncludedTechnicalFactKeys().includes(this.technicalFactKey(fact));
  }

  protected technicalFactValue(fact: AiTechnicalFact): string {
    const edited = this.editedTechnicalFactValues()[this.technicalFactKey(fact)];
    return edited ?? fact.value;
  }

  protected setTechnicalFactValue(fact: AiTechnicalFact, value: string): void {
    const key = this.technicalFactKey(fact);
    const next = value.trimStart();
    this.editedTechnicalFactValues.update((current) => ({ ...current, [key]: next }));
    this.refreshAiPreview();
  }

  protected markAiAccepted(): void {
    const id = this.aiSuggestion()?.suggestionId;
    if (id) {
      this.api.acceptAiSuggestion(id).pipe(catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef)).subscribe();
    }
  }

  protected confidenceLabel(value: number | null | undefined): string {
    if (value === null || value === undefined) {
      return 'sin confianza';
    }

    return `${Math.round(value * 100)}%`;
  }

  protected setAiHint(value: string): void {
    const next = value.slice(0, 3000);
    this.aiHint.set(next);
    if (this.looksLikeDetailedHint(next) && !this.aiModeManuallySelected() && this.aiMode() === 'fast') {
      this.aiMode.set('detailed');
      this.aiModeAutoNotice.set(true);
      return;
    }

    this.aiModeAutoNotice.set(this.aiModeAutoNotice() && this.looksLikeDetailedHint(next));
  }

  protected setAiMode(value: string): void {
    this.aiModeManuallySelected.set(true);
    this.aiModeAutoNotice.set(false);
    this.aiMode.set(value === 'pro' ? 'pro' : value === 'detailed' ? 'detailed' : 'fast');
  }

  protected setAiDescriptionStyle(value: string): void {
    this.aiDescriptionStyle.set(value === 'technicalSheet' ? 'technicalSheet' : 'narrative');
    this.refreshAiPreview();
    if (this.workspaceMode() === 'detailed' && this.aiStep() === 'final' && this.aiUseDescription()) {
      this.draftNotes.set(this.aiPreviewDescription().trim());
    }
  }

  protected setAiStep(value: string): void {
    if (value === 'review' && !this.aiSuggestion()) {
      return;
    }
    this.aiStep.set(value === 'final' ? 'final' : value === 'review' ? 'review' : 'analyze');
  }

  protected technicalFactSourceLabel(source: string): string {
    switch (source) {
      case 'visual': return 'visual';
      case 'visual_inference': return 'inferencia visual';
      case 'product_knowledge': return 'conocimiento producto';
      case 'web_verified': return 'verificado web';
      case 'user_hint': return 'pista usuario';
      default: return 'no verificado';
    }
  }

  protected applyTechnicalFact(fact: AiTechnicalFact): void {
    const line = this.technicalFactLine({ ...fact, value: this.technicalFactValue(fact) });
    const current = this.draftNotes().trim();
    if (current.includes(line)) {
      return;
    }
    this.draftNotes.set(current ? `${current}\n${line}` : line);
  }

  protected discardTechnicalFact(fact: AiTechnicalFact): void {
    const key = this.technicalFactKey(fact);
    this.dismissedTechnicalFactKeys.update((current) => current.includes(key) ? current : [...current, key]);
    this.aiIncludedTechnicalFactKeys.update((current) => current.filter((item) => item !== key));
    this.aiSelectedTechnicalFactKeys.update((current) => current.filter((item) => item !== key));
    this.refreshAiPreview();
  }

  protected isTechnicalFactDismissed(fact: AiTechnicalFact): boolean {
    return this.dismissedTechnicalFactKeys().includes(this.technicalFactKey(fact));
  }

  private looksLikeDetailedHint(value: string): boolean {
    return /\b(placa|pcb|m[oó]dulo|etiqueta|referencia|ref\.?|modelo|serigraf[ií]a|texto peque[nñ]o|chip|soc|uart|esp|8266|arduino|raspberry)\b/i.test(value);
  }

  private descriptionWithTechnicalFacts(description: string, facts: AiTechnicalFact[]): string {
    const factLines = facts.map((fact) => this.technicalFactLine(fact));
    const uniqueLines = factLines.filter((line, index) => line.length > 0 && factLines.indexOf(line) === index && !description.includes(line));
    if (uniqueLines.length === 0) {
      return description.trim();
    }

    if (this.aiDescriptionStyle() === 'technicalSheet') {
      return `${description.trim()}\n\nFicha técnica:\n${uniqueLines.join('\n')}`;
    }

    const factsText = facts
      .map((fact) => `${fact.label.toLocaleLowerCase()}: ${fact.value}`)
      .filter((line, index, lines) => line.length > 0 && lines.indexOf(line) === index)
      .join('; ');
    return `${description.trim()}\n\nDatos técnicos relevantes: ${factsText}.`;
  }

  private technicalFactLine(fact: AiTechnicalFact): string {
    return `- ${fact.label}: ${fact.value}`;
  }

  protected technicalFactKey(fact: AiTechnicalFact): string {
    return `${fact.key}:${fact.value}`.toLowerCase();
  }

  protected aiTagKey(tag: AiSuggestedTag): string {
    return `${tag.tagId ?? 'new'}:${tag.tagName}`.toLowerCase();
  }

  protected setDraftItemClassId(value: number | string | null | (number | string | null)[]): void {
    const itemClassId = typeof value === 'number' && value > 0 ? value : null;
    this.draftItemClassId.set(itemClassId);
    this.draftItemSubtypeId.set(null);
    this.loadItemSubtypes(itemClassId);
  }

  protected setDraftItemSubtypeId(value: number | string | null | (number | string | null)[]): void {
    const itemSubtypeId = typeof value === 'number' && value > 0 ? value : null;
    if (itemSubtypeId !== null && !this.itemSubtypes().some((subtype) => subtype.id === itemSubtypeId)) {
      this.error.set('El subtipo no pertenece a la clase seleccionada.');
      return;
    }

    this.draftItemSubtypeId.set(itemSubtypeId);
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

  private sameIdSet(left: number[], right: number[]): boolean {
    if (left.length !== right.length) {
      return false;
    }

    const normalizedRight = new Set(right);
    return left.every((id) => normalizedRight.has(id));
  }

  private initializeAiSelection(suggestion: AiSuggestItemResponse): void {
    this.aiUseName.set(!!suggestion.proposedName);
    this.aiUseDescription.set(!!suggestion.proposedDescription);
    this.aiUseQuantity.set(suggestion.proposedQuantity !== null && suggestion.proposedQuantity !== undefined && suggestion.quantityConfidence >= 0.65);
    this.aiUseClass.set(!!suggestion.suggestedClass && suggestion.suggestedClass.confidence >= 0.65);
    this.aiSelectedTagKeys.set(suggestion.suggestedTags.filter((tag) => tag.confidence >= 0.65).map((tag) => this.aiTagKey(tag)));
    this.aiIncludedTechnicalFactKeys.set(suggestion.technicalFacts.map((fact) => this.technicalFactKey(fact)));
    this.aiSelectedTechnicalFactKeys.set(suggestion.technicalFacts.filter((fact) => fact.confidence >= 0.65).map((fact) => this.technicalFactKey(fact)));
    this.refreshAiPreview();
  }

  private refreshAiPreview(): void {
    const suggestion = this.aiSuggestion();
    if (!suggestion) {
      this.aiPreviewName.set('');
      this.aiPreviewDescription.set('');
      this.aiPreviewQuantity.set(null);
      return;
    }

    const baseDescription = this.aiUseDescription()
      ? suggestion.proposedDescription
      : this.draftNotes();
    this.aiPreviewName.set(this.aiUseName() ? suggestion.proposedName : this.draftName());
    this.aiPreviewQuantity.set(this.aiUseQuantity() ? suggestion.proposedQuantity : this.draftQuantity());
    this.aiPreviewDescription.set(this.descriptionWithTechnicalFacts(baseDescription, this.acceptedTechnicalFacts()));
  }

  private toggleKey(target: { update: (updater: (current: string[]) => string[]) => void }, key: string, checked: boolean): void {
    target.update((current) => {
      if (checked) {
        return current.includes(key) ? current : [...current, key];
      }
      return current.filter((item) => item !== key);
    });
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
        this.clearAiSuggestion(false);
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

  private clearAiSuggestion(markRejected = false): void {
    const id = this.aiSuggestion()?.suggestionId;
    this.aiSuggestion.set(null);
    this.aiSuggestionPhotoIds.set([]);
    this.aiUseName.set(false);
    this.aiUseDescription.set(false);
    this.aiUseQuantity.set(false);
    this.aiUseClass.set(false);
    this.aiSelectedTagKeys.set([]);
    this.aiIncludedTechnicalFactKeys.set([]);
    this.aiSelectedTechnicalFactKeys.set([]);
    this.aiPreviewName.set('');
    this.aiPreviewDescription.set('');
    this.aiPreviewQuantity.set(null);
    this.dismissedTechnicalFactKeys.set([]);
    this.editedTechnicalFactValues.set({});
    this.aiDescriptionStyle.set('narrative');
    this.aiStep.set('analyze');
    if (markRejected && id) {
      this.api.rejectAiSuggestion(id).pipe(catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef)).subscribe();
    }
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

  private loadItemSubtypes(itemClassId: number | null): void {
    if (!itemClassId) {
      this.itemSubtypes.set([]);
      return;
    }

    this.api.fetchItemSubtypes(itemClassId).pipe(
      tap((response) => {
        this.itemSubtypes.set(response.itemSubtypes);
        const selectedSubtypeId = this.draftItemSubtypeId();
        if (selectedSubtypeId !== null && !response.itemSubtypes.some((subtype) => subtype.id === selectedSubtypeId)) {
          this.draftItemSubtypeId.set(null);
        }
      }),
      catchError(() => {
        this.itemSubtypes.set([]);
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private errorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { error?: string; message?: string; details?: string } | string | null;
      if (typeof body === 'string') {
        const trimmed = body.trim();
        if (trimmed.startsWith('<!DOCTYPE html') || trimmed.startsWith('<html')) {
          return fallback;
        }
        if (trimmed) {
          return trimmed;
        }
      }
      if (body && typeof body === 'object') {
        const main = body.message || body.error;
        const details = body.details;
        if (main?.trim() && details?.trim()) {
          return `${main} ${details}`;
        }
        if (main?.trim()) {
          return main;
        }
      }
    }

    return error instanceof Error ? error.message : fallback;
  }
}
