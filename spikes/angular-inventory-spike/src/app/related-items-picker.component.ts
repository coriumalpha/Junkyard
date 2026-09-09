import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InventoryApiService, RelatedItem } from './inventory-api.service';
import { InventoryCodePipe } from './inventory-code.pipe';
@Component({selector:'app-related-items-picker',standalone:true,imports:[FormsModule,InventoryCodePipe],
 template:`<section class="related-picker"><h3>Elementos relacionados</h3><p>Vincula accesorios, piezas o conjuntos. La relación se verá en ambas fichas; no cambia su contenedor.</p>
 <div class="chosen">@for (item of selected();track item.id) { <button type="button" (click)="toggle(item.id)" [attr.aria-label]="'Quitar relación con '+item.name">{{ item.code | inventoryCode }} · {{ item.name }} <span aria-hidden="true">×</span></button> }</div>
 <input type="search" aria-label="Buscar elementos relacionados" placeholder="Buscar por nombre o código…" [ngModel]="query()" (ngModelChange)="query.set($event)" />
 @if(error()){<p role="alert">{{error()}} <button type="button" (click)="load()">Reintentar</button></p>}
 @else if(loading()){<p>Cargando ítems…</p>}
 @else {<div class="results" role="group" aria-label="Selección múltiple de ítems">@for(item of results();track item.id){<label><input type="checkbox" [checked]="value().includes(item.id)" (change)="toggle(item.id)"/><span><small>{{item.code | inventoryCode}}</small>{{item.name}}</span></label>}@empty{<p>No hay coincidencias.</p>}</div><small>Mostrando {{results().length}} resultados · {{value().length}} seleccionados</small>}
 </section>`,styleUrl:'./related-items-picker.component.scss'})
export class RelatedItemsPickerComponent implements OnInit {
 readonly value=input<number[]>([]);readonly selfId=input<number|null>(null);readonly valueChange=output<number[]>();
 private readonly api=inject(InventoryApiService);protected readonly options=signal<RelatedItem[]>([]);protected readonly query=signal('');protected readonly error=signal('');protected readonly loading=signal(true);
 protected readonly selected=computed(()=>this.options().filter(i=>this.value().includes(i.id)));
 protected readonly results=computed(()=>{const q=this.normalize(this.query());return this.options().filter(i=>i.id!==this.selfId()&&!i.archived&&this.normalize(i.name+' '+i.code).includes(q)).slice(0,30)});
 private normalize(s:string){return s.normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/[-\s]/g,'').toLowerCase()}
 ngOnInit(){this.load()}
 protected load(){this.loading.set(true);this.error.set('');this.api.fetchRelatedItemOptions().subscribe({next:items=>{this.options.set(items);this.loading.set(false)},error:()=>{this.loading.set(false);this.error.set('No se pudieron cargar las relaciones. Tu selección no se ha cambiado.')}})}
 protected toggle(id:number){this.valueChange.emit(this.value().includes(id)?this.value().filter(v=>v!==id):[...this.value(),id])}
}
