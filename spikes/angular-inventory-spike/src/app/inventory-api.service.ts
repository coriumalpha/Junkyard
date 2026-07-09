import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export type InventoryViewMode = 'grouped' | 'flat';
export type InventoryLayoutMode = 'grouped' | 'flat' | 'gallery' | 'table' | 'containers';

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
  onlyUntagged: boolean;
  onlyQuarantined: boolean;
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
  onlyUntagged: boolean;
  onlyQuarantined: boolean;
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
  itemClasses: ItemClass[];
  itemSubtypes: ItemSubtype[];
  locations: InventoryOption[];
  boxes: InventoryBoxOption[];
}

export interface TagsResponse {
  tags: InventoryTag[];
}

export interface ItemClassesResponse {
  itemClasses: ItemClass[];
}

export interface ItemSubtypesResponse {
  itemSubtypes: ItemSubtype[];
}

export interface ItemClassificationCleanupReport {
  lotKitTemporaryCount: number;
  lotKitMigratedCount: number;
  lotKitPendingCount: number;
  lotKitPending: ItemClassificationCleanupItem[];
  quarantineCount: number;
  quarantineItems: ItemClassificationCleanupItem[];
  untypedConsumableCount: number;
  untypedConsumables: ItemClassificationCleanupItem[];
}

export interface ItemClassificationCleanupItem {
  id: number;
  code: string;
  name: string;
  quantityLabel: string;
  boxPath: string;
  tags: string[];
  itemClassName: string | null;
  itemSubtypeName: string | null;
  suggestion: string | null;
}

export interface InventoryTag {
  id: number;
  name: string;
  color: string;
  itemCount: number;
  isTemporary: boolean;
}

export interface ConditionsResponse {
  conditions: InventoryCondition[];
}

export interface InventoryCondition {
  id: number;
  name: string;
  color: string;
}

export type InventoryMode = 'Individual' | 'Fungible' | 'Kit' | 'Lot';

export interface ItemClass {
  id: number;
  name: string;
  inventoryMode: InventoryMode;
  description: string | null;
  color: string | null;
  icon: string | null;
  sortOrder: number | null;
  isActive: boolean;
  itemCount: number;
}

export interface ItemClassUpdate {
  name: string;
  inventoryMode: InventoryMode;
  description: string;
  color: string | null;
  icon: string;
  sortOrder: number | null;
  isActive: boolean;
}

export interface ItemSubtype {
  id: number;
  itemClassId: number;
  name: string;
  unit: string | null;
  minStock: number | null;
  targetStock: number | null;
  description: string | null;
  sortOrder: number | null;
  isActive: boolean;
  itemCount: number;
}

export interface ItemSubtypeUpdate {
  itemClassId: number;
  name: string;
  unit: string;
  minStock: number | null;
  targetStock: number | null;
  description: string;
  sortOrder: number | null;
  isActive: boolean;
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
  itemClassId: number | null;
  itemClassName: string | null;
  inventoryMode: InventoryMode | null;
  itemSubtypeId: number | null;
  itemSubtypeName: string | null;
  itemSubtypeUnit: string | null;
  itemSubtypeMinStock: number | null;
  itemSubtypeTargetStock: number | null;
  tags: InventoryTag[];
  quantity: number;
  unit: string;
  quantityLabel: string;
  generatedLabel: string | null;
  consumable: boolean;
  isQuarantined: boolean;
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
  classedItemCount: number;
  untypedConsumableCount: number;
  lotKitPendingCount: number;
  quarantinedCount: number;
  openActionCount: number;
  highPriorityActionCount: number;
  recentBoxes: DashboardBox[];
  lowStockItems: DashboardItem[];
  lowConsumableGroups: DashboardConsumableGroup[];
  recentPhotos: DashboardPhoto[];
  openActions: InventoryAction[];
  inventoryModeStats: DashboardMetric[];
  boxStatusStats: DashboardMetric[];
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

export interface DashboardConsumableGroup {
  className: string;
  subtypeName: string;
  color: string | null;
  icon: string | null;
  unit: string | null;
  totalQuantity: number;
  minStock: number | null;
  targetStock: number | null;
  itemCount: number;
  status: string;
}

export interface DashboardPhoto {
  id: number;
  url: string;
  rotationDegrees: number;
  caption: string | null;
  entityType: string;
  entityId: number;
}

export interface DashboardMetric {
  label: string;
  count: number;
  tone: string;
}

export interface ArchiveResponse {
  boxCount: number;
  itemCount: number;
  photoCount: number;
  boxes: ArchiveBox[];
  items: ArchiveItem[];
  photos: ArchivePhoto[];
}

export interface ArchiveBox {
  id: number;
  code: string;
  name: string;
  containerTypeLabel: string;
  locationName: string | null;
  archivedAt: string | null;
  coverUrl: string | null;
  rotationDegrees: number;
  legacyUrl: string;
}

export interface ArchiveItem {
  id: number;
  code: string;
  name: string;
  boxCode: string | null;
  boxName: string | null;
  tags: string[];
  archivedAt: string | null;
  coverUrl: string | null;
  rotationDegrees: number;
  legacyUrl: string;
}

export interface ArchivePhoto {
  id: number;
  entityType: string;
  entityId: number;
  caption: string | null;
  archivedAt: string | null;
  url: string;
  rotationDegrees: number;
}

export interface ArchiveEntityRequest {
  comment: string;
}

export interface ArchiveBoxRequest {
  comment: string;
  targetBoxId: number | null;
  orphanContents: boolean;
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
  itemClassId: number | null;
  itemClassName: string | null;
  inventoryMode: InventoryMode | null;
  itemSubtypeId: number | null;
  itemSubtypeName: string | null;
  itemSubtypeUnit: string | null;
  tags: InventoryTag[];
  quantityLabel: string;
  quantity: number;
  unit: string;
  minQuantity: number | null;
  condition: string | null;
  retention: string | null;
  notes: string | null;
  consumable: boolean;
  isQuarantined: boolean;
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
  itemClassId: number | null;
  itemSubtypeId: number | null;
  tagIds: number[];
  quantity: number;
  unit: string;
  minQuantity: number | null;
  condition: string;
  retention: string;
  consumable: boolean;
  isQuarantined: boolean;
  sentimental: boolean;
  obsolete: boolean;
  notes: string;
  boxId: number | null;
}

export interface InventoryBulkUpdate {
  itemIds: number[];
  moveToBoxId?: number | null;
  addTagId?: number | null;
  removeTagId?: number | null;
}

export interface InventoryBulkUpdateResponse {
  updatedCount: number;
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

export interface InventoryActionUpdate {
  title?: string;
  description?: string;
  text?: string;
  priority?: number;
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
  totalCount: number;
  page: number;
  pageSize: number;
  showAll: boolean;
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
  itemClassId?: number | null;
  itemSubtypeId?: number | null;
  name: string;
  notes: string;
  quantity: number;
  unit: string;
  isQuarantined: boolean;
  tagIds: number[];
}

export interface AiStatus {
  enabled: boolean;
  provider: string;
  model: string;
  cheapModel: string;
  imageDetail: 'low' | 'high' | 'auto';
  maxImagesPerRequest: number;
  defaultMode: 'normal' | 'cheap';
  hasApiKey: boolean;
  keySource: 'None' | 'Environment' | 'Stored' | 'EnvironmentOverridesStored';
  maskedApiKey: string | null;
  isUsable: boolean;
  reason: string | null;
  maxDescriptionLength: number;
  storeRawResponse: boolean;
  allowSuggestedNewTags: boolean;
}

export interface AiSettingsUpdate {
  enabled: boolean;
  provider: string;
  model: string;
  cheapModel: string;
  imageDetail: 'low' | 'high' | 'auto';
  maxImagesPerRequest: number;
  defaultMode: 'normal' | 'cheap';
}

export interface AiConnectionTestResponse {
  ok: boolean;
  provider: string;
  model: string;
  message: string | null;
  error: string | null;
}

export interface AiSuggestItemRequest {
  photoIds: number[];
  mode: 'cheap' | 'normal';
  detail: 'low' | 'high' | 'auto';
  userHint?: string;
}

export interface AiSuggestItemResponse {
  suggestionId: number | null;
  proposedName: string;
  proposedDescription: string;
  proposedQuantity: number | null;
  quantityConfidence: number;
  suggestedTags: AiSuggestedTag[];
  suggestedNewTags: string[];
  suggestedCategory: string | null;
  suggestedClass: AiSuggestedChoice | null;
  suggestedSubtype: AiSuggestedChoice | null;
  warnings: string[];
  model: string | null;
  imageDetail: string | null;
  userHint: string | null;
  estimatedCostInfo: string | null;
}

export interface AiSuggestedTag {
  tagId: number | null;
  tagName: string;
  confidence: number;
  reason: string;
}

export interface AiSuggestedChoice {
  id: number;
  name: string;
  confidence: number;
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

  if (state.onlyUntagged) {
      params = params.set('onlyUntagged', 'true');
    }

    if (state.onlyQuarantined) {
      params = params.set('onlyQuarantined', 'true');
    }

    params = params.set('view', state.view);

    return this.http.get<InventoryLiveResponse>('/api/inventory/live', { params });
  }

  fetchOptions(): Observable<InventoryOptionsResponse> {
    return this.http.get<InventoryOptionsResponse>('/api/inventory/options');
  }

  fetchItemClasses(includeInactive = false): Observable<ItemClassesResponse> {
    const params = includeInactive ? new HttpParams().set('includeInactive', 'true') : undefined;
    return this.http.get<ItemClassesResponse>('/api/item-classes', { params });
  }

  createItemClass(input: ItemClassUpdate): Observable<ItemClass> {
    return this.http.post<ItemClass>('/api/item-classes', input);
  }

  updateItemClass(id: number, input: ItemClassUpdate): Observable<ItemClass> {
    return this.http.put<ItemClass>(`/api/item-classes/${id}`, input);
  }

  setItemClassActive(id: number, isActive: boolean): Observable<ItemClass> {
    return this.http.patch<ItemClass>(`/api/item-classes/${id}/active`, { isActive });
  }

  fetchItemSubtypes(itemClassId: number, includeInactive = false): Observable<ItemSubtypesResponse> {
    let params = new HttpParams().set('itemClassId', String(itemClassId));
    if (includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.http.get<ItemSubtypesResponse>('/api/item-subtypes', { params });
  }

  createItemSubtype(input: ItemSubtypeUpdate): Observable<ItemSubtype> {
    return this.http.post<ItemSubtype>('/api/item-subtypes', input);
  }

  updateItemSubtype(id: number, input: ItemSubtypeUpdate): Observable<ItemSubtype> {
    return this.http.put<ItemSubtype>(`/api/item-subtypes/${id}`, input);
  }

  setItemSubtypeActive(id: number, isActive: boolean): Observable<ItemSubtype> {
    return this.http.patch<ItemSubtype>(`/api/item-subtypes/${id}/active`, { isActive });
  }

  fetchItemClassificationCleanup(): Observable<ItemClassificationCleanupReport> {
    return this.http.get<ItemClassificationCleanupReport>('/api/cleanup/item-classification');
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

  renameTag(id: number, name: string): Observable<InventoryTag> {
    return this.http.post<InventoryTag>(`/api/tags/${id}/rename`, { name });
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

  fetchArchive(): Observable<ArchiveResponse> {
    return this.http.get<ArchiveResponse>('/api/archive');
  }

  fetchActions(): Observable<InventoryActionsResponse> {
    return this.http.get<InventoryActionsResponse>('/api/actions');
  }

  createAction(input: InventoryActionCreate): Observable<InventoryAction> {
    return this.http.post<InventoryAction>('/api/actions', input);
  }

  updateAction(id: number, input: InventoryActionUpdate): Observable<InventoryAction> {
    return this.http.put<InventoryAction>(`/api/actions/${id}`, input);
  }

  updateComment(id: number, input: InventoryActionUpdate): Observable<InventoryAction> {
    return this.http.put<InventoryAction>(`/api/comments/${id}`, input);
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

  createItem(input: InventoryItemUpdate): Observable<InventoryItemDetail> {
    return this.http.post<InventoryItemDetail>('/api/items', input);
  }

  updateItem(id: number, input: InventoryItemUpdate): Observable<InventoryItemDetail> {
    return this.http.put<InventoryItemDetail>(`/api/items/${id}`, input);
  }

  archiveItem(id: number, input: ArchiveEntityRequest): Observable<InventoryItemDetail> {
    return this.http.post<InventoryItemDetail>(`/api/items/${id}/archive`, input);
  }

  restoreItem(id: number): Observable<InventoryItemDetail> {
    return this.http.post<InventoryItemDetail>(`/api/items/${id}/restore`, {});
  }

  bulkUpdateItems(input: InventoryBulkUpdate): Observable<InventoryBulkUpdateResponse> {
    return this.http.post<InventoryBulkUpdateResponse>('/api/items/bulk', input);
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

  createBox(input: InventoryBoxUpdate): Observable<InventoryBoxDetail> {
    return this.http.post<InventoryBoxDetail>('/api/boxes', input);
  }

  updateBox(id: number, input: InventoryBoxUpdate): Observable<InventoryBoxDetail> {
    return this.http.put<InventoryBoxDetail>(`/api/boxes/${id}`, input);
  }

  archiveBox(id: number, input: ArchiveBoxRequest): Observable<InventoryBoxDetail> {
    return this.http.post<InventoryBoxDetail>(`/api/boxes/${id}/archive`, input);
  }

  restoreBox(id: number): Observable<InventoryBoxDetail> {
    return this.http.post<InventoryBoxDetail>(`/api/boxes/${id}/restore`, {});
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

  fetchPhotoInbox(status: PhotoInboxStatus, page = 1, pageSize = 96, showAll = false): Observable<PhotoInboxResponse> {
    let params = new HttpParams()
      .set('status', status)
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (showAll) {
      params = params.set('all', 'true');
    }
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

  fetchAiStatus(): Observable<AiStatus> {
    return this.http.get<AiStatus>('/api/ai/settings/status');
  }

  updateAiSettings(input: AiSettingsUpdate): Observable<AiStatus> {
    return this.http.put<AiStatus>('/api/ai/settings', input);
  }

  saveAiApiKey(apiKey: string): Observable<AiStatus> {
    return this.http.post<AiStatus>('/api/ai/settings/api-key', { apiKey });
  }

  deleteAiApiKey(): Observable<AiStatus> {
    return this.http.delete<AiStatus>('/api/ai/settings/api-key');
  }

  testAiConnection(mode: 'normal' | 'cheap'): Observable<AiConnectionTestResponse> {
    return this.http.post<AiConnectionTestResponse>('/api/ai/settings/test', { mode });
  }

  suggestReviewItem(input: AiSuggestItemRequest): Observable<AiSuggestItemResponse> {
    return this.http.post<AiSuggestItemResponse>('/api/ai/photo-review/suggest-item', input);
  }

  acceptAiSuggestion(id: number): Observable<void> {
    return this.http.post<void>(`/api/ai/photo-review/suggestions/${id}/accept`, {});
  }

  rejectAiSuggestion(id: number): Observable<void> {
    return this.http.post<void>(`/api/ai/photo-review/suggestions/${id}/reject`, {});
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
