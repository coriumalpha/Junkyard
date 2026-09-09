import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, finalize, forkJoin, tap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { ColorPickerComponent } from './color-picker.component';
import { InventoryApiService, InventoryMode, ItemClass, ItemClassUpdate, ItemPropertyDataType, ItemPropertyDefinition, ItemPropertyDefinitionUpdate, ItemSubtype, ItemSubtypeUpdate } from './inventory-api.service';

interface ClassDraft {
  name: string;
  inventoryMode: InventoryMode;
  description: string;
  color: string | null;
  icon: string;
  sortOrder: number | null;
  isActive: boolean;
}

interface SubtypeDraft {
  itemClassId: number | null;
  name: string;
  unit: string;
  minStock: number | null;
  targetStock: number | null;
  description: string;
  sortOrder: number | null;
  isActive: boolean;
}

interface PropertyDraft {
  scope: 'Class' | 'Subtype';
  itemSubtypeId: number | null;
  key: string;
  name: string;
  dataType: ItemPropertyDataType;
  sortOrder: number;
  isActive: boolean;
  isRequired: boolean;
  unit: string;
  placeholder: string;
  helpText: string;
  minNumber: number | null;
  maxNumber: number | null;
  defaultValueJson: string;
  optionsText: string;
}

@Component({
  selector: 'app-settings-classes-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSelectModule, MatSlideToggleModule, ColorPickerComponent],
  templateUrl: './settings-classes-page.component.html',
  styleUrl: './settings-classes-page.component.scss'
})
export class SettingsClassesPageComponent {
  protected readonly classes = signal<ItemClass[]>([]);
  protected readonly subtypes = signal<ItemSubtype[]>([]);
  protected readonly selectedClassId = signal<number | null>(null);
  protected readonly editingClassId = signal<number | null>(null);
  protected readonly editingSubtypeId = signal<number | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly classDraft = signal<ClassDraft>(this.emptyClassDraft());
  protected readonly subtypeDraft = signal<SubtypeDraft>(this.emptySubtypeDraft(null));
  protected readonly propertyDefinitions = signal<ItemPropertyDefinition[]>([]);
  protected readonly propertyDraft = signal<PropertyDraft>(this.emptyPropertyDraft('Class'));
  protected readonly editingPropertyId = signal<number | null>(null);
  protected readonly propertyEditorOpen = signal(false);
  protected readonly colorEditorOpen = signal(false);
  protected readonly subtypeEditorOpen = signal(false);
  protected readonly inventoryModes: InventoryMode[] = ['Individual', 'Fungible', 'Kit', 'Lot'];
  protected readonly propertyTypes: ItemPropertyDataType[] = ['ShortText', 'LongText', 'Integer', 'Decimal', 'Boolean', 'Date', 'Url', 'Select', 'MultiSelect'];

  protected readonly selectedClass = computed(() =>
    this.classes().find((itemClass) => itemClass.id === this.selectedClassId()) ?? null);
  protected readonly selectedSubtypes = computed(() =>
    this.subtypes().filter((subtype) => subtype.itemClassId === this.selectedClassId()));
  protected readonly classProperties = computed(() => this.propertyDefinitions().filter((definition) => definition.scope === 'Class'));
  protected readonly subtypeProperties = computed(() => this.propertyDefinitions().filter((definition) => definition.scope === 'Subtype'));

  private readonly api = inject(InventoryApiService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      classes: this.api.fetchItemClasses(true),
      subtypes: this.api.fetchItemSubtypes(0, true)
    }).pipe(
      tap(({ classes, subtypes }) => {
        this.classes.set(classes.itemClasses);
        this.subtypes.set(subtypes.itemSubtypes);
        const currentId = this.selectedClassId();
        const nextClass = classes.itemClasses.find((itemClass) => itemClass.id === currentId) ?? classes.itemClasses[0] ?? null;
        if (nextClass) {
          this.editClass(nextClass);
        } else {
          this.startCreateClass();
        }
      }),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudieron cargar clases y subtipos.'));
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  protected selectClass(itemClass: ItemClass): void {
    this.editClass(itemClass);
  }

  protected startCreateClass(): void {
    this.selectedClassId.set(null);
    this.editingClassId.set(null);
    this.editingSubtypeId.set(null);
    this.subtypeEditorOpen.set(false);
    this.colorEditorOpen.set(false);
    this.classDraft.set(this.emptyClassDraft());
    this.subtypeDraft.set(this.emptySubtypeDraft(null));
    this.propertyDefinitions.set([]);
    this.propertyEditorOpen.set(false);
    this.error.set(null);
    this.message.set(null);
  }

  protected editClass(itemClass: ItemClass): void {
    this.selectedClassId.set(itemClass.id);
    this.editingClassId.set(itemClass.id);
    this.editingSubtypeId.set(null);
    this.subtypeEditorOpen.set(false);
    this.colorEditorOpen.set(false);
    this.classDraft.set({
      name: itemClass.name,
      inventoryMode: itemClass.inventoryMode,
      description: itemClass.description ?? '',
      color: itemClass.color,
      icon: itemClass.icon ?? '',
      sortOrder: itemClass.sortOrder,
      isActive: itemClass.isActive
    });
    this.error.set(null);
    this.loadProperties(itemClass.id);
  }

  protected saveClass(): void {
    const draft = this.classDraft();
    const name = draft.name.trim();
    if (!name || this.saving()) {
      return;
    }

    const duplicate = this.classes().some((itemClass) =>
      itemClass.id !== this.editingClassId()
      && itemClass.name.trim().toLocaleLowerCase('es') === name.toLocaleLowerCase('es'));
    if (duplicate) {
      this.error.set('Ya existe una clase con ese nombre.');
      return;
    }

    const input: ItemClassUpdate = { ...draft, name, description: draft.description.trim(), icon: draft.icon.trim() };
    const request = this.editingClassId()
      ? this.api.updateItemClass(this.editingClassId()!, input)
      : this.api.createItemClass(input);
    this.persistClass(request, this.editingClassId() ? 'Clase actualizada.' : 'Clase creada.');
  }

  protected toggleClass(itemClass: ItemClass): void {
    if (this.saving()) {
      return;
    }

    this.persistClass(this.api.setItemClassActive(itemClass.id, !itemClass.isActive), !itemClass.isActive ? 'Clase activada.' : 'Clase desactivada. Los ítems existentes conservan la referencia.');
  }

  protected startCreateSubtype(): void {
    if (!this.selectedClassId()) {
      this.error.set('Selecciona una clase antes de crear un subtipo.');
      return;
    }
    this.editingSubtypeId.set(null);
    this.subtypeDraft.set(this.emptySubtypeDraft(this.selectedClassId()));
    this.subtypeEditorOpen.set(true);
    this.message.set(null);
  }

  protected cancelSubtypeEdit(): void {
    this.editingSubtypeId.set(null);
    this.subtypeDraft.set(this.emptySubtypeDraft(this.selectedClassId()));
    this.subtypeEditorOpen.set(false);
    this.error.set(null);
  }

  protected editSubtype(subtype: ItemSubtype): void {
    this.selectedClassId.set(subtype.itemClassId);
    this.editingSubtypeId.set(subtype.id);
    this.subtypeEditorOpen.set(true);
    this.subtypeDraft.set({
      itemClassId: subtype.itemClassId,
      name: subtype.name,
      unit: subtype.unit ?? '',
      minStock: subtype.minStock,
      targetStock: subtype.targetStock,
      description: subtype.description ?? '',
      sortOrder: subtype.sortOrder,
      isActive: subtype.isActive
    });
    this.error.set(null);
  }

  protected saveSubtype(): void {
    const draft = this.subtypeDraft();
    const itemClassId = draft.itemClassId ?? this.selectedClassId();
    const name = draft.name.trim();
    if (!itemClassId || !name || this.saving()) {
      return;
    }

    const duplicate = this.subtypes().some((subtype) =>
      subtype.itemClassId === itemClassId
      && subtype.id !== this.editingSubtypeId()
      && subtype.name.trim().toLocaleLowerCase('es') === name.toLocaleLowerCase('es'));
    if (duplicate) {
      this.error.set('Ya existe un subtipo con ese nombre dentro de la clase.');
      return;
    }

    const input: ItemSubtypeUpdate = {
      ...draft,
      itemClassId,
      name,
      unit: draft.unit.trim(),
      description: draft.description.trim()
    };
    const request = this.editingSubtypeId()
      ? this.api.updateItemSubtype(this.editingSubtypeId()!, input)
      : this.api.createItemSubtype(input);
    this.persistSubtype(request, this.editingSubtypeId() ? 'Subtipo actualizado.' : 'Subtipo creado.');
  }

  protected toggleSubtype(subtype: ItemSubtype): void {
    if (this.saving()) {
      return;
    }

    this.persistSubtype(this.api.setItemSubtypeActive(subtype.id, !subtype.isActive), !subtype.isActive ? 'Subtipo activado.' : 'Subtipo desactivado. Los ítems existentes conservan la referencia.');
  }

  protected modeLabel(mode: InventoryMode): string {
    return mode === 'Lot' ? 'Lote' : mode;
  }

  protected modeHelp(mode: InventoryMode): string {
    switch (mode) {
      case 'Individual':
        return 'Ítem trazable uno a uno.';
      case 'Fungible':
        return 'Stock agregable por subtipo.';
      case 'Kit':
        return 'Conjunto funcional.';
      case 'Lot':
        return 'Lote agrupado.';
    }
  }

  protected classUsageHint(itemClass: ItemClass): string {
    const count = this.subtypes().filter((subtype) => subtype.itemClassId === itemClass.id).length;
    return `${count} subtipos · ${itemClass.itemCount} ítems`;
  }

  protected patchClassDraft(patch: Partial<ClassDraft>): void {
    this.classDraft.update((draft) => ({ ...draft, ...patch }));
  }

  protected patchSubtypeDraft(patch: Partial<SubtypeDraft>): void {
    this.subtypeDraft.update((draft) => ({ ...draft, ...patch }));
  }

  protected patchPropertyDraft(patch: Partial<PropertyDraft>): void {
    this.propertyDraft.update((draft) => ({ ...draft, ...patch }));
  }

  protected startCreateProperty(scope: 'Class' | 'Subtype', subtypeId: number | null = null): void {
    this.editingPropertyId.set(null);
    this.propertyDraft.set(this.emptyPropertyDraft(scope, subtypeId));
    this.propertyEditorOpen.set(true);
  }

  protected editProperty(definition: ItemPropertyDefinition): void {
    this.editingPropertyId.set(definition.id);
    this.propertyDraft.set({
      scope: definition.scope,
      itemSubtypeId: definition.itemSubtypeId,
      key: definition.key,
      name: definition.name,
      dataType: definition.dataType,
      sortOrder: definition.sortOrder,
      isActive: definition.isActive,
      isRequired: definition.isRequired,
      unit: definition.unit ?? '',
      placeholder: definition.placeholder ?? '',
      helpText: definition.helpText ?? '',
      minNumber: definition.minNumber,
      maxNumber: definition.maxNumber,
      defaultValueJson: definition.defaultValueJson ?? '',
      optionsText: definition.options.map((option) => `${option.value}|${option.label}`).join('\n')
    });
    this.propertyEditorOpen.set(true);
  }

  protected saveProperty(): void {
    const draft = this.propertyDraft();
    if (!this.selectedClassId() || !draft.name.trim() || this.saving()) return;
    const input: ItemPropertyDefinitionUpdate = {
      scope: draft.scope,
      itemClassId: draft.scope === 'Class' ? this.selectedClassId() : null,
      itemSubtypeId: draft.scope === 'Subtype' ? draft.itemSubtypeId : null,
      key: draft.key.trim(),
      name: draft.name.trim(),
      dataType: draft.dataType,
      sortOrder: draft.sortOrder,
      isActive: draft.isActive,
      isRequired: draft.isRequired,
      unit: draft.unit.trim(),
      placeholder: draft.placeholder.trim(),
      helpText: draft.helpText.trim(),
      minNumber: draft.minNumber,
      maxNumber: draft.maxNumber,
      defaultValueJson: draft.defaultValueJson.trim(),
      options: this.parseOptions(draft.optionsText)
    };
    const request = this.editingPropertyId()
      ? this.api.updateItemProperty(this.editingPropertyId()!, input)
      : this.api.createItemProperty(input);
    this.persistProperty(request, this.editingPropertyId() ? 'Propiedad actualizada.' : 'Propiedad creada.');
  }

  protected toggleProperty(definition: ItemPropertyDefinition): void {
    this.persistProperty(this.api.setItemPropertyActive(definition.id, !definition.isActive), !definition.isActive ? 'Propiedad activada.' : 'Propiedad desactivada.');
  }

  protected propertyTypeLabel(type: ItemPropertyDataType): string {
    return ({ ShortText: 'Texto corto', LongText: 'Texto largo', Integer: 'Entero', Decimal: 'Decimal', Boolean: 'Sí/No', Date: 'Fecha', Url: 'URL', Select: 'Select', MultiSelect: 'Multi-select' } as Record<ItemPropertyDataType, string>)[type];
  }

  protected propertiesForSubtype(subtypeId: number): ItemPropertyDefinition[] {
    return this.subtypeProperties().filter((property) => property.itemSubtypeId === subtypeId);
  }

  private persistClass(request: ReturnType<InventoryApiService['createItemClass']>, success: string): void {
    this.saving.set(true);
    this.error.set(null);
    request.pipe(
      tap((itemClass) => {
        this.classes.update((current) => this.sortClasses([...current.filter((row) => row.id !== itemClass.id), itemClass]));
        this.selectedClassId.set(itemClass.id);
        this.editingClassId.set(itemClass.id);
        this.colorEditorOpen.set(false);
        this.message.set(success);
        this.loadProperties(itemClass.id);
      }),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo guardar la clase.'));
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private persistSubtype(request: ReturnType<InventoryApiService['createItemSubtype']>, success: string): void {
    this.saving.set(true);
    this.error.set(null);
    request.pipe(
      tap((subtype) => {
        this.subtypes.update((current) => this.sortSubtypes([...current.filter((row) => row.id !== subtype.id), subtype]));
        this.selectedClassId.set(subtype.itemClassId);
        this.editingSubtypeId.set(subtype.id);
        this.subtypeEditorOpen.set(false);
        this.message.set(success);
        this.loadProperties(subtype.itemClassId);
      }),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo guardar el subtipo.'));
        this.reload();
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private emptyClassDraft(): ClassDraft {
    return { name: '', inventoryMode: 'Individual', description: '', color: '#48ffb0', icon: 'category', sortOrder: null, isActive: true };
  }

  private emptySubtypeDraft(itemClassId: number | null): SubtypeDraft {
    return { itemClassId, name: '', unit: 'uds', minStock: null, targetStock: null, description: '', sortOrder: null, isActive: true };
  }

  private emptyPropertyDraft(scope: 'Class' | 'Subtype', subtypeId: number | null = null): PropertyDraft {
    return {
      scope,
      itemSubtypeId: subtypeId,
      key: '',
      name: '',
      dataType: 'ShortText',
      sortOrder: 0,
      isActive: true,
      isRequired: false,
      unit: '',
      placeholder: '',
      helpText: '',
      minNumber: null,
      maxNumber: null,
      defaultValueJson: '',
      optionsText: ''
    };
  }

  private loadProperties(itemClassId: number | null): void {
    if (!itemClassId) {
      this.propertyDefinitions.set([]);
      return;
    }
    this.api.fetchItemProperties(itemClassId, null, true).pipe(
      tap((response) => this.propertyDefinitions.set(response.definitions)),
      catchError(() => {
        this.propertyDefinitions.set([]);
        return EMPTY;
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private persistProperty(request: ReturnType<InventoryApiService['createItemProperty']>, success: string): void {
    this.saving.set(true);
    this.error.set(null);
    request.pipe(
      tap(() => {
        this.propertyEditorOpen.set(false);
        this.editingPropertyId.set(null);
        this.message.set(success);
        this.loadProperties(this.selectedClassId());
      }),
      catchError((error: unknown) => {
        this.error.set(this.describeError(error, 'No se pudo guardar la propiedad.'));
        return EMPTY;
      }),
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private parseOptions(text: string) {
    return text.split('\n')
      .map((line, index) => {
        const [value, ...label] = line.split('|');
        return { value: value.trim(), label: (label.join('|') || value).trim(), sortOrder: index, isActive: true };
      })
      .filter((option) => option.value && option.label);
  }

  private sortClasses(classes: ItemClass[]): ItemClass[] {
    return classes.sort((left, right) => (left.sortOrder ?? 999999) - (right.sortOrder ?? 999999) || left.name.localeCompare(right.name, 'es'));
  }

  private sortSubtypes(subtypes: ItemSubtype[]): ItemSubtype[] {
    return subtypes.sort((left, right) => (left.sortOrder ?? 999999) - (right.sortOrder ?? 999999) || left.name.localeCompare(right.name, 'es'));
  }

  private describeError(error: unknown, fallback: string): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = (error as { error?: { error?: string } }).error;
      if (body?.error) {
        return body.error;
      }
    }

    return error instanceof Error ? error.message : fallback;
  }
}
