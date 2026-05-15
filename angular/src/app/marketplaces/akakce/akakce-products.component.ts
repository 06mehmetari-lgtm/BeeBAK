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
import { LocalizationPipe, PermissionService } from '@abp/ng.core';
import { AkakceListingSyncSessionService } from '../akakce-listing-sync-session.service';
import { AkakceSyncService } from '../akakce-sync.service';
import { formatAbpHttpError } from '../format-abp-http-error';
import {
  AkakceListingSyncEventDto,
  AkakceListingSyncInput,
  AkakceListingSyncResultDto,
  AkakceListingSyncStatusDto,
  AkakceOfferDto,
  AkakceProductDto,
  EcScrapeRunEventLevel,
  EcScrapeRunStatus,
} from '../marketplace.models';

const POLL_INTERVAL_MS = 400;
const MAX_EVENTS_KEPT = 500;

@Component({
  selector: 'app-akakce-products',
  standalone: true,
  imports: [CommonModule, FormsModule, LocalizationPipe],
  templateUrl: './akakce-products.component.html',
  styleUrls: ['./akakce-products.component.scss'],
})
export class AkakceProductsComponent implements OnInit, OnDestroy, AfterViewChecked {
  private readonly akakce = inject(AkakceSyncService);
  private readonly permission = inject(PermissionService);
  private readonly listingSyncSession = inject(AkakceListingSyncSessionService);

  @ViewChild('eventLog') eventLogRef?: ElementRef<HTMLDivElement>;
  private autoScroll = true;
  private pendingScrollToBottom = false;
  private latestEventUtc: string | null = null;
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  readonly events = signal<AkakceListingSyncEventDto[]>([]);
  readonly levelEnum = EcScrapeRunEventLevel;
  readonly statusEnum = EcScrapeRunStatus;

  readonly products = signal<AkakceProductDto[]>([]);
  readonly totalCount = signal(0);
  readonly skipCount = signal(0);
  readonly maxResultCount = signal(20);
  readonly search = signal('');
  readonly includeOffers = signal(true);

  readonly isLoading = signal(false);
  readonly isSyncing = signal(false);
  readonly isClearing = signal(false);
  readonly isCancelling = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly syncError = signal<string | null>(null);
  readonly clearError = signal<string | null>(null);
  readonly lastSyncResult = signal<AkakceListingSyncResultDto | null>(null);
  readonly syncStatus = signal<AkakceListingSyncStatusDto | null>(null);

  readonly maxPagesInput = signal<number | null>(null);
  readonly maxProductsInput = signal<number | null>(null);
  readonly listingPageUrl = signal('https://www.akakce.com/fiyati-dusen-urunler/?s=5');
  readonly includeProductDetails = signal(true);
  readonly expandedProductId = signal<string | null>(null);

  readonly canSync = computed(() => this.permission.getGrantedPolicy('BeeBAK.Akakce.Sync'));

  readonly pageCount = computed(() => {
    const size = this.maxResultCount();
    return size > 0 ? Math.max(1, Math.ceil(this.totalCount() / size)) : 1;
  });

  readonly currentPage = computed(() => {
    const size = this.maxResultCount();
    return size > 0 ? Math.floor(this.skipCount() / size) + 1 : 1;
  });

  readonly progressPercent = computed(() => {
    const s = this.syncStatus();
    return s ? Math.round((s.progress ?? 0) * 100) : 0;
  });

  readonly elapsedText = computed(() => this.formatDuration(this.syncStatus()?.elapsedSeconds ?? 0));

  readonly etaText = computed(() => {
    const s = this.syncStatus();
    if (!s) return '';
    if (!s.isActive) return this.formatDuration(s.elapsedSeconds);
    return s.estimatedRemainingSeconds == null ? '...' : this.formatDuration(s.estimatedRemainingSeconds);
  });

  readonly statusLabelKey = computed(() => {
    const s = this.syncStatus();
    if (!s) return '';
    if (s.cancelRequested && s.isActive) return '::AkakceStatusCancelling';
    switch (s.status) {
      case EcScrapeRunStatus.Pending: return '::AkakceStatusPending';
      case EcScrapeRunStatus.Running: return '::AkakceStatusRunning';
      case EcScrapeRunStatus.Completed: return '::AkakceStatusCompleted';
      case EcScrapeRunStatus.Failed: return '::AkakceStatusFailed';
      case EcScrapeRunStatus.Cancelled: return '::AkakceStatusCancelled';
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

  ngOnInit(): void {
    this.load();
    this.listingSyncSession.hydrateFromStorage();
    const activeRunId = this.listingSyncSession.activeRunId();
    if (activeRunId) {
      this.startPolling(activeRunId);
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  ngAfterViewChecked(): void {
    if (this.pendingScrollToBottom && this.autoScroll && this.eventLogRef?.nativeElement) {
      const el = this.eventLogRef.nativeElement;
      el.scrollTop = el.scrollHeight;
      this.pendingScrollToBottom = false;
    }
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.akakce
      .getList({
        skipCount: this.skipCount(),
        maxResultCount: this.maxResultCount(),
        search: this.search() || null,
        includeOffers: this.includeOffers(),
      })
      .subscribe({
        next: result => {
          this.products.set(this.sortProducts(result.items ?? []));
          this.totalCount.set(result.totalCount ?? 0);
          this.isLoading.set(false);
        },
        error: err => {
          this.loadError.set(formatAbpHttpError(err));
          this.isLoading.set(false);
        },
      });
  }

  triggerSync(): void {
    this.isSyncing.set(true);
    this.syncError.set(null);
    this.clearError.set(null);
    this.lastSyncResult.set(null);
    this.events.set([]);
    this.latestEventUtc = null;

    const input: AkakceListingSyncInput = {
      maxPages: this.maxPagesInput(),
      maxProducts: this.maxProductsInput(),
      includeProductDetails: this.includeProductDetails(),
      forceRefresh: true,
      listingPageUrl: this.listingPageUrl().trim() || null,
    };

    this.akakce.syncListing(input).subscribe({
      next: result => {
        this.lastSyncResult.set(result);
        this.isSyncing.set(false);
        if (result.scrapeRunId) {
          this.listingSyncSession.beginRun(result.scrapeRunId);
          this.startPolling(result.scrapeRunId);
        } else {
          this.load();
        }
      },
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.isSyncing.set(false);
      },
    });
  }

  cancelSync(): void {
    const runId = this.syncStatus()?.scrapeRunId ?? this.listingSyncSession.activeRunId();
    if (!runId) return;
    this.isCancelling.set(true);
    this.akakce.cancelSync(runId).subscribe({
      next: status => {
        this.applyStatus(status);
        this.isCancelling.set(false);
      },
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.isCancelling.set(false);
      },
    });
  }

  clearStoredData(): void {
    if (!confirm('Akakce veritabanindaki urun, satici, teklif ve scrape kayitlari silinsin mi?')) {
      return;
    }
    this.isClearing.set(true);
    this.clearError.set(null);
    this.akakce.clearAllStoredData().subscribe({
      next: () => {
        this.products.set([]);
        this.totalCount.set(0);
        this.events.set([]);
        this.syncStatus.set(null);
        this.lastSyncResult.set(null);
        this.listingSyncSession.clearRun();
        this.isClearing.set(false);
      },
      error: err => {
        this.clearError.set(formatAbpHttpError(err));
        this.isClearing.set(false);
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

  searchNow(): void {
    this.skipCount.set(0);
    this.load();
  }

  onPageSizeChange(value: number | string): void {
    const parsed = Number(value);
    this.maxResultCount.set(Number.isFinite(parsed) && parsed > 0 ? parsed : 20);
    this.skipCount.set(0);
    this.load();
  }

  onEventLogScroll(): void {
    const el = this.eventLogRef?.nativeElement;
    if (!el) return;
    this.autoScroll = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  }

  trackByProductId(_: number, item: AkakceProductDto): string {
    return item.id;
  }

  trackByOfferId(_: number, item: AkakceOfferDto): string {
    return item.id;
  }

  trackByEventId(_: number, item: AkakceListingSyncEventDto): string {
    return item.id;
  }

  eventLevelClass(level: EcScrapeRunEventLevel): string {
    switch (level) {
      case EcScrapeRunEventLevel.Success: return 'level-success';
      case EcScrapeRunEventLevel.Warning: return 'level-warning';
      case EcScrapeRunEventLevel.Error: return 'level-error';
      default: return 'level-info';
    }
  }

  formatTime(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  formatMoney(value?: number | null, currency = 'TRY'): string {
    if (value == null) return '-';
    return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(value);
  }

  private startPolling(scrapeRunId: string): void {
    this.stopPolling();
    this.pollOnce(scrapeRunId);
    this.pollTimer = setInterval(() => this.pollOnce(scrapeRunId), POLL_INTERVAL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private pollOnce(scrapeRunId: string): void {
    this.akakce.getSyncStatus(scrapeRunId, this.latestEventUtc).subscribe({
      next: status => this.applyStatus(status),
      error: err => {
        this.syncError.set(formatAbpHttpError(err));
        this.stopPolling();
      },
    });
  }

  private applyStatus(status: AkakceListingSyncStatusDto): void {
    this.syncStatus.set(status);
    this.appendEvents(status.events ?? []);
    if (status.latestEventUtc) {
      this.latestEventUtc = status.latestEventUtc;
    }
    if (!status.isActive) {
      this.stopPolling();
      this.listingSyncSession.clearRun();
      this.isCancelling.set(false);
      this.load();
    }
  }

  private appendEvents(incoming: AkakceListingSyncEventDto[]): void {
    if (incoming.length === 0) return;
    const seen = new Set(this.events().map(x => x.id));
    const merged = [...this.events()];
    for (const event of incoming) {
      if (!seen.has(event.id)) {
        merged.push(event);
        seen.add(event.id);
      }
    }
    this.events.set(merged.slice(-MAX_EVENTS_KEPT));
    this.pendingScrollToBottom = true;
  }

  bestMerchantDisplay(p: AkakceProductDto): string | null {
    const offers = p.offers ?? [];
    const cheapest = offers.find(o => o.isCheapest) ?? [...offers].sort((a, b) => a.price - b.price)[0];
    const name = cheapest?.merchantName?.trim() || cheapest?.offerTitle?.trim();
    return name || null;
  }

  private sortProducts(items: AkakceProductDto[]): AkakceProductDto[] {
    return [...items].sort((a, b) => (b.discountPercent ?? -1) - (a.discountPercent ?? -1));
  }

  private formatDuration(totalSeconds: number): string {
    const seconds = Math.max(0, Math.round(totalSeconds || 0));
    const minutes = Math.floor(seconds / 60);
    const remaining = seconds % 60;
    if (minutes <= 0) return `${remaining}s`;
    return `${minutes}m ${remaining}s`;
  }
}
