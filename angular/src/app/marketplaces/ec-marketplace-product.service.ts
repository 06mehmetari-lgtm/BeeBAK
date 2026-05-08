import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  EcMarketplaceProductPagedResult,
  MarketplaceKind,
} from './marketplace.models';

@Injectable({ providedIn: 'root' })
export class EcMarketplaceProductService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/app/ec-marketplace-product';

  getList(input: {
    marketplace: MarketplaceKind;
    skipCount: number;
    maxResultCount: number;
  }): Observable<EcMarketplaceProductPagedResult> {
    const params = new HttpParams()
      .set('Marketplace', input.marketplace)
      .set('SkipCount', input.skipCount)
      .set('MaxResultCount', input.maxResultCount);

    return this.http.get<EcMarketplaceProductPagedResult>(this.baseUrl, {
      params,
    });
  }
}
