import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of, Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { AkakceSyncService } from '../akakce-sync.service';
import { CimriSyncService } from '../cimri-sync.service';
import {
  AkakceListingSyncStatusDto,
  CimriListingSyncStatusDto,
  EcScrapeRunEventLevel,
  EcScrapeRunStatus,
} from '../marketplace.models';

const REFRESH_MS = 10_000;
const MAX_EVENTS = 15;

@Component({
  selector: 'app-live-monitor',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './live-monitor.component.html',
  styleUrls: ['./live-monitor.component.scss'],
})
export class LiveMonitorComponent implements OnInit, OnDestroy {
  private readonly cimriSvc = inject(CimriSyncService);
  private readonly akakceSvc = inject(AkakceSyncService);

  readonly levelEnum = EcScrapeRunEventLevel;
  readonly statusEnum = EcScrapeRunStatus;

  readonly cimri = signal<CimriListingSyncStatusDto | null>(null);
  readonly akakce = signal<AkakceListingSyncStatusDto | null>(null);
  readonly lastRefresh = signal<Date | null>(null);
  readonly isRefreshing = signal(false);
  readonly error = signal<string | null>(null);

  readonly cimriActive = computed(() => this.cimri()?.isActive === true);
  readonly akakceActive = computed(() => this.akakce()?.isActive === true);
  readonly anyActive = computed(() => this.cimriActive() || this.akakceActive());

  readonly cimriProgress = computed(() => Math.round((this.cimri()?.progress ?? 0) * 100));
  readonly akakceProgress = computed(() => Math.round((this.akakce()?.progress ?? 0) * 100));

  readonly cimriEvents = computed(() =>
    (this.cimri()?.events ?? []).slice(-MAX_EVENTS).reverse()
  );
  readonly akakceEvents = computed(() =>
    (this.akakce()?.events ?? []).slice(-MAX_EVENTS).reverse()
  );

  private sub?: Subscription;

  ngOnInit(): void {
    this.sub = timer(0, REFRESH_MS)
      .pipe(
        switchMap(() => {
          this.isRefreshing.set(true);
          return forkJoin({
            cimri: this.cimriSvc.getLatest().pipe(catchError(() => of(null))),
            akakce: this.akakceSvc.getLatest().pipe(catchError(() => of(null))),
          });
        }),
      )
      .subscribe({
        next: ({ cimri, akakce }) => {
          this.cimri.set(cimri);
          this.akakce.set(akakce);
          this.lastRefresh.set(new Date());
          this.isRefreshing.set(false);
          this.error.set(null);
        },
        error: () => {
          this.isRefreshing.set(false);
          this.error.set('Veri alınamadı — API erişilemiyor');
        },
      });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  statusLabel(status: EcScrapeRunStatus | undefined): string {
    switch (status) {
      case EcScrapeRunStatus.Running: return 'Çalışıyor';
      case EcScrapeRunStatus.Pending: return 'Bekliyor';
      case EcScrapeRunStatus.Completed: return 'Tamamlandı';
      case EcScrapeRunStatus.Failed: return 'Hata';
      case EcScrapeRunStatus.Cancelled: return 'İptal';
      default: return 'Bilinmiyor';
    }
  }

  statusClass(s: CimriListingSyncStatusDto | AkakceListingSyncStatusDto | null): string {
    if (!s) return '';
    if (s.cancelRequested && s.isActive) return 'cancelling';
    switch (s.status) {
      case EcScrapeRunStatus.Running: return 'running';
      case EcScrapeRunStatus.Completed: return 'completed';
      case EcScrapeRunStatus.Failed: return 'failed';
      case EcScrapeRunStatus.Cancelled: return 'cancelled';
      default: return '';
    }
  }

  eventClass(level: EcScrapeRunEventLevel): string {
    switch (level) {
      case EcScrapeRunEventLevel.Success: return 'evt-ok';
      case EcScrapeRunEventLevel.Warning: return 'evt-warn';
      case EcScrapeRunEventLevel.Error: return 'evt-err';
      default: return 'evt-info';
    }
  }

  eventIcon(level: EcScrapeRunEventLevel): string {
    switch (level) {
      case EcScrapeRunEventLevel.Success: return '✓';
      case EcScrapeRunEventLevel.Warning: return '!';
      case EcScrapeRunEventLevel.Error: return '✕';
      default: return '›';
    }
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
}
