import { CommonModule } from '@angular/common';
import {
  AfterViewChecked,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizationPipe, LocalizationService, PermissionService } from '@abp/ng.core';
import { CimriSyncService } from '../cimri-sync.service';
import { CimriListingSyncSessionService } from '../cimri-listing-sync-session.service';
import {
  CimriListingSyncEventDto,
  CimriListingSyncInput,
  CimriListingSyncResultDto,
  CimriListingSyncStatusDto,
  CimriOfferDto,
  CimriProductDto,
  EcScrapeRunEventLevel,
  EcScrapeRunStatus,
} from '../marketplace.models';
import { formatAbpHttpError } from '../format-abp-http-error';

interface CimriShareCardVm {
  id: string;
  theme: string;
  slots: CimriShareSlotVm[];
}

interface CimriShareSlotVm {
  slotIndex: number;
  product: CimriProductDto;
}

/** Arka plandaki çekim durumu sık güncellensin. */
const POLL_INTERVAL_MS = 400;
const MAX_EVENTS_KEPT = 500;

@Component({
  selector: 'app-cimri-products',
  standalone: true,
  imports: [CommonModule, FormsModule, LocalizationPipe],
  templateUrl: './cimri-products.component.html',
  styleUrls: ['./cimri-products.component.scss'],
})
export class CimriProductsComponent implements OnInit, OnDestroy, AfterViewChecked {
  private readonly cimri = inject(CimriSyncService);
  private readonly permission = inject(PermissionService);
  private readonly listingSyncSession = inject(CimriListingSyncSessionService);
  private readonly localization = inject(LocalizationService);

  @ViewChild('eventLog') eventLogRef?: ElementRef<HTMLDivElement>;
  private autoScroll = true;
  private pendingScrollToBottom = false;

  readonly events = signal<CimriListingSyncEventDto[]>([]);
  private latestEventUtc: string | null = null;
  readonly levelEnum = EcScrapeRunEventLevel;

  readonly products = signal<CimriProductDto[]>([]);
  readonly totalCount = signal(0);
  readonly skipCount = signal(0);
  readonly maxResultCount = signal(20);
  readonly search = signal<string>('');
  readonly includeOffers = signal(true);

  readonly isLoading = signal(false);
  readonly isSyncing = signal(false);
  readonly isClearing = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly syncError = signal<string | null>(null);
  /** Veritabanını temizle isteği başarısız olduğunda (senkron hatasından ayrı başlık için). */
  readonly clearError = signal<string | null>(null);
  readonly lastSyncResult = signal<CimriListingSyncResultDto | null>(null);

  readonly maxPagesInput = signal<number | null>(null);
  readonly maxProductsInput = signal<number | null>(null);
  /** Boş bırakılırsa sunucudaki Cimri:ListingPageUrl kullanılır; o da yoksa senkron hata verir (otomatik indirimli-ürünler yok). */
  readonly listingPageUrl = signal('');
  readonly expandAllOffers = signal(true);
  readonly includeProductDetails = signal(true);

  readonly canSync = computed(() => this.permission.getGrantedPolicy('BeeBAK.Cimri.Sync'));

  readonly expandedProductId = signal<string | null>(null);

  readonly syncStatus = signal<CimriListingSyncStatusDto | null>(null);
  readonly isCancelling = signal(false);

  readonly statusEnum = EcScrapeRunStatus;

  readonly progressPercent = computed(() => {
    const s = this.syncStatus();
    if (!s) return 0;
    return Math.round((s.progress ?? 0) * 100);
  });

  readonly etaText = computed(() => {
    const s = this.syncStatus();
    if (!s) return '';
    if (!s.isActive) {
      return this.formatDuration(s.elapsedSeconds);
    }
    if (s.estimatedRemainingSeconds == null) {
      return '…';
    }
    return this.formatDuration(s.estimatedRemainingSeconds);
  });

  readonly elapsedText = computed(() => this.formatDuration(this.syncStatus()?.elapsedSeconds ?? 0));

  readonly statusLabelKey = computed(() => {
    const s = this.syncStatus();
    if (!s) return '';
    if (s.cancelRequested && s.isActive) return '::CimriStatusCancelling';
    switch (s.status) {
      case EcScrapeRunStatus.Pending: return '::CimriStatusPending';
      case EcScrapeRunStatus.Running: return '::CimriStatusRunning';
      case EcScrapeRunStatus.Completed: return '::CimriStatusCompleted';
      case EcScrapeRunStatus.Failed: return '::CimriStatusFailed';
      case EcScrapeRunStatus.Cancelled: return '::CimriStatusCancelled';
      default: return '';
    }
  });

  readonly statusVariant = computed(() => {
    const s = this.syncStatus();
    if (!s) return '';
    if (s.cancelRequested && s.isActive) return 'cancelling';
    switch (s.status) {
      case EcScrapeRunStatus.Running: return 'running';
      case EcScrapeRunStatus.Completed: return 'completed';
      case EcScrapeRunStatus.Failed: return 'failed';
      case EcScrapeRunStatus.Cancelled: return 'cancelled';
      default: return '';
    }
  });

  /** Senkronizasyon tamamlandığında kısa süre gösterilecek banner verisi. */
  readonly completionBanner = signal<{ productsAffected: number } | null>(null);
  private completionBannerTimer: ReturnType<typeof setTimeout> | null = null;

  private pollTimer: ReturnType<typeof setInterval> | null = null;

  readonly pageCount = computed(() => {
    const total = this.totalCount();
    const size = this.maxResultCount();
    return size > 0 ? Math.max(1, Math.ceil(total / size)) : 1;
  });

  readonly currentPage = computed(() => {
    const size = this.maxResultCount();
    return size > 0 ? Math.floor(this.skipCount() / size) + 1 : 1;
  });

  /** Bu sayfadaki liste satırlarından üretilen paylaşım kartları (aynı ürün görseli / teklif linkleri). */
  readonly slotsPerShareCard = 1;
  readonly shareCards = computed(() =>
    this.buildShareCardsFromProducts(this.products(), this.slotsPerShareCard),
  );
  readonly showShareCardsSection = computed(
    () => this.permission.getGrantedPolicy('BeeBAK.Cimri') && this.products().length > 0,
  );

  private readonly shareThemes = ['ember', 'aurora', 'tide', 'citrus'] as const;

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.clearCompletionBannerTimer();
  }

  ngAfterViewChecked(): void {
    if (this.pendingScrollToBottom && this.autoScroll && this.eventLogRef?.nativeElement) {
      const el = this.eventLogRef.nativeElement;
      el.scrollTop = el.scrollHeight;
      this.pendingScrollToBottom = false;
    }
  }

  onEventLogScroll(): void {
    const el = this.eventLogRef?.nativeElement;
    if (!el) return;
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    this.autoScroll = distanceFromBottom < 24;
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
          this.products.set(this.sortProductsByDiscountDesc(result.items ?? []));
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
    this.clearError.set(null);
    this.lastSyncResult.set(null);
    this.syncStatus.set(null);
    this.events.set([]);
    this.latestEventUtc = null;
    this.autoScroll = true;
    this.isSyncing.set(true);

    const payload: CimriListingSyncInput = {
      maxPages: this.maxPagesInput(),
      maxProducts: this.maxProductsInput(),
      includeOffers: this.includeProductDetails(),
      includeProductDetails: this.includeProductDetails(),
      expandAllOffers: this.expandAllOffers(),
      listingPageUrl: this.listingPageUrl().trim() || null,
      forceRefresh: true,
    };

    this.cimri.syncListing(payload).subscribe({
      next: result => {
        this.lastSyncResult.set(result);
        // Queue modunda API hemen döner; polling ile backend'den ilerlemeyi takip ederiz.
        if (result.queued && result.scrapeRunId) {
          this.startPolling(result.scrapeRunId);
        } else {
          this.isSyncing.set(false);
          this.skipCount.set(0);
          this.load();
        }
      },
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.isSyncing.set(false);
      },
    });
  }

  clearStoredData(): void {
    const msg = this.localization.instant('::CimriClearDatabaseConfirm');
    if (!confirm(msg)) {
      return;
    }
    this.stopPolling();
    this.listingSyncSession.clearRun();
    this.syncError.set(null);
    this.clearError.set(null);
    this.isClearing.set(true);
    this.loadError.set(null);
    this.cimri.clearAllStoredData().subscribe({
      next: () => {
        this.events.set([]);
        this.latestEventUtc = null;
        this.syncStatus.set(null);
        this.lastSyncResult.set(null);
        this.expandedProductId.set(null);
        this.skipCount.set(0);
        this.isClearing.set(false);
        this.clearError.set(null);
        this.load();
      },
      error: err => {
        // F12 → Console: tam HttpErrorResponse (Network sekmesi ile birlikte teşhis için).
        console.error('[BeeBAK Cimri] clearAllStoredData failed', err);
        const msg = formatAbpHttpError(err);
        this.clearError.set(msg);
        this.loadError.set(msg);
        this.isClearing.set(false);
      },
    });
  }

  cancelSync(): void {
    const status = this.syncStatus();
    if (!status || !status.scrapeRunId || this.isCancelling()) return;
    this.isCancelling.set(true);
    this.cimri.cancelSync(status.scrapeRunId).subscribe({
      next: s => {
        this.applyStatus(s);
        this.isCancelling.set(false);
      },
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.isCancelling.set(false);
      },
    });
  }

  private startPolling(scrapeRunId: string): void {
    this.stopPolling();
    this.listingSyncSession.beginRun(scrapeRunId);
    this.pollOnce(scrapeRunId);
    this.pollTimer = setInterval(() => this.pollOnce(scrapeRunId), POLL_INTERVAL_MS);
  }

  private pollOnce(scrapeRunId: string): void {
    this.cimri.getSyncStatus(scrapeRunId, this.latestEventUtc).subscribe({
      next: s => this.applyStatus(s),
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.stopPolling();
        this.isSyncing.set(false);
      },
    });
  }

  private applyStatus(s: CimriListingSyncStatusDto): void {
    this.syncStatus.set(s);
    this.appendEvents(s.events ?? []);
    if (s.latestEventUtc) {
      this.latestEventUtc = s.latestEventUtc;
    }
    if (!s.isActive) {
      this.stopPolling();
      this.isSyncing.set(false);
      this.listingSyncSession.clearRun();
      if (s.status === EcScrapeRunStatus.Completed) {
        this.showCompletionBanner(s.processedItems);
      }
      this.skipCount.set(0);
      this.load();
    }
  }

  private showCompletionBanner(productsAffected: number): void {
    this.clearCompletionBannerTimer();
    this.completionBanner.set({ productsAffected });
    this.completionBannerTimer = setTimeout(() => {
      this.completionBanner.set(null);
    }, 6000);
  }

  dismissCompletionBanner(): void {
    this.clearCompletionBannerTimer();
    this.completionBanner.set(null);
  }

  private clearCompletionBannerTimer(): void {
    if (this.completionBannerTimer) {
      clearTimeout(this.completionBannerTimer);
      this.completionBannerTimer = null;
    }
  }

  private appendEvents(incoming: CimriListingSyncEventDto[]): void {
    if (!incoming.length) return;
    const current = this.events();
    const seen = new Set(current.map(e => e.id));
    const additions = incoming.filter(e => !seen.has(e.id));
    if (!additions.length) return;
    const merged = [...current, ...additions];
    if (merged.length > MAX_EVENTS_KEPT) {
      merged.splice(0, merged.length - MAX_EVENTS_KEPT);
    }
    this.events.set(merged);
    this.pendingScrollToBottom = true;
  }

  /** Listede en yüksek indirim üstte; API sırasına ek güvence. discountPercent yoksa eski/liste fiyatından tahmin. */
  private sortProductsByDiscountDesc(items: CimriProductDto[]): CimriProductDto[] {
    return [...items].sort((a, b) => {
      const db = this.effectiveSortDiscount(b);
      const da = this.effectiveSortDiscount(a);
      if (db !== da) {
        return db - da;
      }
      return (a.title ?? '').localeCompare(b.title ?? '', 'tr', { sensitivity: 'base' });
    });
  }

  private effectiveSortDiscount(p: CimriProductDto): number {
    if (p.discountPercent != null && Number.isFinite(Number(p.discountPercent))) {
      return Number(p.discountPercent);
    }
    const prev = p.previousPriceAmount;
    const best = p.bestPriceAmount;
    if (prev != null && best != null && prev > 0 && prev > best) {
      return ((prev - best) / prev) * 100;
    }
    return Number.NEGATIVE_INFINITY;
  }

  /** Liste kartında indirim rozeti — API yüzdesi veya eski fiyattan tahmin. */
  effectiveDiscountPercent(p: CimriProductDto): number | null {
    if (p.discountPercent != null && Number.isFinite(Number(p.discountPercent))) {
      return Math.round(Number(p.discountPercent));
    }
    const prev = p.previousPriceAmount;
    const best = this.displayBestPriceAmount(p);
    if (prev != null && best != null && prev > 0 && prev > best) {
      return Math.round(((prev - best) / prev) * 100);
    }
    return null;
  }

  /** Kartta gösterilecek en düşük fiyat (özet alan veya tekliflerden). */
  displayBestPriceAmount(p: CimriProductDto): number | null {
    if (p.bestPriceAmount != null && Number.isFinite(p.bestPriceAmount)) {
      return p.bestPriceAmount;
    }
    const offers = p.offers ?? [];
    if (!offers.length) {
      return null;
    }
    return Math.min(...offers.map(o => o.price));
  }

  currencyForProduct(p: CimriProductDto): string {
    const offers = p.offers ?? [];
    const tagged = offers.find(o => o.isCheapest);
    if (tagged?.currency) {
      return tagged.currency;
    }
    if (!offers.length) {
      return 'TRY';
    }
    const sorted = [...offers].sort((a, b) => a.price - b.price);
    return sorted[0]?.currency ?? 'TRY';
  }

  bestMerchantDisplay(p: CimriProductDto): string | null {
    const direct = p.bestPriceMerchantName?.trim();
    if (direct) {
      return direct;
    }
    const offers = p.offers ?? [];
    const cheapest = offers.find(o => o.isCheapest) ?? [...offers].sort((a, b) => a.price - b.price)[0];
    const name = cheapest?.merchantName?.trim() || cheapest?.offerTitle?.trim();
    return name || null;
  }

  /** Liste satırı beyaz fiyat sütunu — tek kez hesaplanır (0 ₺ dahil). */
  productPriceColumn(p: CimriProductDto): {
    amount: number;
    currency: string;
    merchant: string | null;
    previous: number | null;
  } | null {
    const amount = this.displayBestPriceAmount(p);
    if (amount === null) {
      return null;
    }
    return {
      amount,
      currency: this.currencyForProduct(p),
      merchant: this.bestMerchantDisplay(p),
      previous: p.previousPriceAmount ?? null,
    };
  }

  /** Paylaşım kartı görseli — tıklanınca en ucuz teklifin mağaza ürün sayfası (yoksa Cimri ürün URL'si). */
  bestMerchantUrl(p: CimriProductDto): string {
    const top = this.topMerchantLinksForProduct(p, 1);
    const url = top[0]?.url?.trim();
    return url || p.productUrl;
  }

  slotImageAriaLabel(product: CimriProductDto): string {
    const m = this.bestMerchantDisplay(product);
    const t = (product.title ?? '').trim();
    if (m && t) {
      return `${t} — ${m}`;
    }
    return t || m || 'Product';
  }

  /**
   * Paylaşım kartı slotu: çevrimiçi tekliflerin ortalaması (≥2 teklif) + en düşük fiyat.
   * Tıklama `bestMerchantUrl` ile en uygun mağazaya gider.
   */
  /** Kart ürün görseli üzerinde başlık ve/veya fiyat bandı gösterilsin mi. */
  slotShowCaptionBox(product: CimriProductDto): boolean {
    return !!(product.title?.trim() || this.shareSlotOverlay(product));
  }

  shareSlotOverlay(product: CimriProductDto): {
    avg: number | null;
    avgStoreCount: number;
    lowest: number;
    currency: string;
    merchant: string | null;
    /** Önceki / üst referans fiyat (liste, indirim öncesi veya en yüksek teklif); en uygunun üzerindeyse gösterilir. */
    bestPriceFrom: number | null;
  } | null {
    const lowestAmt = this.displayBestPriceAmount(product);
    if (lowestAmt === null) {
      return null;
    }
    const offers = product.offers ?? [];
    let avg: number | null = null;
    if (offers.length >= 2) {
      const sum = offers.reduce((s, o) => s + o.price, 0);
      avg = sum / offers.length;
    }

    let bestPriceFrom: number | null = null;
    const prev = product.previousPriceAmount;
    if (prev != null && Number.isFinite(Number(prev)) && prev > lowestAmt) {
      bestPriceFrom = Number(prev);
    } else {
      const pct = product.discountPercent;
      if (
        pct != null &&
        Number.isFinite(Number(pct)) &&
        Number(pct) > 0 &&
        Number(pct) < 100
      ) {
        const computed = lowestAmt / (1 - Number(pct) / 100);
        if (Number.isFinite(computed) && computed > lowestAmt) {
          bestPriceFrom = computed;
        }
      }
    }
    if (bestPriceFrom == null && offers.length >= 2) {
      const maxPrice = Math.max(...offers.map(o => o.price));
      if (Number.isFinite(maxPrice) && maxPrice > lowestAmt) {
        bestPriceFrom = maxPrice;
      }
    }
    if (bestPriceFrom != null && Math.abs(bestPriceFrom - lowestAmt) < 0.005) {
      bestPriceFrom = null;
    }

    return {
      avg,
      avgStoreCount: offers.length,
      lowest: lowestAmt,
      currency: this.currencyForProduct(product),
      merchant: this.bestMerchantDisplay(product),
      bestPriceFrom,
    };
  }

  formatTime(iso: string): string {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleTimeString('tr-TR', { hour12: false });
  }

  eventLevelClass(level: EcScrapeRunEventLevel): string {
    switch (level) {
      case EcScrapeRunEventLevel.Success: return 'evt-success';
      case EcScrapeRunEventLevel.Warning: return 'evt-warning';
      case EcScrapeRunEventLevel.Error:   return 'evt-error';
      default: return 'evt-info';
    }
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  trackShareCard(_i: number, c: CimriShareCardVm): string {
    return c.id;
  }

  slotGridClass(_card: CimriShareCardVm): string {
    return 'slots-grid slots-grid--1';
  }

  private buildShareCardsFromProducts(
    products: CimriProductDto[],
    slotsPerCard: number,
  ): CimriShareCardVm[] {
    if (!products.length) {
      return [];
    }
    const cards: CimriShareCardVm[] = [];
    for (let i = 0; i < products.length; i += slotsPerCard) {
      const slice = products.slice(i, i + slotsPerCard);
      const idx = cards.length;
      cards.push({
        id: `share-${idx}-${slice[0]?.contentId ?? ''}`,
        theme: this.shareThemes[idx % this.shareThemes.length],
        slots: slice.map((p, si) => ({
          slotIndex: si + 1,
          product: p,
        })),
      });
    }
    return cards;
  }

  private topMerchantLinksForProduct(
    p: CimriProductDto,
    max: number,
  ): { merchantName: string; price: number; currency: string; url: string }[] {
    const offers = p.offers ?? [];
    const fallbackCcy = offers[0]?.currency ?? 'TRY';
    if (!offers.length) {
      const price = p.bestPriceAmount ?? 0;
      return [
        {
          merchantName: 'Mağaza',
          price,
          currency: fallbackCcy,
          url: p.productUrl,
        },
      ];
    }
    const sorted = [...offers].sort((a, b) => a.price - b.price);
    const byMerchant = new Map<string, CimriOfferDto>();
    for (const o of sorted) {
      if (!byMerchant.has(o.merchantId)) {
        byMerchant.set(o.merchantId, o);
      }
    }
    const uniq = [...byMerchant.values()].slice(0, max);
    return uniq.map(o => ({
      merchantName: (o.merchantName || o.offerTitle || o.sellerName || 'Mağaza').trim(),
      price: o.price,
      currency: o.currency || fallbackCcy,
      // offerUrl (cimri.com/offer/xxx) Telegram'da çalışmaz — sadece doğrudan mağaza URL'si
      url: (o.merchantProductUrl || p.productUrl).trim(),
    }));
  }

  trackByProductId(_idx: number, p: CimriProductDto): string {
    return p.id;
  }

  trackByOfferId(_idx: number, o: { id: string }): string {
    return o.id;
  }

  trackByEventId(_idx: number, e: CimriListingSyncEventDto): string {
    return e.id;
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

  private formatDuration(totalSeconds: number): string {
    const seconds = Math.max(0, Math.round(totalSeconds));
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    if (h > 0) {
      return `${h}sa ${m.toString().padStart(2, '0')}dk ${s.toString().padStart(2, '0')}sn`;
    }
    if (m > 0) {
      return `${m}dk ${s.toString().padStart(2, '0')}sn`;
    }
    return `${s}sn`;
  }
}
