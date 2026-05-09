import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizationPipe, PermissionService } from '@abp/ng.core';
import { CimriSyncService } from '../cimri-sync.service';
import {
  CimriListingSyncInput,
  CimriListingSyncResultDto,
  CimriProductDto,
} from '../marketplace.models';
import { formatAbpHttpError } from '../format-abp-http-error';

@Component({
  selector: 'app-cimri-products',
  standalone: true,
  imports: [CommonModule, FormsModule, LocalizationPipe],
  templateUrl: './cimri-products.component.html',
  styleUrls: ['./cimri-products.component.scss'],
})
export class CimriProductsComponent implements OnInit {
  private readonly cimri = inject(CimriSyncService);
  private readonly permission = inject(PermissionService);

  readonly products = signal<CimriProductDto[]>([]);
  readonly totalCount = signal(0);
  readonly skipCount = signal(0);
  readonly maxResultCount = signal(20);
  readonly search = signal<string>('');
  readonly includeOffers = signal(true);

  readonly isLoading = signal(false);
  readonly isSyncing = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly syncError = signal<string | null>(null);
  readonly lastSyncResult = signal<CimriListingSyncResultDto | null>(null);

  readonly maxPagesInput = signal<number | null>(null);
  readonly maxProductsInput = signal<number | null>(null);
  readonly expandAllOffers = signal(true);
  readonly includeProductDetails = signal(true);

  readonly canSync = computed(() => this.permission.getGrantedPolicy('BeeBAK.Cimri.Sync'));

  readonly expandedProductId = signal<string | null>(null);

  readonly pageCount = computed(() => {
    const total = this.totalCount();
    const size = this.maxResultCount();
    return size > 0 ? Math.max(1, Math.ceil(total / size)) : 1;
  });

  readonly currentPage = computed(() => {
    const size = this.maxResultCount();
    return size > 0 ? Math.floor(this.skipCount() / size) + 1 : 1;
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.cimri
      .getList({
        skipCount: this.skipCount(),
        maxResultCount: this.maxResultCount(),
        search: this.search() || null,
        includeOffers: this.includeOffers(),
      })
      .subscribe({
        next: result => {
          this.products.set(result.items ?? []);
          this.totalCount.set(result.totalCount ?? 0);
          this.isLoading.set(false);
        },
        error: err => {
          this.loadError.set(formatAbpHttpError(err));
          this.isLoading.set(false);
        },
      });
  }

  toggleProduct(id: string): void {
    this.expandedProductId.set(this.expandedProductId() === id ? null : id);
  }

  prevPage(): void {
    const next = Math.max(0, this.skipCount() - this.maxResultCount());
    if (next !== this.skipCount()) {
      this.skipCount.set(next);
      this.load();
    }
  }

  nextPage(): void {
    const next = this.skipCount() + this.maxResultCount();
    if (next < this.totalCount()) {
      this.skipCount.set(next);
      this.load();
    }
  }

  applySearch(): void {
    this.skipCount.set(0);
    this.load();
  }

  changePageSize(size: number): void {
    this.maxResultCount.set(size);
    this.skipCount.set(0);
    this.load();
  }

  triggerSync(): void {
    this.syncError.set(null);
    this.lastSyncResult.set(null);
    this.isSyncing.set(true);

    const payload: CimriListingSyncInput = {
      maxPages: this.maxPagesInput(),
      maxProducts: this.maxProductsInput(),
      includeProductDetails: this.includeProductDetails(),
      expandAllOffers: this.expandAllOffers(),
      forceRefresh: true,
    };

    this.cimri.syncListing(payload).subscribe({
      next: result => {
        this.lastSyncResult.set(result);
        this.isSyncing.set(false);
        this.skipCount.set(0);
        this.load();
      },
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.isSyncing.set(false);
      },
    });
  }

  trackByProductId(_idx: number, p: CimriProductDto): string {
    return p.id;
  }

  trackByOfferId(_idx: number, o: { id: string }): string {
    return o.id;
  }

  formatPrice(amount?: number | null, currency?: string | null): string {
    if (amount === null || amount === undefined) {
      return '—';
    }
    const ccy = (currency || 'TRY').toUpperCase();
    try {
      return new Intl.NumberFormat('tr-TR', {
        style: 'currency',
        currency: ccy,
        maximumFractionDigits: 2,
      }).format(amount);
    } catch {
      return `${amount.toFixed(2)} ${ccy}`;
    }
  }
}
