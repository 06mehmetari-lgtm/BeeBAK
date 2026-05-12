import { EnvironmentService } from '@abp/ng.core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AkakceListingSyncInput,
  AkakceListingSyncResultDto,
  AkakceListingSyncStatusDto,
  AkakceProductPagedResult,
  GetAkakceProductListInput,
} from './marketplace.models';

@Injectable({ providedIn: 'root' })
export class AkakceSyncService {
  private readonly http = inject(HttpClient);
  private readonly environment = inject(EnvironmentService);

  private get root(): string {
    return this.environment.getApiUrl('default').replace(/\/+$/, '');
  }

  syncListing(input: AkakceListingSyncInput): Observable<AkakceListingSyncResultDto> {
    return this.http.post<AkakceListingSyncResultDto>(
      `${this.root}/api/app/akakce-listing-sync/sync`,
      input,
    );
  }

  getSyncStatus(
    scrapeRunId: string,
    sinceUtc?: string | null,
  ): Observable<AkakceListingSyncStatusDto> {
    let params = new HttpParams();
    if (sinceUtc) {
      params = params.set('sinceUtc', sinceUtc);
    }
    return this.http.get<AkakceListingSyncStatusDto>(
      `${this.root}/api/app/akakce-listing-sync/status/${encodeURIComponent(scrapeRunId)}`,
      { params },
    );
  }

  cancelSync(scrapeRunId: string): Observable<AkakceListingSyncStatusDto> {
    return this.http.post<AkakceListingSyncStatusDto>(
      `${this.root}/api/app/akakce-listing-sync/cancel/${encodeURIComponent(scrapeRunId)}`,
      null,
    );
  }

  getLatest(): Observable<AkakceListingSyncStatusDto | null> {
    return this.http.get<AkakceListingSyncStatusDto | null>(
      `${this.root}/api/app/akakce-listing-sync/latest`,
    );
  }

  clearAllStoredData(): Observable<void> {
    return this.http.post<void>(
      `${this.root}/api/app/akakce-product/clear-all-stored-data`,
      {},
    );
  }

  getList(input: GetAkakceProductListInput): Observable<AkakceProductPagedResult> {
    let params = new HttpParams()
      .set('SkipCount', input.skipCount ?? 0)
      .set('MaxResultCount', input.maxResultCount ?? 20)
      .set('IncludeOffers', input.includeOffers ?? true);
    if (input.search) {
      params = params.set('Search', input.search);
    }
    return this.http.get<AkakceProductPagedResult>(`${this.root}/api/app/akakce-product`, {
      params,
    });
  }
}
