import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, of, Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { MonitorService } from './monitor.service';
import {
  AkakceListingSyncStatusDto,
  AllActiveRunsDto,
  CimriListingSyncStatusDto,
  EcScrapeRunEventLevel,
  EcScrapeRunStatus,
  MarketplaceKind,
  TelegramSentItemDto,
} from '../marketplace.models';

const REFRESH_MS = 8_000;
const MAX_EVENTS = 8;

@Component({
  selector: 'app-live-monitor',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './live-monitor.component.html',
  styleUrls: ['./live-monitor.component.scss'],
})
export class LiveMonitorComponent implements OnInit, OnDestroy {
  private readonly monitorSvc = inject(MonitorService);

  readonly levelEnum     = EcScrapeRunEventLevel;
  readonly statusEnum    = EcScrapeRunStatus;
  readonly marketplaceKind = MarketplaceKind;

  readonly data           = signal<AllActiveRunsDto | null>(null);
  readonly lastRefresh    = signal<Date | null>(null);
  readonly isRefreshing   = signal(false);
  readonly error          = signal<string | null>(null);

  readonly cimriRuns  = computed(() => this.data()?.cimriRuns  ?? []);
  readonly akakceRuns = computed(() => this.data()?.akakceRuns ?? []);
  readonly recentSent = computed(() => this.data()?.recentSent ?? []);
  readonly queueSize  = computed(() => this.data()?.telegramQueueSize ?? 0);

  readonly anyActive = computed(() =>
    [...this.cimriRuns(), ...this.akakceRuns()].some(r => r.isActive)
  );

  readonly activeRunCount = computed(() =>
    [...this.cimriRuns(), ...this.akakceRuns()].filter(r => r.isActive).length
  );

  private sub?: Subscription;

  ngOnInit(): void {
    this.sub = timer(0, REFRESH_MS)
      .pipe(
        switchMap(() => {
          this.isRefreshing.set(true);
          return this.monitorSvc.getAllActive().pipe(catchError(() => of(null)));
        }),
      )
      .subscribe({
        next: dto => {
          if (dto) {
            this.data.set(dto);
            this.error.set(null);
          } else {
            this.error.set('API erişilemiyor');
          }
          this.lastRefresh.set(new Date());
          this.isRefreshing.set(false);
        },
        error: () => {
          this.isRefreshing.set(false);
          this.error.set('Veri alınamadı');
        },
      });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  eventsOf(run: CimriListingSyncStatusDto | AkakceListingSyncStatusDto): any[] {
    return (run.events ?? []).slice(-MAX_EVENTS).reverse();
  }

  progress(run: CimriListingSyncStatusDto | AkakceListingSyncStatusDto): number {
    return Math.round((run.progress ?? 0) * 100);
  }

  urlLabel(url?: string | null): string {
    if (!url) return '—';
    try {
      const u = new URL(url);
      return u.pathname + (u.search || '');
    } catch { return url; }
  }

  statusLabel(status: EcScrapeRunStatus | undefined): string {
    switch (status) {
      case EcScrapeRunStatus.Running:   return 'Çalışıyor';
      case EcScrapeRunStatus.Pending:   return 'Bekliyor';
      case EcScrapeRunStatus.Completed: return 'Tamamlandı';
      case EcScrapeRunStatus.Failed:    return 'Hata';
      case EcScrapeRunStatus.Cancelled: return 'İptal';
      default: return '—';
    }
  }

  statusClass(run: CimriListingSyncStatusDto | AkakceListingSyncStatusDto | null): string {
    if (!run) return '';
    switch (run.status) {
      case EcScrapeRunStatus.Running:   return 'running';
      case EcScrapeRunStatus.Completed: return 'completed';
      case EcScrapeRunStatus.Failed:    return 'failed';
      case EcScrapeRunStatus.Cancelled: return 'cancelled';
      default: return '';
    }
  }

  eventClass(level: EcScrapeRunEventLevel): string {
    switch (level) {
      case EcScrapeRunEventLevel.Success: return 'evt-ok';
      case EcScrapeRunEventLevel.Warning: return 'evt-warn';
      case EcScrapeRunEventLevel.Error:   return 'evt-err';
      default: return 'evt-info';
    }
  }

  eventIcon(level: EcScrapeRunEventLevel): string {
    switch (level) {
      case EcScrapeRunEventLevel.Success: return '✓';
      case EcScrapeRunEventLevel.Warning: return '!';
      case EcScrapeRunEventLevel.Error:   return '✕';
      default: return '›';
    }
  }

  triggerLabel(t: string): string {
    switch (t) {
      case 'price_drop':   return '📉 Fiyat Düştü';
      case 'discount_up':  return '🔥 İndirim Arttı';
      case 'new':          return '✨ Yeni';
      default:             return t;
    }
  }

  discountClass(pct?: number | null): string {
    if (!pct) return '';
    if (pct >= 40) return 'disc--hot';
    if (pct >= 20) return 'disc--warm';
    return 'disc--cool';
  }

  formatTime(iso: string): string {
    const d = new Date(iso);
    return isNaN(d.getTime()) ? '' : d.toLocaleTimeString('tr-TR', { hour12: false });
  }

  formatDuration(sec: number): string {
    const s = Math.max(0, Math.round(sec ?? 0));
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const r = s % 60;
    if (h > 0) return `${h}sa ${m.toString().padStart(2, '0')}dk`;
    if (m > 0) return `${m}dk ${r.toString().padStart(2, '0')}sn`;
    return `${r}sn`;
  }

  formatTs(d: Date | null): string {
    if (!d) return '—';
    return d.toLocaleTimeString('tr-TR', { hour12: false });
  }

  formatSentAgo(iso: string): string {
    const diff = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
    if (diff < 60)  return `${diff}sn önce`;
    if (diff < 3600) return `${Math.floor(diff / 60)}dk önce`;
    return `${Math.floor(diff / 3600)}sa önce`;
  }

  formatPrice(price?: number | null): string {
    if (price == null) return '—';
    return price.toLocaleString('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 0 });
  }
}
