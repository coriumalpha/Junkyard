import {CommonModule} from '@angular/common';
import {Component, EventEmitter, Input, Output, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RouterLink} from '@angular/router';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {finalize} from 'rxjs';
import {InventoryApiService, InventoryItemDetail, InventoryOptionsResponse, ItemPlacementInput} from './inventory-api.service';
import {SearchableSelectComponent, SearchableSelectOption} from './searchable-select.component';

@Component({
  selector: 'app-item-placement', standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatIconModule, SearchableSelectComponent],
  templateUrl: './item-placement.component.html', styleUrl: './item-placement.component.scss'
})
export class ItemPlacementComponent {
  @Input({required:true}) item!: InventoryItemDetail;
  @Input({required:true}) options!: InventoryOptionsResponse;
  @Output() readonly updated = new EventEmitter<InventoryItemDetail>();
  @Output() readonly dirtyChange = new EventEmitter<boolean>();
  @Output() readonly editingChange = new EventEmitter<boolean>();
  private readonly api = inject(InventoryApiService);
  editing = false;
  confirmingReturn = false;
  busy = false;
  error = '';
  message = '';
  private baseline = '';
  form: ItemPlacementInput = this.emptyForm();
  get locationOptions(): SearchableSelectOption[] { return this.options.locations.map(l => ({value:l.id,label:l.name})); }
  get boxOptions(): SearchableSelectOption[] { return this.options.boxes.filter(b=>b.id!==this.item.box?.id).map(b=>({value:b.id,label:`${b.code} · ${b.name}`,hint:b.path})); }
  localDate(value: string | Date): string {
    const d = new Date(value); return new Date(d.getTime()-d.getTimezoneOffset()*60000).toISOString().slice(0,16);
  }
  private emptyForm(): ItemPlacementInput {
    return {destinationType:'text',destination:'',locationId:null,destinationBoxId:null,notes:'',expectedReturnDate:null,startedAt:this.localDate(new Date()),version:null};
  }
  open(): void {
    const p = this.item.currentPlacement;
    this.form = p ? {...p,notes:p.notes||'',startedAt:this.localDate(p.startedAt)} : this.emptyForm();
    this.baseline = JSON.stringify(this.form); this.editing = true; this.error = ''; this.message = '';
    this.editingChange.emit(true); this.dirtyChange.emit(false);
  }
  changed(): void { this.dirtyChange.emit(JSON.stringify(this.form)!==this.baseline); }
  cancel(): void { if(this.busy)return; this.editing=false; this.error=''; this.editingChange.emit(false); this.dirtyChange.emit(false); }
  save(): void {
    if(this.busy)return;
    const started = new Date(this.form.startedAt);
    if(Number.isNaN(started.getTime())) {this.error='Indica una fecha de salida válida.';return;}
    const input: ItemPlacementInput={...this.form,startedAt:started.toISOString(),expectedReturnDate:this.form.expectedReturnDate||null};
    const active = this.item.currentPlacement;
    const request = active ? this.api.updatePlacement(this.item.id,active.id,input) : this.api.startPlacement(this.item.id,input);
    this.busy=true;this.error='';
    request.pipe(finalize(()=>this.busy=false)).subscribe({
      next:item=>{this.updated.emit(item);this.editing=false;this.dirtyChange.emit(false);this.editingChange.emit(false);this.message=active?'Desplazamiento actualizado.':'Salida registrada. La pertenencia no ha cambiado.';},
      error:e=>this.error=e.error?.error||'No se pudo guardar el desplazamiento. Revisa los datos o recarga la ficha.'
    });
  }
  returnItem(): void {
    const p=this.item.currentPlacement;if(!p||this.busy)return;
    this.busy=true;this.error='';
    this.api.returnPlacement(this.item.id,p.id,p.version).pipe(finalize(()=>this.busy=false)).subscribe({
      next:item=>{this.updated.emit(item);this.confirmingReturn=false;this.message='Devolución registrada. Se recupera la ubicación de pertenencia.';},
      error:e=>this.error=e.error?.error||'No se pudo registrar la devolución.'
    });
  }
  overdue(value: string | null): boolean {return !!value && value<this.localDate(new Date()).slice(0,10);}
}
