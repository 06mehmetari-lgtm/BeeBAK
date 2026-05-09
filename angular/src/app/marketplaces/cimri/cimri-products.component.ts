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
import { CimriSyncService } from '../cimri-sync.service';
import {
  CimriListingSyncEventDto,
  CimriListingSyncInput,
  CimriListingSyncResultDto,
  CimriListingSyncStatusDto,
  CimriProductDto,
  EcScrapeRunEventLevel,
  EcScrapeRunStatus,
} from '../marketplace.models';
import { formatAbpHttpError } from '../format-abp-http-error';

const POLL_INTERVAL_MS = 1500;
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
  readonly loadError = signal<string | null>(null);
  readonly syncError = signal<string | null>(null);
  readonly lastSyncResult = signal<CimriListingSyncResultDto | null>(null);

  readonly maxPagesInput = signal<number | null>(null);
  readonly maxProductsInput = signal<number | null>(null);
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

  ngOnInit(): void {
    this.load();
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
    this.syncStatus.set(null);
    this.events.set([]);
    this.latestEventUtc = null;
    this.autoScroll = true;
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
      this.skipCount.set(0);
      this.load();
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
