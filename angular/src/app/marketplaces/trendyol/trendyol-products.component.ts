import { CommonModule } from '@angular/common';
import { Component, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  LocalizationPipe,
  LocalizationService,
  PermissionDirective,
} from '@abp/ng.core';
import { ToasterService } from '@abp/ng.theme.shared';
import { EcMarketplaceProductService } from '../ec-marketplace-product.service';
import { TrendyolSyncService } from '../trendyol-sync.service';
import {
  EcMarketplaceProductDto,
  EcMarketplaceProductPagedResult,
  MarketplaceKind,
} from '../marketplace.models';

@Component({
  selector: 'app-trendyol-products',
  templateUrl: './trendyol-products.component.html',
  styleUrls: ['./trendyol-products.component.scss'],
  imports: [CommonModule, FormsModule, LocalizationPipe, PermissionDirective],
})
export class TrendyolProductsComponent implements OnInit {
  private readonly productService = inject(EcMarketplaceProductService);
  private readonly syncService = inject(TrendyolSyncService);
  private readonly toaster = inject(ToasterService);
  private readonly l = inject(LocalizationService);

  items: EcMarketplaceProductDto[] = [];
  totalCount = 0;
  page = 1;
  readonly pageSize = 10;
  loading = false;
  syncing = false;

  syncQuery = '';

  ngOnInit(): void {
    this.loadPage();
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  loadPage(): void {
    this.loading = true;
    this.productService
      .getList({
        marketplace: MarketplaceKind.Trendyol,
        skipCount: (this.page - 1) * this.pageSize,
        maxResultCount: this.pageSize,
      })
      .subscribe({
        next: res => {
          const r = res as EcMarketplaceProductPagedResult & {
            Items?: EcMarketplaceProductDto[];
            TotalCount?: number;
          };
          this.items = r.items ?? r.Items ?? [];
          this.totalCount = r.totalCount ?? r.TotalCount ?? 0;
          this.loading = false;
        },
        error: () => {
          this.loading = false;
        },
      });
  }

  prevPage(): void {
    if (this.page > 1) {
      this.page--;
      this.loadPage();
    }
  }

  nextPage(): void {
    if (this.page < this.totalPages) {
      this.page++;
      this.loadPage();
    }
  }

  sync(): void {
    this.syncing = true;
    this.syncService
      .sync({
        searchQuery: this.syncQuery?.trim() || undefined,
        forceRefresh: false,
      })
      .subscribe({
        next: result => {
          this.syncing = false;
          this.toaster.success(
            this.l.instant(
              '::TrendyolSyncFinishedDetail',
              result.productsAffected.toString(),
              result.pagesFetched.toString(),
            ),
            this.l.instant('::TrendyolProductsTitle'),
          );
          this.page = 1;
          this.loadPage();
        },
        error: () => {
          this.syncing = false;
        },
      });
  }
}
