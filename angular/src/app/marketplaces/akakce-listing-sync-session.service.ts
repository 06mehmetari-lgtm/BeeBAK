import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'beebak.akakceListingSyncRun';
const MAX_AGE_MS = 6 * 60 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class AkakceListingSyncSessionService {
  readonly activeRunId = signal<string | null>(null);

  beginRun(scrapeRunId: string): void {
    this.activeRunId.set(scrapeRunId);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ scrapeRunId, startedAt: Date.now() }));
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
      const value = JSON.parse(raw) as { scrapeRunId?: string; startedAt?: number };
      if (!value?.scrapeRunId) {
        this.clearRun();
        return;
      }
      if (value.startedAt != null && Date.now() - value.startedAt > MAX_AGE_MS) {
        this.clearRun();
        return;
      }
      this.activeRunId.set(value.scrapeRunId);
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
