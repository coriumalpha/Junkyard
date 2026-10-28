import {Component,Input,Output,EventEmitter,OnChanges,SimpleChanges,signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInputModule} from '@angular/material/input';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {DescriptionEditorComponent} from './description-editor.component';
import {ItemPropertyFieldsComponent} from './item-property-fields.component';
import {RelatedItemsPickerComponent} from './related-items-picker.component';
import {SearchableSelectComponent,SearchableSelectOption,SearchableSelectValue} from './searchable-select.component';
import {TagPickerComponent} from './tag-picker.component';
import {InventoryItemUpdate,InventoryOptionsResponse,ItemPropertyField} from './inventory-api.service';
@Component({selector:'app-item-editor',standalone:true,imports:[FormsModule,MatButtonModule,MatIconModule,MatFormFieldModule,MatInputModule,MatSlideToggleModule,DescriptionEditorComponent,ItemPropertyFieldsComponent,RelatedItemsPickerComponent,SearchableSelectComponent,TagPickerComponent],templateUrl:'./item-editor.component.html',styleUrl:'./item-editor.component.scss'})
export class ItemEditorComponent implements OnChanges {
 @Input({required:true}) value!:InventoryItemUpdate;
 @Input({required:true}) options!:InventoryOptionsResponse;
 @Input() classOptions:SearchableSelectOption[]=[];@Input() subtypeOptions:SearchableSelectOption[]=[];
 @Input() boxes:SearchableSelectOption[]=[];@Input() conditions:SearchableSelectOption[]=[];
 @Input() fields:ItemPropertyField[]=[];@Input() selfId:number|null=null;@Input() classLabel='';
 @Input() photoCapture=false;
 @Input() initialSection='general';@Input() allowCreateTag=false;
 @Output() patch=new EventEmitter<Partial<InventoryItemUpdate>>();
 @Output() classChange=new EventEmitter<SearchableSelectValue|SearchableSelectValue[]>();
 @Output() subtypeChange=new EventEmitter<SearchableSelectValue|SearchableSelectValue[]>();
 @Output() createTag=new EventEmitter<void>();
 protected readonly section=signal('general');
 protected readonly sections=[{key:'general',label:'Esencial',hint:'Nombre, descripción y ubicación',icon:'inventory_2'},{key:'classification',label:'Clasificación',hint:'Clase, etiquetas y propiedades',icon:'category'},{key:'placement',label:'Estado y control',hint:'Condición y existencias',icon:'fact_check'},{key:'relations',label:'Relaciones',hint:'Componentes y accesorios',icon:'hub'}];
 ngOnChanges(changes:SimpleChanges){if(changes['initialSection'])this.section.set(this.initialSection);}
}
