import { CommonModule } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ItemPropertyField, ItemPropertyValue } from './inventory-api.service';

@Component({
  selector: 'app-item-property-fields',
  standalone: true,
  imports: [CommonModule, FormsModule],
  styleUrl: './item-property-fields.component.scss',
  template: `
    @if (fields().length) {
      <section class="property-fields">
        <header>
          <div>
            <p class="eyebrow">Propiedades específicas</p>
            <h3>{{ readonlyMode() ? 'Datos de clase y subtipo' : 'Campos de clase y subtipo' }}</h3>
          </div>
        </header>
        <div class="property-grid">
          @for (field of fields(); track field.definition.id) {
            <label class="property-field" [class.required]="field.definition.isRequired">
              <span>{{ field.definition.name }} @if (field.definition.unit) { <small>({{ field.definition.unit }})</small> }</span>
              @if (readonlyMode()) {
                <strong>{{ displayValue(field) }}</strong>
              } @else {
                @switch (field.definition.dataType) {
                  @case ('LongText') { <textarea rows="3" [placeholder]="field.definition.placeholder || ''" [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event)"></textarea> }
                  @case ('Integer') { <input type="number" step="1" [min]="field.definition.minNumber ?? null" [max]="field.definition.maxNumber ?? null" [placeholder]="field.definition.placeholder || ''" [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event === '' ? null : +$event)" /> }
                  @case ('Decimal') { <input type="number" step="any" [min]="field.definition.minNumber ?? null" [max]="field.definition.maxNumber ?? null" [placeholder]="field.definition.placeholder || ''" [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event === '' ? null : +$event)" /> }
                  @case ('Boolean') { <select [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event === '' ? null : $event === 'true')"><option value="">Sin indicar</option><option value="true">Sí</option><option value="false">No</option></select> }
                  @case ('Date') { <input type="date" [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event)" /> }
                  @case ('Url') { <input type="url" [placeholder]="field.definition.placeholder || 'https://...'" [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event)" /> }
                  @case ('Select') { <select [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event)"><option value="">Sin indicar</option>@for (option of field.definition.options; track option.value) { @if (option.isActive) { <option [value]="option.value">{{ option.label }}</option> } }</select> }
                  @case ('MultiSelect') { <select multiple [ngModel]="multiValueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event)">@for (option of field.definition.options; track option.value) { @if (option.isActive) { <option [value]="option.value">{{ option.label }}</option> } }</select> }
                  @default { <input type="text" [placeholder]="field.definition.placeholder || ''" [ngModel]="valueFor(field.definition.id)" (ngModelChange)="setValue(field.definition.id, $event)" /> }
                }
                @if (field.definition.helpText) { <small class="help">{{ field.definition.helpText }}</small> }
              }
            </label>
          }
        </div>
      </section>
    }
  `
})
export class ItemPropertyFieldsComponent {
  readonly fields = input<ItemPropertyField[]>([]);
  readonly values = input<ItemPropertyValue[]>([]);
  readonly readonlyMode = input(false, { alias: 'readonly' });
  readonly valuesChange = output<ItemPropertyValue[]>();

  protected readonly valueMap = computed(() => new Map(this.values().map((entry) => [entry.definitionId, entry.value])));

  protected valueFor(definitionId: number): unknown {
    return this.valueMap().get(definitionId) ?? null;
  }

  protected multiValueFor(definitionId: number): string[] {
    const value = this.valueFor(definitionId);
    return Array.isArray(value) ? value.map(String) : [];
  }

  protected setValue(definitionId: number, value: unknown): void {
    const next = this.values().filter((entry) => entry.definitionId !== definitionId);
    next.push({ definitionId, value });
    this.valuesChange.emit(next);
  }

  protected displayValue(field: ItemPropertyField): string {
    const value = field.value?.value;
    if (value === null || value === undefined || value === '') return 'Sin indicar';
    if (Array.isArray(value)) {
      const labels = new Map(field.definition.options.map((option) => [option.value, option.label]));
      return value.map((entry) => labels.get(String(entry)) ?? String(entry)).join(', ');
    }
    if (typeof value === 'boolean') return value ? 'Sí' : 'No';
    if (field.definition.dataType === 'Select') {
      return field.definition.options.find((option) => option.value === value)?.label ?? String(value);
    }
    return String(value);
  }
}
