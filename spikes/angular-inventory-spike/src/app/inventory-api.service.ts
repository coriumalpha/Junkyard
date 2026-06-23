import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export type InventoryViewMode = 'grouped' | 'flat';

export interface InventoryQueryState {
  q: string;
  box: string;
  includeChildren: boolean;
  onlyConsumable: boolean;
  onlyOrphans: boolean;
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

@Injectable({ providedIn: 'root' })
export class InventoryApiService {
  private readonly http = inject(HttpClient);

  fetchInventory(state: InventoryQueryState): Observable<InventoryLiveResponse> {
    let params = new HttpParams().set('handler', 'Live');

    if (state.q.trim()) {
      params = params.set('q', state.q.trim());
    }

    if (state.box.trim()) {
      params = params.set('box', state.box.trim());
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

    return this.http.get<InventoryLiveResponse>('/items', { params });
  }
}
