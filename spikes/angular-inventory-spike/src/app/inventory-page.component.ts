import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { catchError, distinctUntilChanged, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatToolbarModule } from '@angular/material/toolbar';

import { InventoryApiService, InventoryBoxOption, InventoryBoxUpdate, InventoryBulkUpdate, InventoryGroup, InventoryItem, InventoryLayoutMode, InventoryLiveResponse, InventoryOptionsResponse, InventoryQueryState, InventoryViewMode } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';
import { legacyUrl } from './legacy-url';
import { SearchableSelectComponent, SearchableSelectOption } from './searchable-select.component';
import { TagPickerComponent } from './tag-picker.component';

interface FocusVisual {
  kind: 'group' | 'item' | 'summary';
  title: string;
  subtitle: string;
  imageUrl: string | null;
  rotationDegrees: number;
  fallback: string;
}

interface LayoutOption {
  value: InventoryLayoutMode;
  label: string;
  icon: string;
  description: string;
}

interface InventoryGroupNode extends InventoryGroup {
  children: InventoryGroupNode[];
}

const DEFAULT_STATE: InventoryQueryState = {
  q: '',
  category: '',
  tagIds: [],
  box: '',
  boxIds: [],
  locationId: null,
  includeChildren: false,
  onlyConsumable: false,
  onlyOrphans: false,
  layout: 'grouped',
  view: 'grouped'
};

const LAYOUT_OPTIONS: LayoutOption[] = [
  {
    value: 'grouped',
    label: 'Agrupado',
    icon: 'account_tree',
    description: 'Contenedores plegables'
  },
  {
    value: 'flat',
    label: 'Plano',
    icon: 'view_agenda',
    description: 'Ítems como filas densas'
  },
  {
    value: 'gallery',
    label: 'Galería',
    icon: 'photo_library',
    description: 'Fotos por encima de metadatos'
  },
  {
    value: 'table',
    label: 'Tabla',
    icon: 'table_rows',
    description: 'Base preparada para tabla futura'
  },
  {
    value: 'containers',
    label: 'Contenedores',
    icon: 'inventory',
    description: 'Gestión visual de cajas'
  }
];

const LAYOUT_TO_VIEW: Record<InventoryLayoutMode, InventoryViewMode> = {
  grouped: 'grouped',
  flat: 'flat',
  gallery: 'flat',
  table: 'flat',
  containers: 'grouped'
};

const CONTAINER_TYPE_OPTIONS: SearchableSelectOption[] = [
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

const BOX_STATUS_OPTIONS: SearchableSelectOption[] = ['Active', 'Quarantine', 'Archived']
  .map((status) => ({ value: status, label: status }));

@Component({
  selector: 'app-inventory-page',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatCheckboxModule,
    MatChipsModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    SearchableSelectComponent,
    TagPickerComponent,
    InventoryCodePipe,
    MatSlideToggleModule,
    MatToolbarModule,
    RouterLink
  ],
  templateUrl: './inventory-page.component.html',
  styleUrl: './inventory-page.component.scss'
})
export class InventoryPageComponent {
  protected readonly state = signal<InventoryQueryState>({ ...DEFAULT_STATE });
  protected readonly data = signal<InventoryLiveResponse | null>(null);
  protected readonly options = signal<InventoryOptionsResponse>({ categories: [], tags: [], conditions: [], locations: [], boxes: [] });
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly expandedGroups = signal<Record<string, boolean>>({});
  protected readonly groupPages = signal<Record<string, number>>({});
  protected readonly editMode = signal(false);
  protected readonly selectedItemIds = signal<number[]>([]);
  protected readonly bulkTagId = signal<number | null>(null);
  protected readonly bulkBoxId = signal<number | null>(null);
  protected readonly bulkBusy = signal(false);
  protected readonly bulkMessage = signal<string | null>(null);
  protected readonly showCreateBoxForm = signal(false);
  protected readonly createBoxForm = signal<InventoryBoxUpdate>(this.emptyBoxForm());
  protected readonly createBoxBusy = signal(false);
  protected readonly createBoxMessage = signal<string | null>(null);
  protected readonly createBoxError = signal<string | null>(null);
  protected readonly groups = computed(() => this.data()?.groups ?? []);
  protected readonly groupTree = computed(() => this.buildGroupTree(this.groups()));
  protected readonly items = computed(() => this.data()?.items ?? []);
  protected readonly visibleItems = computed(() => this.state().layout === 'grouped'
    ? this.groups().flatMap((group) => group.items)
    : this.items());
  protected readonly selectedItemsCount = computed(() => this.selectedItemIds().length);
  protected readonly shouldAutoExpandGroups = computed(() => {
    const state = this.state();
    return state.layout === 'grouped'
      && Boolean(state.q.trim() || state.category.trim() || state.tagIds.length || state.boxIds.length || state.locationId !== null || state.onlyConsumable || state.onlyOrphans);
  });
  protected readonly filteredContainers = computed(() => {
    const state = this.state();
    const query = state.q.trim().toLocaleLowerCase();
    const selectedLocationName = state.locationId === null ? null : this.locationLabel(state.locationId);
    return this.options().boxes.filter((box) => {
      if (selectedLocationName !== null && box.locationName !== selectedLocationName) {
        return false;
      }

      if (!query) {
        return true;
      }

      return [box.code, box.name, box.path, box.locationName, box.containerTypeLabel]
        .filter(Boolean)
        .some((value) => value!.toLocaleLowerCase().includes(query));
    });
  });
  protected readonly focusVisuals = computed(() => this.buildFocusVisuals());
  protected readonly selectedBoxOptions = computed(() => {
    const selected = new Set(this.state().boxIds);
    return this.options().boxes.filter((box) => selected.has(box.id));
  });
  protected readonly tagOptions = computed<SearchableSelectOption[]>(() =>
    this.options().tags.map((tag) => ({ value: tag.id, label: tag.name, hint: tag.color })));
  protected readonly locationOptions = computed<SearchableSelectOption[]>(() =>
    this.options().locations.map((location) => ({ value: location.id, label: location.name })));
  protected readonly boxOptions = computed<SearchableSelectOption[]>(() =>
    this.options().boxes.map((box) => ({
      value: box.id,
      label: this.boxOptionLabel(box),
      hint: this.boxOptionHint(box),
      imageUrl: box.coverUrl,
      rotationDegrees: box.rotationDegrees,
      placeholder: box.code
    })));
  protected readonly layoutOptions = LAYOUT_OPTIONS;
  protected readonly containerTypeOptions = CONTAINER_TYPE_OPTIONS;
  protected readonly boxStatusOptions = BOX_STATUS_OPTIONS;
  protected readonly groupPageSize = 12;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly stateKey = (state: InventoryQueryState) => JSON.stringify(state);

  constructor() {
    this.route.queryParamMap.pipe(
      map((params) => this.parseState(params)),
      distinctUntilChanged((left, right) => this.stateKey(left) === this.stateKey(right)),
      tap((state) => this.state.set(state)),
      switchMap((state) => {
        this.loading.set(true);
        this.error.set(null);
        return this.api.fetchInventory(state).pipe(
          tap((response) => {
            this.data.set(response);
            const visibleIds = new Set(response.items.map((item) => item.id));
            this.selectedItemIds.update((current) => current.filter((id) => visibleIds.has(id)));
          }),
          catchError((error: unknown) => {
            this.error.set(this.describeError(error));
            this.data.set(null);
            return EMPTY;
          }),
          finalize(() => this.loading.set(false))
        );
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();

    this.refreshOptions();

  }

  protected setQuery(value: string): void {
    this.navigate({ q: value });
  }

  protected setCategory(value: string): void {
    this.navigate({ category: value });
  }

  protected setTagIds(value: number[]): void {
    this.navigate({ tagIds: [...value], category: '' });
  }

  protected removeTagId(value: number): void {
    this.navigate({ tagIds: this.state().tagIds.filter((tagId) => tagId !== value) });
  }

  protected setBox(value: string): void {
    this.navigate({ box: value, boxIds: [] });
  }

  protected setBoxIds(value: number[]): void {
    this.navigate({ boxIds: [...value], box: '' });
  }

  protected removeBoxId(value: number): void {
    this.navigate({ boxIds: this.state().boxIds.filter((boxId) => boxId !== value) });
  }

  protected clearBoxIds(): void {
    this.navigate({ boxIds: [], box: '' });
  }

  protected setLocationId(value: number | null): void {
    this.navigate({ locationId: value });
  }

  protected setLayout(value: InventoryLayoutMode): void {
    this.navigate({ layout: value, view: this.deriveBackendView(value) });
  }

  protected setIncludeChildren(value: boolean): void {
    this.navigate({ includeChildren: value });
  }

  protected setOnlyConsumable(value: boolean): void {
    this.navigate({
      onlyConsumable: value,
      onlyOrphans: value ? false : this.state().onlyOrphans
    });
  }

  protected setOnlyOrphans(value: boolean): void {
    this.navigate({
      onlyOrphans: value,
      onlyConsumable: value ? false : this.state().onlyConsumable
    });
  }

  protected toggleEditMode(): void {
    this.editMode.update((value) => !value);
    this.bulkMessage.set(null);
    if (!this.editMode()) {
      this.clearSelection();
    }
  }

  protected isItemSelected(item: InventoryItem): boolean {
    return this.selectedItemIds().includes(item.id);
  }

  protected toggleItemSelection(item: InventoryItem, event?: unknown): void {
    const maybeEvent = event as { stopPropagation?: () => void; preventDefault?: () => void } | undefined;
    if (typeof maybeEvent?.stopPropagation === 'function') {
      maybeEvent.stopPropagation();
    }

    if (typeof maybeEvent?.preventDefault === 'function') {
      maybeEvent.preventDefault();
    }

    this.selectedItemIds.update((current) =>
      current.includes(item.id) ? current.filter((id) => id !== item.id) : [...current, item.id]);
  }

  protected selectAllVisible(): void {
    this.selectedItemIds.set(Array.from(new Set(this.visibleItems().map((item) => item.id))));
  }

  protected clearSelection(): void {
    this.selectedItemIds.set([]);
    this.bulkTagId.set(null);
    this.bulkBoxId.set(null);
  }

  protected bulkMoveToBox(): void {
    const boxId = this.bulkBoxId();
    if (!boxId) {
      this.bulkMessage.set('Selecciona un contenedor destino.');
      return;
    }

    this.runBulk({ moveToBoxId: boxId }, 'Ítems movidos.');
  }

  protected bulkAddTag(): void {
    const tagId = this.bulkTagId();
    if (!tagId) {
      this.bulkMessage.set('Selecciona un tag.');
      return;
    }

    this.runBulk({ addTagId: tagId }, 'Tag añadido a la selección.');
  }

  protected bulkRemoveTag(): void {
    const tagId = this.bulkTagId();
    if (!tagId) {
      this.bulkMessage.set('Selecciona un tag.');
      return;
    }

    this.runBulk({ removeTagId: tagId }, 'Tag retirado de la selección.');
  }

  protected toggleCreateBoxForm(): void {
    this.showCreateBoxForm.update((visible) => !visible);
    this.createBoxError.set(null);
    this.createBoxMessage.set(null);
  }

  protected patchCreateBoxForm(patch: Partial<InventoryBoxUpdate>): void {
    this.createBoxForm.update((current) => ({ ...current, ...patch }));
    this.createBoxError.set(null);
    this.createBoxMessage.set(null);
  }

  protected createBox(): void {
    if (this.createBoxBusy()) {
      return;
    }

    this.createBoxBusy.set(true);
    this.createBoxError.set(null);
    this.createBoxMessage.set(null);
    this.api.createBox(this.createBoxForm()).pipe(
      switchMap((box) => this.api.fetchOptions().pipe(
        tap((options) => {
          this.options.set(options);
          this.createBoxForm.set(this.emptyBoxForm());
          this.showCreateBoxForm.set(false);
          this.createBoxMessage.set(`Contenedor ${box.code} creado.`);
          this.router.navigate(['/boxes', box.code]).catch(() => undefined);
        })
      )),
      catchError((error: unknown) => {
        this.createBoxError.set(this.describeError(error));
        return EMPTY;
      }),
      finalize(() => this.createBoxBusy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected backendUrl(path: string | null | undefined): string {
    return legacyUrl(path);
  }

  protected assetUrl(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }

    if (path.startsWith('http://') || path.startsWith('https://')) {
      return path;
    }

    return path.startsWith('/') ? path : `/${path}`;
  }

  protected currentScopeBackendUrl(): string {
    const state = this.state();
    const params = new URLSearchParams();

    if (state.q.trim()) {
      params.set('q', state.q.trim());
    }

    if (state.box.trim()) {
      params.set('box', state.box.trim());
    }

    for (const boxId of state.boxIds) {
      params.append('boxIds', String(boxId));
    }

    if (state.category.trim()) {
      params.set('category', state.category.trim());
    }

    for (const tagId of state.tagIds) {
      params.append('tagIds', String(tagId));
    }

    if (state.locationId !== null) {
      params.set('locationId', String(state.locationId));
    }

    if (state.includeChildren) {
      params.set('includeChildren', 'true');
    }

    if (state.onlyConsumable) {
      params.set('onlyConsumable', 'true');
    }

    if (state.onlyOrphans) {
      params.set('onlyOrphans', 'true');
    }

    params.set('view', state.view);
    return this.backendUrl(`/items?${params.toString()}`);
  }

  protected badgeClass(item: InventoryItem): string {
    if (item.lowStock) {
      return 'badge low';
    }

    if (item.obsolete) {
      return 'badge muted';
    }

    if (item.sentimental) {
      return 'badge warm';
    }

    if (item.consumable) {
      return 'badge cool';
    }

    return 'badge';
  }

  protected filterSummary(): string[] {
    const state = this.state();
    const summary: string[] = [];

    if (state.q.trim()) {
      summary.push(`q=${state.q.trim()}`);
    }

    if (state.category.trim()) {
      summary.push(`categoría=${state.category.trim()}`);
    }

    if (state.tagIds.length) {
      summary.push(`${state.tagIds.length} tags`);
    }

    if (state.box.trim()) {
      summary.push(`box=${state.box.trim()}`);
    }

    if (state.boxIds.length) {
      summary.push(`${state.boxIds.length} CT`);
    }

    if (state.locationId !== null) {
      summary.push(this.locationLabel(state.locationId));
    }

    if (state.includeChildren) {
      summary.push('hijos incluidos');
    }

    if (state.onlyConsumable) {
      summary.push('solo consumibles');
    }

    if (state.onlyOrphans) {
      summary.push('huérfanos');
    }

    summary.push(this.layoutLabel(state.layout));
    return summary;
  }

  protected layoutLabel(layout: InventoryLayoutMode): string {
    return this.layoutOptions.find((option) => option.value === layout)?.label ?? layout;
  }

  protected isActiveLayout(layout: InventoryLayoutMode): boolean {
    return this.state().layout === layout;
  }

  protected trackByGroup(_index: number, group: InventoryGroup): string {
    return this.groupKey(group);
  }

  protected trackByItem(_index: number, item: InventoryItem): number {
    return item.id;
  }

  protected trackByFocusVisual(_index: number, visual: FocusVisual): string {
    return `${visual.kind}:${visual.title}:${visual.subtitle}`;
  }

  protected previewItems(group: InventoryGroup): InventoryItem[] {
    return group.items.slice(0, 2);
  }

  protected remainingPreviewCount(group: InventoryGroup): number {
    return Math.max(group.itemCount - this.previewItems(group).length, 0);
  }

  protected groupKey(group: InventoryGroup): string {
    return `${group.boxId ?? 'orphan'}:${group.code}`;
  }

  protected isGroupExpanded(group: InventoryGroup): boolean {
    if (this.shouldAutoExpandGroups()) {
      return true;
    }

    return this.expandedGroups()[this.groupKey(group)] ?? false;
  }

  protected setGroupExpanded(group: InventoryGroup, expanded: boolean): void {
    const key = this.groupKey(group);
    this.expandedGroups.update((current) => ({ ...current, [key]: expanded }));

    if (expanded && this.groupPages()[key] === undefined) {
      this.groupPages.update((current) => ({ ...current, [key]: 0 }));
    }
  }

  protected pagedGroupItems(group: InventoryGroup): InventoryItem[] {
    const page = this.groupPage(group);
    const start = page * this.groupPageSize;
    return group.items.slice(start, start + this.groupPageSize);
  }

  protected groupPage(group: InventoryGroup): number {
    return this.groupPages()[this.groupKey(group)] ?? 0;
  }

  protected groupPageCount(group: InventoryGroup): number {
    return Math.max(1, Math.ceil(group.items.length / this.groupPageSize));
  }

  protected groupPageStart(group: InventoryGroup): number {
    if (!group.items.length) {
      return 0;
    }

    return this.groupPage(group) * this.groupPageSize + 1;
  }

  protected groupPageEnd(group: InventoryGroup): number {
    return Math.min((this.groupPage(group) + 1) * this.groupPageSize, group.items.length);
  }

  protected setGroupPage(group: InventoryGroup, page: number): void {
    const key = this.groupKey(group);
    const boundedPage = Math.min(Math.max(page, 0), this.groupPageCount(group) - 1);
    this.groupPages.update((current) => ({ ...current, [key]: boundedPage }));
  }

  protected hasPreviousGroupPage(group: InventoryGroup): boolean {
    return this.groupPage(group) > 0;
  }

  protected hasNextGroupPage(group: InventoryGroup): boolean {
    return this.groupPage(group) < this.groupPageCount(group) - 1;
  }

  protected hasGroupChildren(group: InventoryGroupNode): boolean {
    return group.children.length > 0;
  }

  protected boxOptionLabel(box: InventoryBoxOption): string {
    return `${box.code} · ${box.name}`;
  }

  protected boxOptionHint(box: InventoryBoxOption): string {
    return [box.containerTypeLabel, box.locationName, box.path]
      .filter(Boolean)
      .join(' · ');
  }

  protected locationLabel(locationId: number): string {
    return this.options().locations.find((location) => location.id === locationId)?.name ?? `ubicación ${locationId}`;
  }

  protected layoutGridClass(): string {
    return `layout-${this.state().layout}`;
  }

  protected readInputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | null)?.value ?? '';
  }

  protected readLocationId(value: number | string | null | (number | string | null)[]): number {
    return typeof value === 'number' && value > 0 ? value : 0;
  }

  protected readNullableBoxId(value: number | string | null | (number | string | null)[]): number | null {
    return typeof value === 'number' && value > 0 ? value : null;
  }

  protected readSelectText(value: number | string | null | (number | string | null)[], fallback: string): string {
    return typeof value === 'string' && value.trim() ? value : fallback;
  }

  private runBulk(action: Omit<InventoryBulkUpdate, 'itemIds'>, success: string): void {
    const itemIds = this.selectedItemIds();
    if (!itemIds.length || this.bulkBusy()) {
      this.bulkMessage.set('Selecciona al menos un ítem.');
      return;
    }

    this.bulkBusy.set(true);
    this.bulkMessage.set(null);
    this.api.bulkUpdateItems({ itemIds, ...action }).pipe(
      switchMap((response) => this.api.fetchInventory(this.state()).pipe(
        tap((data) => {
          this.data.set(data);
          this.bulkMessage.set(`${success} ${response.updatedCount} actualizados.`);
          this.clearSelection();
        })
      )),
      catchError((error: unknown) => {
        this.bulkMessage.set(this.describeError(error));
        return EMPTY;
      }),
      finalize(() => this.bulkBusy.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private navigate(patch: Partial<InventoryQueryState>): void {
    const next: InventoryQueryState = {
      ...this.state(),
      ...patch
    };

    next.view = this.deriveBackendView(next.layout);

    if (next.onlyConsumable && next.onlyOrphans) {
      if (patch.onlyConsumable) {
        next.onlyOrphans = false;
      } else if (patch.onlyOrphans) {
        next.onlyConsumable = false;
      }
    }

    next.boxIds = Array.from(new Set(next.boxIds.filter((boxId) => boxId > 0))).sort((left, right) => left - right);
    next.tagIds = Array.from(new Set(next.tagIds.filter((tagId) => tagId > 0))).sort((left, right) => left - right);

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: this.buildQueryParams(next)
    }).catch(() => undefined);
  }

  private buildQueryParams(state: InventoryQueryState): Record<string, string> {
    const params: Record<string, string> = {
      layout: state.layout,
      view: state.view
    };

    if (state.q.trim()) {
      params['q'] = state.q.trim();
    }

    if (state.category.trim()) {
      params['category'] = state.category.trim();
    }

    if (state.tagIds.length) {
      params['tagIds'] = state.tagIds.join(',');
    }

    if (state.box.trim()) {
      params['box'] = state.box.trim();
    }

    if (state.boxIds.length) {
      params['boxIds'] = state.boxIds.join(',');
    }

    if (state.locationId !== null) {
      params['locationId'] = String(state.locationId);
    }

    if (state.includeChildren) {
      params['includeChildren'] = 'true';
    }

    if (state.onlyConsumable) {
      params['onlyConsumable'] = 'true';
    }

    if (state.onlyOrphans) {
      params['onlyOrphans'] = 'true';
    }

    return params;
  }

  private parseState(params: ParamMap): InventoryQueryState {
    const serverView = params.get('view') === 'flat' ? 'flat' : 'grouped';
    const layoutParam = params.get('layout');
    const layout = this.isLayoutMode(layoutParam) ? layoutParam : serverView;

    return {
      q: params.get('q') ?? '',
      category: params.get('category') ?? '',
      tagIds: this.parseIds(params, 'tagIds'),
      box: params.get('box') ?? '',
      boxIds: this.parseBoxIds(params),
      locationId: this.parseNumber(params.get('locationId')),
      includeChildren: params.get('includeChildren') === 'true',
      onlyConsumable: params.get('onlyConsumable') === 'true',
      onlyOrphans: params.get('onlyOrphans') === 'true',
      layout,
      view: this.deriveBackendView(layout)
    };
  }

  private parseBoxIds(params: ParamMap): number[] {
    return this.parseIds(params, 'boxIds');
  }

  private parseIds(params: ParamMap, key: string): number[] {
    return params.getAll(key)
      .flatMap((value) => value.split(','))
      .map((value) => Number.parseInt(value, 10))
      .filter((value) => Number.isInteger(value) && value > 0);
  }

  private parseNumber(value: string | null): number | null {
    if (!value) {
      return null;
    }

    const parsed = Number.parseInt(value, 10);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }

  private isLayoutMode(value: string | null): value is InventoryLayoutMode {
    return value === 'grouped' || value === 'flat' || value === 'gallery' || value === 'table' || value === 'containers';
  }

  private deriveBackendView(layout: InventoryLayoutMode): InventoryViewMode {
    return LAYOUT_TO_VIEW[layout];
  }

  private refreshOptions(): void {
    this.api.fetchOptions().pipe(
      tap((options) => this.options.set(options)),
      catchError(() => EMPTY),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
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

  private buildFocusVisuals(): FocusVisual[] {
    const data = this.data();
    if (!data) {
      return [];
    }

    const visuals: FocusVisual[] = [];

    for (const group of data.groups) {
      if (!group.coverUrl) {
        continue;
      }

      visuals.push({
        kind: 'group',
        title: group.code,
        subtitle: group.name,
        imageUrl: this.assetUrl(group.coverUrl),
        rotationDegrees: group.rotationDegrees,
        fallback: group.generatedLabel || group.code.slice(0, 1)
      });

      if (visuals.length >= 2) {
        break;
      }
    }

    if (visuals.length < 2) {
      for (const item of data.items) {
        if (!item.coverUrl) {
          continue;
        }

        visuals.push({
          kind: 'item',
          title: `${item.code} · ${item.name}`,
          subtitle: item.boxCode || this.tagSummary(item),
          imageUrl: this.assetUrl(item.coverUrl),
          rotationDegrees: item.rotationDegrees,
          fallback: item.generatedLabel || item.name.slice(0, 1)
        });

        if (visuals.length >= 3) {
          break;
        }
      }
    }

    visuals.push({
      kind: 'summary',
      title: `${data.itemsCount} ítems`,
      subtitle: `${data.groupsCount} grupos · ${data.selectedBoxes.length} contenedores`,
      imageUrl: null,
      rotationDegrees: 0,
      fallback: data.viewMode === 'flat' ? 'PLANO' : 'GRP'
    });

    return visuals.slice(0, 4);
  }

  protected selectedTagLabel(tagId: number): string {
    return this.options().tags.find((tag) => tag.id === tagId)?.name ?? `tag ${tagId}`;
  }

  protected tagSummary(item: InventoryItem): string {
    return item.tags.length ? item.tags.map((tag) => tag.name).join(', ') : item.category;
  }

  private buildGroupTree(groups: InventoryGroup[]): InventoryGroupNode[] {
    const nodes = new Map<number, InventoryGroupNode>();
    const roots: InventoryGroupNode[] = [];

    for (const group of groups) {
      if (group.boxId === null) {
        continue;
      }

      nodes.set(group.boxId, { ...group, children: [] });
    }

    for (const group of groups) {
      if (group.boxId === null) {
        roots.push({ ...group, children: [] });
        continue;
      }

      const node = nodes.get(group.boxId);
      if (!node) {
        continue;
      }

      if (group.parentBoxId !== null && nodes.has(group.parentBoxId)) {
        nodes.get(group.parentBoxId)!.children.push(node);
      } else {
        roots.push(node);
      }
    }

    const sortNodes = (entries: InventoryGroupNode[]): InventoryGroupNode[] => entries
      .sort((left, right) => left.path.localeCompare(right.path))
      .map((entry) => ({
        ...entry,
        children: sortNodes(entry.children)
      }));

    return sortNodes(roots);
  }

  private describeError(error: unknown): string {
    if (error && typeof error === 'object' && 'message' in error && typeof (error as { message?: unknown }).message === 'string') {
      return (error as { message: string }).message;
    }

    return 'No se pudo cargar Inventario desde el backend.';
  }
}
