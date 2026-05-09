export enum MarketplaceKind {
  Hepsiburada = 1,
  Cimri = 3,
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
  includeProductDetails?: boolean;
  expandAllOffers?: boolean;
  forceRefresh?: boolean;
}

export interface CimriListingSyncResultDto {
  scrapeRunId: string;
  pagesFetched: number;
  productsAffected: number;
  offersAffected: number;
  merchantsAffected: number;
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
