import { Pipe, PipeTransform } from '@angular/core';

export function formatInventoryCode(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  const compact = value.replace(/\s+/g, '').toUpperCase();
  const match = compact.match(/^(IT|CT)-?(\d+)$/);
  if (!match) {
    return value;
  }

  const prefix = match[1];
  const digits = match[2].padStart(6, '0').slice(-6);
  return prefix === 'IT'
    ? `IT-${digits.slice(0, 3)}-${digits.slice(3)}`
    : `CT-${digits}`;
}

@Pipe({
  name: 'inventoryCode',
  standalone: true
})
export class InventoryCodePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatInventoryCode(value);
  }
}
