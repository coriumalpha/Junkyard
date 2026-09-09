import { Pipe, PipeTransform } from '@angular/core';
import { formatInventoryCode } from './inventory-code';
export { formatInventoryCode } from './inventory-code';
@Pipe({name:'inventoryCode',standalone:true})
export class InventoryCodePipe implements PipeTransform {
  transform(value:string|null|undefined):string { return formatInventoryCode(value); }
}
