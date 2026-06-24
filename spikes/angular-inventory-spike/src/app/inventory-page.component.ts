import { CommonModule } from '@angular/common';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { catchError, distinctUntilChanged, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';

import { InventoryApiService, InventoryGroup, InventoryItem, InventoryLayoutMode, InventoryLiveResponse, InventoryQueryState, InventoryViewMode } from './inventory-api.service';

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
  box: '',
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
  }
];

const LAYOUT_TO_VIEW: Record<InventoryLayoutMode, InventoryViewMode> = {
  grouped: 'grouped',
  flat: 'flat',
  gallery: 'flat',
  table: 'flat'
};

@Component({
  selector: 'app-inventory-page',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatChipsModule,
    MatDividerModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSidenavModule,
    MatToolbarModule
  ],
  templateUrl: './inventory-page.component.html',
  styleUrl: './inventory-page.component.scss'
})
export class InventoryPageComponent {
  protected readonly backendOrigin = `${globalThis.location?.protocol ?? 'http:'}//${globalThis.location?.hostname ?? '127.0.0.1'}:8089`;
  protected readonly state = signal<InventoryQueryState>({ ...DEFAULT_STATE });
  protected readonly data = signal<InventoryLiveResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly drawerCollapsed = signal(false);
  protected readonly drawerOpen = signal(true);
  protected readonly handset = signal(false);
  protected readonly expandedGroups = signal<Record<string, boolean>>({});
  protected readonly groupPages = signal<Record<string, number>>({});
  protected readonly groups = computed(() => this.data()?.groups ?? []);
  protected readonly groupTree = computed(() => this.buildGroupTree(this.groups()));
  protected readonly items = computed(() => this.data()?.items ?? []);
  protected readonly focusVisuals = computed(() => this.buildFocusVisuals());
  protected readonly layoutOptions = LAYOUT_OPTIONS;
  protected readonly groupPageSize = 12;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly breakpointObserver = inject(BreakpointObserver);
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
          tap((response) => this.data.set(response)),
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

    this.breakpointObserver.observe(['(max-width: 1024px)']).pipe(
      map((result) => result.matches),
      distinctUntilChanged(),
      tap((matches) => {
        this.handset.set(matches);
        this.drawerOpen.set(!matches);
        if (matches) {
          this.drawerCollapsed.set(false);
        }
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected setQuery(value: string): void {
    this.navigate({ q: value });
  }

  protected setBox(value: string): void {
    this.navigate({ box: value });
  }

  protected setLayout(value: InventoryLayoutMode): void {
    this.navigate({ layout: value, view: this.deriveBackendView(value) });
    if (this.handset()) {
      this.drawerOpen.set(false);
    }
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

  protected toggleDrawer(): void {
    if (this.handset()) {
      this.drawerOpen.update((value) => !value);
      return;
    }

    this.drawerCollapsed.update((value) => !value);
  }

  protected openDrawer(): void {
    this.drawerOpen.set(true);
  }

  protected closeDrawer(): void {
    if (this.handset()) {
      this.drawerOpen.set(false);
    }
  }

  protected backendUrl(path: string | null | undefined): string {
    if (!path) {
      return this.backendOrigin;
    }

    if (path.startsWith('http://') || path.startsWith('https://')) {
      return path;
    }

    return `${this.backendOrigin}${path.startsWith('/') ? '' : '/'}${path}`;
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

    if (state.box.trim()) {
      summary.push(`box=${state.box.trim()}`);
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

  protected layoutGridClass(): string {
    return `layout-${this.state().layout}`;
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

    if (state.box.trim()) {
      params['box'] = state.box.trim();
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
      box: params.get('box') ?? '',
      includeChildren: params.get('includeChildren') === 'true',
      onlyConsumable: params.get('onlyConsumable') === 'true',
      onlyOrphans: params.get('onlyOrphans') === 'true',
      layout,
      view: this.deriveBackendView(layout)
    };
  }

  private isLayoutMode(value: string | null): value is InventoryLayoutMode {
    return value === 'grouped' || value === 'flat' || value === 'gallery' || value === 'table';
  }

  private deriveBackendView(layout: InventoryLayoutMode): InventoryViewMode {
    return LAYOUT_TO_VIEW[layout];
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
          title: item.name,
          subtitle: item.boxCode || item.category,
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
