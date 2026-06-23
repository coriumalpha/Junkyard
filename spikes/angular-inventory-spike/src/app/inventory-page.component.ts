import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { catchError, distinctUntilChanged, EMPTY, finalize, map, switchMap, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatToolbarModule } from '@angular/material/toolbar';

import { InventoryApiService, InventoryGroup, InventoryItem, InventoryLiveResponse, InventoryQueryState, InventoryViewMode } from './inventory-api.service';

const DEFAULT_STATE: InventoryQueryState = {
  q: '',
  box: '',
  includeChildren: false,
  onlyConsumable: false,
  onlyOrphans: false,
  view: 'grouped'
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
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatToolbarModule
  ],
  templateUrl: './inventory-page.component.html',
  styleUrl: './inventory-page.component.scss'
})
export class InventoryPageComponent {
  protected readonly backendOrigin = 'http://127.0.0.1:8088';
  protected readonly state = signal<InventoryQueryState>({ ...DEFAULT_STATE });
  protected readonly data = signal<InventoryLiveResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly groups = computed(() => this.data()?.groups ?? []);
  protected readonly items = computed(() => this.data()?.items ?? []);

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
  }

  protected setQuery(value: string): void {
    this.navigate({ q: value });
  }

  protected setBox(value: string): void {
    this.navigate({ box: value });
  }

  protected setView(value: string): void {
    this.navigate({ view: value === 'flat' ? 'flat' : 'grouped' });
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

    return this.backendUrl(path);
  }

  protected currentScopeBackendUrl(): string {
    const state = this.state();
    const params = new URLSearchParams();

    if (state.box.trim()) {
      params.set('box', state.box.trim());
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

    summary.push(state.view === 'flat' ? 'vista plana' : 'vista agrupada');
    return summary;
  }

  protected trackByGroup(_index: number, group: InventoryGroup): string {
    return `${group.boxId ?? 'orphan'}:${group.code}`;
  }

  protected trackByItem(_index: number, item: InventoryItem): number {
    return item.id;
  }

  private navigate(patch: Partial<InventoryQueryState>): void {
    const next: InventoryQueryState = {
      ...this.state(),
      ...patch
    };

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
    return {
      q: params.get('q') ?? '',
      box: params.get('box') ?? '',
      includeChildren: params.get('includeChildren') === 'true',
      onlyConsumable: params.get('onlyConsumable') === 'true',
      onlyOrphans: params.get('onlyOrphans') === 'true',
      view: params.get('view') === 'flat' ? 'flat' : 'grouped'
    };
  }

  private describeError(error: unknown): string {
    if (error && typeof error === 'object' && 'message' in error && typeof (error as { message?: unknown }).message === 'string') {
      return (error as { message: string }).message;
    }

    return 'No se pudo cargar Inventario desde el backend.';
  }
}
