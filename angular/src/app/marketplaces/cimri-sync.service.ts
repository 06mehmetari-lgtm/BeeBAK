import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { EnvironmentService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import {
  CimriListingSyncInput,
  CimriListingSyncResultDto,
  CimriListingSyncStatusDto,
  CimriProductPagedResult,
  GetCimriProductListInput,
} from './marketplace.models';

@Injectable({ providedIn: 'root' })
export class CimriSyncService {
  private readonly http = inject(HttpClient);
  private readonly environment = inject(EnvironmentService);

  private get root(): string {
    return this.environment.getApiUrl('default').replace(/\/+$/, '');
  }

  syncListing(input: CimriListingSyncInput): Observable<CimriListingSyncResultDto> {
    return this.http.post<CimriListingSyncResultDto>(
      `${this.root}/api/app/cimri-listing-sync/sync`,
      input,
    );
  }

  getSyncStatus(
    scrapeRunId: string,
    sinceUtc?: string | null,
  ): Observable<CimriListingSyncStatusDto> {
    let params = new HttpParams();
    if (sinceUtc) {
      params = params.set('sinceUtc', sinceUtc);
    }
    return this.http.get<CimriListingSyncStatusDto>(
      `${this.root}/api/app/cimri-listing-sync/status/${encodeURIComponent(scrapeRunId)}`,
      { params },
    );
  }

  cancelSync(scrapeRunId: string): Observable<CimriListingSyncStatusDto> {
    return this.http.post<CimriListingSyncStatusDto>(
      `${this.root}/api/app/cimri-listing-sync/cancel/${encodeURIComponent(scrapeRunId)}`,
      null,
    );
  }

  getList(input: GetCimriProductListInput): Observable<CimriProductPagedResult> {
    let params = new HttpParams()
      .set('SkipCount', input.skipCount ?? 0)
      .set('MaxResultCount', input.maxResultCount ?? 20)
      .set('IncludeOffers', input.includeOffers ?? true);
    if (input.search) {
      params = params.set('Search', input.search);
    }
    return this.http.get<CimriProductPagedResult>(`${this.root}/api/app/cimri-product`, {
      params,
    });
  }
}
