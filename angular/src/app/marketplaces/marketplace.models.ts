export enum MarketplaceKind {
  Hepsiburada = 1,
  Cimri = 3,
  Akakce = 4,
}

export interface EcMarketplaceProductDto {
  id: string;
  marketplace: MarketplaceKind;
  externalProductId: string;
  title: string;
  productUrl: string;
  brandName?: string | null;
  primaryImageUrl?: string | null;
  ratingAverage?: number | null;
  reviewCount?: number | null;
  lastSyncedUtc?: string | null;
  latestPriceAmount?: number | null;
  currency?: string | null;
  listPriceAmount?: number | null;
  discountPercent?: number | null;
}

export interface EcMarketplaceProductPagedResult {
  items: EcMarketplaceProductDto[];
  totalCount: number;
}

export interface CimriListingSyncInput {
  maxPages?: number | null;
  maxProducts?: number | null;
  /** Sunucu ile uyum: öncelik `includeProductDetails`, yoksa `includeOffers`. */
  includeOffers?: boolean;
  includeProductDetails?: boolean | null;
  expandAllOffers?: boolean;
  forceRefresh?: boolean;
  /** Tam HTTPS listeleme URL'si; boş bırakılırsa yalnızca sunucu Cimri:ListingPageUrl (BaseUrl+yol birleştirilmez). */
  listingPageUrl?: string | null;
  restrictOffersToAllowedMerchants?: boolean | null;
  requireMerchantProductId?: boolean | null;
  skipProductWithoutQualifiedOffers?: boolean | null;
  allowedMerchantSubstrings?: string[] | null;
}

export interface CimriListingSyncResultDto {
  scrapeRunId: string;
  pagesFetched: number;
  productsAffected: number;
  offersAffected: number;
  merchantsAffected: number;
  resolvedListingPageUrl?: string | null;
  queued?: boolean;
}

export enum EcScrapeRunStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
  Cancelled = 4,
}

export enum EcScrapeRunEventLevel {
  Info = 0,
  Success = 1,
  Warning = 2,
  Error = 3,
}

export interface CimriListingSyncEventDto {
  id: string;
  timestampUtc: string;
  level: EcScrapeRunEventLevel;
  phase: string;
  message: string;
  title?: string | null;
  url?: string | null;
  index?: number | null;
  total?: number | null;
}

export interface CimriListingSyncStatusDto {
  scrapeRunId: string;
  status: EcScrapeRunStatus;
  totalItems: number;
  processedItems: number;
  failedItems: number;
  cancelRequested: boolean;
  startedUtc: string;
  completedUtc?: string | null;
  notes?: string | null;
  progress: number;
  elapsedSeconds: number;
  estimatedRemainingSeconds?: number | null;
  isActive: boolean;
  events: CimriListingSyncEventDto[];
  latestEventUtc?: string | null;
  /** Bu çalışmada kullanılan tam liste HTTPS adresi (API durum sorgusu). */
  resolvedListingPageUrl?: string | null;
  /** 'form' | 'server' — adres senkron formundan mı, Cimri:ListingPageUrl'den mi geldi. */
  listingPageSource?: string | null;
}

export interface CimriOfferDto {
  id: string;
  merchantId: string;
  merchantName: string;
  merchantSlug?: string | null;
  merchantLogoUrl?: string | null;
  displayOrder: number;
  offerTitle?: string | null;
  sellerName?: string | null;
  price: number;
  currency: string;
  shippingText?: string | null;
  promotionText?: string | null;
  lastUpdatedText?: string | null;
  lastUpdatedUtc?: string | null;
  installmentBadge?: string | null;
  merchantScore?: number | null;
  isSponsored: boolean;
  isCheapest: boolean;
  yearsOnCimri?: number | null;
  offerUrl?: string | null;
  merchantProductUrl?: string | null;
  merchantProductId?: string | null;
  scrapedUtc: string;
}

export interface CimriProductDto {
  id: string;
  contentId: string;
  productUrl: string;
  primaryCategorySlug?: string | null;
  categoryPath?: string | null;
  title: string;
  brandName?: string | null;
  primaryImageUrl?: string | null;
  totalOfferCount?: number | null;
  discountPercent?: number | null;
  bestPriceAmount?: number | null;
  bestPriceMerchantName?: string | null;
  previousPriceAmount?: number | null;
  isActive: boolean;
  lastSyncedUtc?: string | null;
  offers: CimriOfferDto[];
}

export interface CimriProductPagedResult {
  items: CimriProductDto[];
  totalCount: number;
}

export interface GetCimriProductListInput {
  skipCount?: number;
  maxResultCount?: number;
  search?: string | null;
  includeOffers?: boolean;
}

export interface AkakceListingSyncInput {
  maxPages?: number | null;
  maxProducts?: number | null;
  includeProductDetails?: boolean | null;
  forceRefresh?: boolean;
  listingPageUrl?: string | null;
}

export interface AkakceListingSyncResultDto {
  scrapeRunId: string;
  pagesFetched: number;
  productsAffected: number;
  offersAffected: number;
  merchantsAffected: number;
  resolvedListingPageUrl?: string | null;
  queued?: boolean;
}

export interface AkakceListingSyncEventDto {
  id: string;
  timestampUtc: string;
  level: EcScrapeRunEventLevel;
  phase: string;
  message: string;
  title?: string | null;
  url?: string | null;
  index?: number | null;
  total?: number | null;
}

export interface AkakceListingSyncStatusDto {
  scrapeRunId: string;
  status: EcScrapeRunStatus;
  totalItems: number;
  processedItems: number;
  failedItems: number;
  cancelRequested: boolean;
  startedUtc: string;
  completedUtc?: string | null;
  notes?: string | null;
  progress: number;
  elapsedSeconds: number;
  estimatedRemainingSeconds?: number | null;
  isActive: boolean;
  events: AkakceListingSyncEventDto[];
  latestEventUtc?: string | null;
  resolvedListingPageUrl?: string | null;
  listingPageSource?: string | null;
}

export interface AkakceOfferDto {
  id: string;
  merchantId: string;
  merchantName: string;
  merchantSlug?: string | null;
  merchantLogoUrl?: string | null;
  displayOrder: number;
  offerTitle?: string | null;
  price: number;
  currency: string;
  shippingText?: string | null;
  shippingAmount?: number | null;
  isFreeShipping?: boolean | null;
  stockText?: string | null;
  stockQuantity?: number | null;
  deliveryText?: string | null;
  lastUpdatedText?: string | null;
  lastUpdatedUtc?: string | null;
  isSponsored: boolean;
  isCheapest: boolean;
  offerUrl?: string | null;
  akakceOfferUrl?: string | null;
  merchantProductUrl?: string | null;
  siteRedirectUrl?: string | null;
  scrapedUtc: string;
}

export interface AkakceProductDto {
  id: string;
  productCode: string;
  productUrl: string;
  categoryPath?: string | null;
  title: string;
  brandName?: string | null;
  primaryImageUrl?: string | null;
  discountPercent?: number | null;
  bestPriceAmount?: number | null;
  previousPriceAmount?: number | null;
  offerCount?: number | null;
  isActive: boolean;
  lastSyncedUtc?: string | null;
  offers: AkakceOfferDto[];
}

export interface AkakceProductPagedResult {
  items: AkakceProductDto[];
  totalCount: number;
}

export interface GetAkakceProductListInput {
  skipCount?: number;
  maxResultCount?: number;
  search?: string | null;
  includeOffers?: boolean;
}

// ── Monitor ──────────────────────────────────────────────────────────────────

export interface TelegramSentItemDto {
  id: string;
  marketplace: MarketplaceKind;
  title: string;
  imageUrl?: string | null;
  productUrl: string;
  price: number;
  previousPrice?: number | null;
  discountPercent?: number | null;
  triggerType: string;
  sentAt: string;
}

export interface AllActiveRunsDto {
  cimriRuns: CimriListingSyncStatusDto[];
  akakceRuns: AkakceListingSyncStatusDto[];
  recentSent: TelegramSentItemDto[];
  telegramQueueSize: number;
}
