import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export type InventoryViewMode = 'grouped' | 'flat';
export type InventoryLayoutMode = 'grouped' | 'flat' | 'gallery' | 'table';

export interface InventoryQueryState {
  q: string;
  category: string;
  tagIds: number[];
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
  tagIds: number[];
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
  tags: InventoryTag[];
  conditions: InventoryCondition[];
  locations: InventoryOption[];
  boxes: InventoryBoxOption[];
}

export interface TagsResponse {
  tags: InventoryTag[];
}

export interface InventoryTag {
  id: number;
  name: string;
  color: string;
}

export interface ConditionsResponse {
  conditions: InventoryCondition[];
}

export interface InventoryCondition {
  id: number;
  name: string;
  color: string;
}

export interface TagUpdate {
  name: string;
  color: string;
}

export interface InventoryOption {
  id: number;
  name: string;
}

export interface InventoryLocation {
  id: number;
  name: string;
  description: string | null;
  boxesCount: number;
}

export interface LocationsResponse {
  locations: InventoryLocation[];
}

export interface LocationUpdate {
  name: string;
  description: string;
}

export interface LocationArchiveResponse {
  movedBoxes: number;
}

export interface InventoryBoxOption {
  id: number;
  code: string;
  name: string;
  path: string;
  locationName: string | null;
  containerTypeLabel: string;
  coverUrl: string | null;
  rotationDegrees: number;
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
  code: string;
  name: string;
  url: string;
  coverUrl: string | null;
  rotationDegrees: number;
  boxCode: string | null;
  boxPath: string | null;
  locationName: string | null;
  category: string;
  tags: InventoryTag[];
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

export interface InventoryActionsResponse {
  openCount: number;
  completedCount: number;
  actions: InventoryAction[];
}

export interface InventoryAction {
  id: number;
  title: string;
  description: string | null;
  kind: string;
  priority: number;
  status: string;
  linkedLabel: string;
  spaUrl: string | null;
  legacyUrl: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface InventoryItemDetail {
  id: number;
  code: string;
  name: string;
  category: string;
  tags: InventoryTag[];
  quantityLabel: string;
  quantity: number;
  unit: string;
  minQuantity: number | null;
  condition: string | null;
  retention: string | null;
  notes: string | null;
  consumable: boolean;
  lowStock: boolean;
  sentimental: boolean;
  obsolete: boolean;
  archived: boolean;
  createdAt: string;
  updatedAt: string;
  box: InventoryItemBox | null;
  legacyUrl: string;
  photos: InventoryPhoto[];
  actions: InventoryAction[];
  comments: InventoryAction[];
}

export interface InventoryItemUpdate {
  code: string;
  name: string;
  category: string;
  tagIds: number[];
  quantity: number;
  unit: string;
  minQuantity: number | null;
  condition: string;
  retention: string;
  consumable: boolean;
  sentimental: boolean;
  obsolete: boolean;
  notes: string;
  boxId: number | null;
}

export interface InventoryItemBox {
  id: number;
  code: string;
  name: string;
  path: string;
  locationName: string | null;
  locationSourceLabel: string | null;
  containerTypeLabel: string;
  hierarchy: InventoryHierarchyNode[];
}

export interface PhotoReturnToInboxResponse<TDetail> {
  detail: TDetail;
  inboxId: number | null;
}

export interface InventoryBoxDetail {
  id: number;
  code: string;
  name: string;
  containerType: string;
  containerTypeLabel: string;
  status: string;
  description: string | null;
  path: string;
  locationId: number;
  locationName: string | null;
  locationSourceLabel: string | null;
  hierarchy: InventoryHierarchyNode[];
  parent: InventoryBoxLink | null;
  children: InventoryBoxLink[];
  items: InventoryItem[];
  legacyUrl: string;
  photos: InventoryPhoto[];
  createdAt: string;
  updatedAt: string;
  actions: InventoryAction[];
  comments: InventoryAction[];
}

export interface InventoryActionCreate {
  title: string;
  description: string;
  priority: number;
}

export interface InventoryCommentCreate {
  text: string;
}

export interface InventoryBoxUpdate {
  code: string;
  name: string;
  containerType: string;
  description: string;
  locationId: number;
  parentBoxId: number | null;
  status: string;
}

export interface InventoryBoxLink {
  id: number;
  code: string;
  name: string;
}

export interface InventoryPhoto {
  id: number;
  url: string;
  previewUrl: string;
  fullUrl: string;
  rotationDegrees: number;
  caption: string | null;
  createdAt: string;
}

export interface InventoryHierarchyNode {
  label: string;
  sublabel: string | null;
  icon: string;
  tone: 'location' | 'box' | 'item' | 'current' | 'muted';
  routerLink: string | null;
  coverUrl: string | null;
  rotationDegrees: number;
}

export type PhotoInboxStatus = 'Pending' | 'Assigned' | 'Discarded' | 'All';

export interface PhotoInboxResponse {
  currentStatus: PhotoInboxStatus;
  pendingCount: number;
  assignedCount: number;
  discardedCount: number;
  photos: PhotoInboxItem[];
}

export interface PhotoInboxUploadResponse {
  imported: number;
  rejected: string[];
  inbox: PhotoInboxResponse;
}

export interface PhotoInboxItem {
  id: number;
  url: string;
  rotationDegrees: number;
  originalFilename: string;
  status: Exclude<PhotoInboxStatus, 'All'>;
  importedAt: string;
  processedAt: string | null;
  sourceBox: InventoryBoxLink | null;
  notes: string | null;
  legacyReviewUrl: string;
}

export interface PhotoReviewResponse {
  pendingCount: number;
  processedCount: number;
  previousId: number | null;
  nextId: number | null;
  current: PhotoReviewPhoto | null;
  pending: PhotoReviewPhoto[];
}

export interface PhotoReviewMutationResponse {
  review: PhotoReviewResponse;
  affectedIds: number[];
}

export interface PhotoReviewPhoto {
  id: number;
  thumbUrl: string;
  previewUrl: string;
  fullUrl: string;
  rotationDegrees: number;
  originalFilename: string;
  importedAt: string;
  sourceBox: InventoryBoxLink | null;
  notes: string | null;
}

export interface PhotoReviewCreateItem {
  ids: number[];
  boxId: number | null;
  name: string;
  notes: string;
  quantity: number;
  unit: string;
  tagIds: number[];
}

export interface CsvInventoryRow {
  location: string;
  boxCode: string;
  boxName: string;
  itemName: string;
  category: string;
  quantity: number;
  unit: string;
  consumable: boolean;
  minQuantity: number | null;
  condition: string;
  retention: string;
  sentimental: boolean;
  obsolete: boolean;
  notes: string;
}

export interface CsvPreviewResponse {
  key: string;
  rows: CsvInventoryRow[];
  count: number;
}

export interface CsvImportResponse {
  imported: number;
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

    for (const tagId of state.tagIds) {
      params = params.append('tagIds', String(tagId));
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

  fetchLocations(): Observable<LocationsResponse> {
    return this.http.get<LocationsResponse>('/api/locations');
  }

  createLocation(input: LocationUpdate): Observable<InventoryLocation> {
    return this.http.post<InventoryLocation>('/api/locations', input);
  }

  updateLocation(id: number, input: LocationUpdate): Observable<InventoryLocation> {
    return this.http.put<InventoryLocation>(`/api/locations/${id}`, input);
  }

  archiveLocation(id: number): Observable<LocationArchiveResponse> {
    return this.http.delete<LocationArchiveResponse>(`/api/locations/${id}`);
  }

  fetchTags(): Observable<TagsResponse> {
    return this.http.get<TagsResponse>('/api/tags');
  }

  createTag(input: TagUpdate): Observable<InventoryTag> {
    return this.http.post<InventoryTag>('/api/tags', input);
  }

  updateTag(id: number, input: TagUpdate): Observable<InventoryTag> {
    return this.http.put<InventoryTag>(`/api/tags/${id}`, input);
  }

  deleteTag(id: number): Observable<void> {
    return this.http.delete<void>(`/api/tags/${id}`);
  }

  fetchConditions(): Observable<ConditionsResponse> {
    return this.http.get<ConditionsResponse>('/api/item-conditions');
  }

  createCondition(input: TagUpdate): Observable<InventoryCondition> {
    return this.http.post<InventoryCondition>('/api/item-conditions', input);
  }

  updateCondition(id: number, input: TagUpdate): Observable<InventoryCondition> {
    return this.http.put<InventoryCondition>(`/api/item-conditions/${id}`, input);
  }

  deleteCondition(id: number): Observable<void> {
    return this.http.delete<void>(`/api/item-conditions/${id}`);
  }

  fetchDashboard(): Observable<DashboardResponse> {
    return this.http.get<DashboardResponse>('/api/dashboard');
  }

  fetchActions(): Observable<InventoryActionsResponse> {
    return this.http.get<InventoryActionsResponse>('/api/actions');
  }

  createAction(input: InventoryActionCreate): Observable<InventoryAction> {
    return this.http.post<InventoryAction>('/api/actions', input);
  }

  completeAction(id: number): Observable<InventoryAction> {
    return this.http.post<InventoryAction>(`/api/actions/${id}/complete`, {});
  }

  reopenAction(id: number): Observable<InventoryAction> {
    return this.http.post<InventoryAction>(`/api/actions/${id}/reopen`, {});
  }

  createItemAction(id: number, input: InventoryActionCreate): Observable<InventoryAction> {
    return this.http.post<InventoryAction>(`/api/items/${id}/actions`, input);
  }

  createItemComment(id: number, input: InventoryCommentCreate): Observable<InventoryAction> {
    return this.http.post<InventoryAction>(`/api/items/${id}/comments`, input);
  }

  createBoxAction(id: number, input: InventoryActionCreate): Observable<InventoryAction> {
    return this.http.post<InventoryAction>(`/api/boxes/${id}/actions`, input);
  }

  createBoxComment(id: number, input: InventoryCommentCreate): Observable<InventoryAction> {
    return this.http.post<InventoryAction>(`/api/boxes/${id}/comments`, input);
  }

  fetchItem(id: number): Observable<InventoryItemDetail> {
    return this.http.get<InventoryItemDetail>(`/api/items/${id}`);
  }

  updateItem(id: number, input: InventoryItemUpdate): Observable<InventoryItemDetail> {
    return this.http.put<InventoryItemDetail>(`/api/items/${id}`, input);
  }

  setItemCoverPhoto(id: number, photoId: number): Observable<InventoryItemDetail> {
    return this.http.post<InventoryItemDetail>(`/api/items/${id}/photos/${photoId}/cover`, {});
  }

  rotateItemPhoto(id: number, photoId: number, delta: number): Observable<InventoryItemDetail> {
    return this.http.post<InventoryItemDetail>(`/api/items/${id}/photos/${photoId}/rotate`, { delta });
  }

  archiveItemPhoto(id: number, photoId: number): Observable<InventoryItemDetail> {
    return this.http.post<InventoryItemDetail>(`/api/items/${id}/photos/${photoId}/archive`, {});
  }

  returnItemPhotoToInbox(id: number, photoId: number): Observable<PhotoReturnToInboxResponse<InventoryItemDetail>> {
    return this.http.post<PhotoReturnToInboxResponse<InventoryItemDetail>>(`/api/items/${id}/photos/${photoId}/return-to-inbox`, {});
  }

  itemPhotosDownloadUrl(id: number): string {
    return `/api/items/${id}/photos/download`;
  }

  fetchBox(code: string): Observable<InventoryBoxDetail> {
    return this.http.get<InventoryBoxDetail>(`/api/boxes/${encodeURIComponent(code)}`);
  }

  updateBox(id: number, input: InventoryBoxUpdate): Observable<InventoryBoxDetail> {
    return this.http.put<InventoryBoxDetail>(`/api/boxes/${id}`, input);
  }

  setBoxCoverPhoto(id: number, photoId: number): Observable<InventoryBoxDetail> {
    return this.http.post<InventoryBoxDetail>(`/api/boxes/${id}/photos/${photoId}/cover`, {});
  }

  rotateBoxPhoto(id: number, photoId: number, delta: number): Observable<InventoryBoxDetail> {
    return this.http.post<InventoryBoxDetail>(`/api/boxes/${id}/photos/${photoId}/rotate`, { delta });
  }

  archiveBoxPhoto(id: number, photoId: number): Observable<InventoryBoxDetail> {
    return this.http.post<InventoryBoxDetail>(`/api/boxes/${id}/photos/${photoId}/archive`, {});
  }

  returnBoxPhotoToInbox(id: number, photoId: number): Observable<PhotoReturnToInboxResponse<InventoryBoxDetail>> {
    return this.http.post<PhotoReturnToInboxResponse<InventoryBoxDetail>>(`/api/boxes/${id}/photos/${photoId}/return-to-inbox`, {});
  }

  fetchPhotoInbox(status: PhotoInboxStatus): Observable<PhotoInboxResponse> {
    const params = new HttpParams().set('status', status);
    return this.http.get<PhotoInboxResponse>('/api/photos/inbox', { params });
  }

  discardInboxPhoto(id: number): Observable<PhotoInboxItem> {
    return this.http.post<PhotoInboxItem>(`/api/photos/inbox/${id}/discard`, {});
  }

  restoreInboxPhoto(id: number): Observable<PhotoInboxItem> {
    return this.http.post<PhotoInboxItem>(`/api/photos/inbox/${id}/pending`, {});
  }

  uploadInboxPhotos(files: File[], sourceBoxId: number | null): Observable<HttpEvent<PhotoInboxUploadResponse>> {
    const body = new FormData();
    for (const file of files) {
      body.append('files', file, file.name);
    }
    if (sourceBoxId !== null) {
      body.append('sourceBoxId', String(sourceBoxId));
    }

    return this.http.post<PhotoInboxUploadResponse>('/api/photos/inbox/upload', body, {
      observe: 'events',
      reportProgress: true
    });
  }

  uploadItemPhotos(id: number, files: File[], caption: string): Observable<InventoryItemDetail> {
    const body = new FormData();
    for (const file of files) {
      body.append('files', file, file.name);
    }
    if (caption.trim()) {
      body.append('caption', caption.trim());
    }

    return this.http.post<InventoryItemDetail>(`/api/items/${id}/photos/upload`, body);
  }

  uploadBoxPhotos(id: number, files: File[], caption: string): Observable<InventoryBoxDetail> {
    const body = new FormData();
    for (const file of files) {
      body.append('files', file, file.name);
    }
    if (caption.trim()) {
      body.append('caption', caption.trim());
    }

    return this.http.post<InventoryBoxDetail>(`/api/boxes/${id}/photos/upload`, body);
  }

  fetchPhotoReview(id: number | null): Observable<PhotoReviewResponse> {
    let params = new HttpParams();
    if (id !== null) {
      params = params.set('id', String(id));
    }

    return this.http.get<PhotoReviewResponse>('/api/photos/review', { params });
  }

  rotateReviewPhotos(id: number, ids: number[], delta: number): Observable<PhotoReviewMutationResponse> {
    return this.http.post<PhotoReviewMutationResponse>(`/api/photos/review/${id}/rotate`, { ids, delta });
  }

  discardReviewPhotos(id: number, ids: number[]): Observable<PhotoReviewMutationResponse> {
    return this.http.post<PhotoReviewMutationResponse>(`/api/photos/review/${id}/discard`, { ids, delta: 0 });
  }

  assignReviewPhotosToBox(id: number, ids: number[], boxId: number): Observable<PhotoReviewMutationResponse> {
    return this.http.post<PhotoReviewMutationResponse>(`/api/photos/review/${id}/assign-box`, { ids, boxId });
  }

  assignReviewPhotosToItem(id: number, ids: number[], itemId: number): Observable<PhotoReviewMutationResponse> {
    return this.http.post<PhotoReviewMutationResponse>(`/api/photos/review/${id}/assign-item`, { ids, itemId });
  }

  createItemFromReviewPhotos(id: number, input: PhotoReviewCreateItem): Observable<PhotoReviewMutationResponse> {
    return this.http.post<PhotoReviewMutationResponse>(`/api/photos/review/${id}/create-item`, input);
  }

  undoReviewPhotos(ids: number[]): Observable<PhotoReviewResponse> {
    return this.http.post<PhotoReviewResponse>('/api/photos/review/undo', { ids });
  }

  previewCsvImport(file: File): Observable<CsvPreviewResponse> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<CsvPreviewResponse>('/api/csv/preview', body);
  }

  confirmCsvImport(key: string): Observable<CsvImportResponse> {
    return this.http.post<CsvImportResponse>('/api/csv/confirm', { key });
  }
}
