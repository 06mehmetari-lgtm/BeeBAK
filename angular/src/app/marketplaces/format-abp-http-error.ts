import { HttpErrorResponse } from '@angular/common/http';

/**
 * Builds a readable message from an Angular HTTP failure (including ABP remote-service payloads).
 */
export function formatAbpHttpError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error;

    if (body && typeof body === 'object' && !Array.isArray(body)) {
      const obj = body as Record<string, unknown>;

      const extracted = extractBestApiMessage(obj);
      if (extracted) {
        return extracted;
      }

      // Son çare: tam gövde (geliştirici tarayıcıda / kullanıcıda gerçek nedeni görmek için)
      try {
        const raw = JSON.stringify(body);
        if (raw.length >= 20 && raw.length <= 6000) {
          return `${err.status} ${err.statusText}\n${raw}`;
        }
      } catch {
        /* ignore */
      }
    }

    if (typeof body === 'string' && body.trim().length > 0 && body.length < 2000) {
      return `${err.status} ${err.statusText} — ${body.trim()}`;
    }

    const urlHint =
      typeof err.url === 'string' && err.url.trim().length > 0 ? `\nİstek URL: ${err.url}` : '';
    return `${err.status} ${err.statusText}${err.message ? ` (${err.message})` : ''}${urlHint}`.trim();
  }

  if (err instanceof Error) {
    return err.message;
  }

  return String(err);
}

/** ABP, ProblemDetails ve MVC validation gövdelerinden mümkün olan en iyi metni çıkarır. */
function extractBestApiMessage(obj: Record<string, unknown>): string | null {
  // ASP.NET Core ProblemDetails (bazen kökte)
  const pdDetail = obj['detail'];
  if (typeof pdDetail === 'string' && pdDetail.trim()) {
    const title = obj['title'];
    const prefix = typeof title === 'string' && title.trim() ? `${title.trim()}: ` : '';
    return `${prefix}${pdDetail.trim()}`;
  }
  const pdTitle = obj['title'];
  if (typeof pdTitle === 'string' && pdTitle.trim() && pdTitle !== 'One or more validation errors occurred.') {
    return pdTitle.trim();
  }

  // ABP error { code, message, details, validationErrors }
  const errObj = obj['error'];
  if (errObj && typeof errObj === 'object') {
    const e = errObj as Record<string, unknown>;
    const code = e['code'];
    const msg = e['message'];
    const det = e['details'];
    const codeStr = typeof code === 'string' && code.trim() ? `[${code.trim()}] ` : '';

    if (typeof msg === 'string' && msg.trim()) {
      const base = `${codeStr}${msg.trim()}`;
      return appendValidationSegments(base, e['validationErrors']);
    }

    if (typeof det === 'string' && det.trim()) {
      return `${codeStr}${det.trim()}`;
    }
  }

  // MVC model state { errors: { prop: ["msg"] } }
  const errors = obj['errors'];
  if (errors && typeof errors === 'object' && !Array.isArray(errors)) {
    const parts: string[] = [];
    for (const [key, val] of Object.entries(errors)) {
      if (Array.isArray(val)) {
        for (const item of val) {
          if (typeof item === 'string' && item.trim()) {
            parts.push(`${key}: ${item.trim()}`);
          }
        }
      } else if (typeof val === 'string' && val.trim()) {
        parts.push(`${key}: ${val.trim()}`);
      }
    }
    if (parts.length) {
      return parts.join('\n');
    }
  }

  // Bazı proxy katmanları { Message: "..." }
  const legacyMessage = obj['Message'];
  if (typeof legacyMessage === 'string' && legacyMessage.trim()) {
    return legacyMessage.trim();
  }

  const plainMsg = obj['message'];
  if (typeof plainMsg === 'string' && plainMsg.trim()) {
    return plainMsg.trim();
  }

  return null;
}

function appendValidationSegments(base: string, validationErrors: unknown): string {
  if (!validationErrors || !Array.isArray(validationErrors)) {
    return base;
  }

  const parts: string[] = [];
  for (const ve of validationErrors) {
    if (!ve || typeof ve !== 'object') {
      continue;
    }
    const v = ve as Record<string, unknown>;
    const members = v['members'];
    const msg = v['message'];
    const memberStr =
      Array.isArray(members) && members.length ? String(members[0]) : '';
    const msgStr = typeof msg === 'string' ? msg : '';
    if (memberStr || msgStr) {
      parts.push(memberStr ? `${memberStr}: ${msgStr}` : msgStr);
    }
  }

  if (parts.length === 0) {
    return base;
  }

  return `${base}\n${parts.join('\n')}`;
}
