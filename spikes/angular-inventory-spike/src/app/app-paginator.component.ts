import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

@Component({
  selector: 'app-paginator',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  templateUrl: './app-paginator.component.html',
  styleUrl: './app-paginator.component.scss'
})
export class AppPaginatorComponent implements OnChanges {
  @Input() page = 1;
  @Input() pageCount = 1;
  @Input() start = 0;
  @Input() end = 0;
  @Input() total = 0;
  @Input() itemLabel = 'elementos';
  @Input() showAll = false;
  @Input() allowShowAll = false;
  @Input() compact = false;
  @Output() readonly pageChange = new EventEmitter<number>();
  @Output() readonly showAllChange = new EventEmitter<boolean>();

  protected draftPage = 1;

  ngOnChanges(): void {
    this.draftPage = this.safePage(this.page);
  }

  protected previous(): void {
    this.goTo(this.safePage(this.page) - 1);
  }

  protected next(): void {
    this.goTo(this.safePage(this.page) + 1);
  }

  protected goTo(value: number | string): void {
    const page = this.safePage(Number(value));
    this.draftPage = page;
    if (page !== this.safePage(this.page)) {
      this.pageChange.emit(page);
    }
  }

  protected toggleShowAll(): void {
    this.showAllChange.emit(!this.showAll);
  }

  protected canGoBack(): boolean {
    return !this.showAll && this.safePage(this.page) > 1;
  }

  protected canGoForward(): boolean {
    return !this.showAll && this.safePage(this.page) < this.safePageCount();
  }

  protected safePageCount(): number {
    return Math.max(1, Math.floor(Number(this.pageCount) || 1));
  }

  private safePage(value: number): number {
    const parsed = Math.floor(Number(value) || 1);
    return Math.max(1, Math.min(this.safePageCount(), parsed));
  }
}
