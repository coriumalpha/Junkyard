import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export type InventoryViewMode = 'grouped' | 'flat';
export type InventoryLayoutMode = 'grouped' | 'flat' | 'gallery' | 'table';

export interface InventoryQueryState {
  q: string;
  category: string;
  box: string;
  boxIds: number[];
  locationId: number | null;
  includeChildren: boolean;
  onlyConsumable: boolean;
  onlyOrphans: boolean;
  layout: InventoryLayoutMode;
  view: InventoryViewMode;
}

export interface InventoryLiveResponse {
  query: string;
  category: string;
  boxCode: string;
  boxId: number | null;
  boxIds: number[];
  locationId: number | null;
  includeChildren: boolean;
  onlyConsumable: boolean;
  onlyOrphans: boolean;
  viewMode: InventoryViewMode;
  selectedBoxes: InventorySelectedBox[];
  selectedBox: InventoryContext | null;
  selectedLocationName: string | null;
  itemsCount: number;
  groupsCount: number;
  groups: InventoryGroup[];
  items: InventoryItem[];
}

export interface InventoryOptionsResponse {
  categories: string[];
  locations: InventoryOption[];
  boxes: InventoryBoxOption[];
}

export interface InventoryOption {
  id: number;
  name: string;
}

export interface InventoryBoxOption {
  id: number;
  code: string;
  name: string;
  path: string;
  locationName: string | null;
  containerTypeLabel: string;
}

export interface InventorySelectedBox {
  id: number;
  code: string;
  name: string;
  path: string;
  locationDisplay: string | null;
  effectiveLocationSourceLabel: string | null;
  containerTypeLabel: string;
}

export interface InventoryContext {
  code: string;
  name: string;
  path: string;
  locationName: string | null;
  locationSourceLabel: string | null;
  containerTypeLabel: string | null;
  missing: boolean;
}

export interface InventoryGroup {
  boxId: number | null;
  code: string;
  name: string;
  url: string;
  coverUrl: string | null;
  rotationDegrees: number;
  locationName: string | null;
  locationSourceLabel: string | null;
  path: string;
  parentBoxId: number | null;
  isOrphanGroup: boolean;
  childCount: number;
  photoCount: number;
  itemCount: number;
  generatedLabel: string | null;
  items: InventoryItem[];
}

export interface InventoryItem {
  id: number;
  name: string;
  url: string;
  coverUrl: string | null;
  rotationDegrees: number;
  boxCode: string | null;
  boxPath: string | null;
  locationName: string | null;
  category: string;
  quantityLabel: string;
  generatedLabel: string | null;
  consumable: boolean;
  lowStock: boolean;
  sentimental: boolean;
  obsolete: boolean;
}

export interface DashboardResponse {
  locationCount: number;
  boxCount: number;
  itemCount: number;
  lowStockCount: number;
  orphanCount: number;
  photoInboxPendingCount: number;
  recentBoxes: DashboardBox[];
  lowStockItems: DashboardItem[];
  recentPhotos: DashboardPhoto[];
}

export interface DashboardBox {
  id: number;
  code: string;
  name: string;
  url: string;
  containerTypeLabel: string;
  status: string;
  locationName: string | null;
  itemCount: number;
  coverUrl: string | null;
  rotationDegrees: number;
}

export interface DashboardItem {
  id: number;
  name: string;
  url: string;
  boxCode: string | null;
  category: string;
  quantity: number;
  minQuantity: number | null;
  unit: string;
  coverUrl: string | null;
  rotationDegrees: number;
}

export interface DashboardPhoto {
  id: number;
  url: string;
  rotationDegrees: number;
  caption: string | null;
  entityType: string;
  entityId: number;
}

@Injectable({ providedIn: 'root' })
export class InventoryApiService {
  private readonly http = inject(HttpClient);

  fetchInventory(state: InventoryQueryState): Observable<InventoryLiveResponse> {
    let params = new HttpParams().set('handler', 'Live');

    if (state.q.trim()) {
      params = params.set('q', state.q.trim());
    }

    if (state.category.trim()) {
      params = params.set('category', state.category.trim());
    }

    if (state.box.trim()) {
      params = params.set('box', state.box.trim());
    }

    for (const boxId of state.boxIds) {
      params = params.append('boxIds', String(boxId));
    }

    if (state.locationId !== null) {
      params = params.set('locationId', String(state.locationId));
    }

    if (state.includeChildren) {
      params = params.set('includeChildren', 'true');
    }

    if (state.onlyConsumable) {
      params = params.set('onlyConsumable', 'true');
    }

    if (state.onlyOrphans) {
      params = params.set('onlyOrphans', 'true');
    }

    params = params.set('view', state.view);

    return this.http.get<InventoryLiveResponse>('/api/inventory/live', { params });
  }

  fetchOptions(): Observable<InventoryOptionsResponse> {
    return this.http.get<InventoryOptionsResponse>('/api/inventory/options');
  }

  fetchDashboard(): Observable<DashboardResponse> {
    return this.http.get<DashboardResponse>('/api/dashboard');
  }
}
