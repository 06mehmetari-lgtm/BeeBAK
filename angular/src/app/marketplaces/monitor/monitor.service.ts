import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EnvironmentService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { AllActiveRunsDto } from '../marketplace.models';

@Injectable({ providedIn: 'root' })
export class MonitorService {
  private readonly http = inject(HttpClient);
  private readonly environment = inject(EnvironmentService);

  private get root(): string {
    return this.environment.getApiUrl('default').replace(/\/+$/, '');
  }

  getAllActive(): Observable<AllActiveRunsDto> {
    return this.http.get<AllActiveRunsDto>(`${this.root}/api/app/monitor/all-active`);
  }
}
