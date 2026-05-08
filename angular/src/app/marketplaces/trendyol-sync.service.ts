import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  TrendyolListingSyncInput,
  TrendyolListingSyncResultDto,
} from './marketplace.models';

@Injectable({ providedIn: 'root' })
export class TrendyolSyncService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/app/trendyol-listing-sync/sync';

  sync(input: TrendyolListingSyncInput): Observable<TrendyolListingSyncResultDto> {
    return this.http.post<TrendyolListingSyncResultDto>(this.url, input);
  }
}
