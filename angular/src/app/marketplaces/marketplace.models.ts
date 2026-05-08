export enum MarketplaceKind {
  Hepsiburada = 1,
  Trendyol = 2,
}

export interface EcMarketplaceProductDto {
  id: string;
  marketplace: MarketplaceKind;
  externalProductId: string;
  title: string;
  productUrl: string;
  lastSyncedUtc?: string | null;
  latestPriceAmount?: number | null;
  currency?: string | null;
}

export interface EcMarketplaceProductPagedResult {
  items: EcMarketplaceProductDto[];
  totalCount: number;
}

export interface TrendyolListingSyncInput {
  searchQuery?: string | null;
  ecMarketplaceCategoryId?: string | null;
  maxPages?: number | null;
  forceRefresh?: boolean;
}

export interface TrendyolListingSyncResultDto {
  scrapeRunId: string;
  pagesFetched: number;
  productsAffected: number;
  resolvedSearchQuery: string;
}
