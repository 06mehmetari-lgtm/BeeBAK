import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'beebak.cimriListingSyncRun';
const MAX_AGE_MS = 6 * 60 * 60 * 1000;

/**
 * Kuyruğa alınan Cimri liste senkronunun `scrapeRunId`sini saklar (sekme / yenileme arası).
 */
@Injectable({ providedIn: 'root' })
export class CimriListingSyncSessionService {
  readonly activeRunId = signal<string | null>(null);

  beginRun(scrapeRunId: string): void {
    this.activeRunId.set(scrapeRunId);
    try {
      localStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({ scrapeRunId, startedAt: Date.now() }),
      );
    } catch {
      /* private mode */
    }
  }

  hydrateFromStorage(): void {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) {
        return;
      }
      const o = JSON.parse(raw) as { scrapeRunId?: string; startedAt?: number };
      if (!o?.scrapeRunId) {
        this.clearRun();
        return;
      }
      if (o.startedAt != null && Date.now() - o.startedAt > MAX_AGE_MS) {
        this.clearRun();
        return;
      }
      this.activeRunId.set(o.scrapeRunId);
    } catch {
      this.clearRun();
    }
  }

  clearRun(): void {
    this.activeRunId.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* ignore */
    }
  }
}
