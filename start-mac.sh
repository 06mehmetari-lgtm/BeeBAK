#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
#  BeeBAK — Mac Başlatıcı
#  Kullanım:  chmod +x start-mac.sh && ./start-mac.sh
#
#  Yaptıkları:
#   1. Mac'in yerel IP adresini otomatik bulur
#   2. Docker servisleri (backend, worker, DB, Redis, RabbitMQ, Selenium) başlatır
#   3. Angular'ı 0.0.0.0'a bind eder → telefondan erişilebilir
#   4. Çıkışta geçici dosyaları temizler
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ANGULAR_DIR="$SCRIPT_DIR/angular"
ENV_FILE="$ANGULAR_DIR/src/environments/environment.ts"
ENV_BACKUP="$ANGULAR_DIR/src/environments/environment.ts.mac.bak"
OVERRIDE_FILE="$SCRIPT_DIR/docker-compose.mac-override.yml"

# ── Renk kodları ─────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'

log()  { echo -e "${CYAN}▶${RESET} $*"; }
ok()   { echo -e "${GREEN}✓${RESET} $*"; }
warn() { echo -e "${YELLOW}⚠${RESET} $*"; }
err()  { echo -e "${RED}✗${RESET} $*"; }

# ── Çıkışta temizlik ─────────────────────────────────────────────────────────
cleanup() {
  echo ""
  log "Temizleniyor..."

  # environment.ts geri yükle
  if [ -f "$ENV_BACKUP" ]; then
    mv "$ENV_BACKUP" "$ENV_FILE"
    ok "environment.ts geri yüklendi"
  fi

  # override dosyasını sil
  if [ -f "$OVERRIDE_FILE" ]; then
    rm -f "$OVERRIDE_FILE"
    ok "Docker override dosyası silindi"
  fi

  echo ""
  ok "Temizlik tamamlandı. Docker servisleri hâlâ arka planda çalışıyor."
  echo -e "   Durdurmak için: ${BOLD}docker compose down${RESET}"
}
trap cleanup EXIT INT TERM

# ═══════════════════════════════════════════════════════════════════════════════
echo ""
echo -e "${BOLD}╔══════════════════════════════════════╗${RESET}"
echo -e "${BOLD}║       BeeBAK — Mac Başlatıcı         ║${RESET}"
echo -e "${BOLD}╚══════════════════════════════════════╝${RESET}"
echo ""

# ── 1. Mac yerel IP'sini bul ─────────────────────────────────────────────────
log "Yerel IP adresi tespit ediliyor..."

LOCAL_IP=""
# Wi-Fi (en0) önce dene, sonra Ethernet (en1), sonra diğerleri
for iface in en0 en1 en2 en3 eth0; do
  IP=$(ipconfig getifaddr "$iface" 2>/dev/null || true)
  if [ -n "$IP" ]; then
    LOCAL_IP="$IP"
    ok "Ağ arayüzü: $iface → IP: ${BOLD}$LOCAL_IP${RESET}"
    break
  fi
done

if [ -z "$LOCAL_IP" ]; then
  # Fallback: route tablosundan al
  LOCAL_IP=$(route get default 2>/dev/null | grep interface | awk '{print $2}' | xargs ipconfig getifaddr 2>/dev/null || true)
fi

if [ -z "$LOCAL_IP" ]; then
  warn "Yerel IP bulunamadı, localhost kullanılıyor (telefon erişimi çalışmayabilir)"
  LOCAL_IP="localhost"
fi

# ── 2. Docker çalışıyor mu? ──────────────────────────────────────────────────
log "Docker kontrol ediliyor..."
if ! docker info >/dev/null 2>&1; then
  err "Docker çalışmıyor!"
  echo ""
  echo "  👉 Docker Desktop'ı başlat, menü çubuğundaki balina ikonunun"
  echo "     'Docker Desktop is running' yazana kadar bekle, sonra tekrar dene."
  exit 1
fi
ok "Docker hazır"

# ── 3. Docker compose override oluştur ───────────────────────────────────────
log "Docker yapılandırması hazırlanıyor (IP: $LOCAL_IP)..."

cat > "$OVERRIDE_FILE" <<EOF
# Otomatik oluşturuldu — start-mac.sh tarafından. Silme, çıkışta temizlenir.
services:
  api:
    environment:
      App__SelfUrl: "http://${LOCAL_IP}:44381"
      App__CorsOrigins: "http://localhost:4200,https://localhost:4200,http://${LOCAL_IP}:4200,https://${LOCAL_IP}:4200,"
      App__RedirectAllowedUrls: "http://localhost:4200,http://${LOCAL_IP}:4200,"
      AuthServer__Authority: "http://${LOCAL_IP}:44381"
      AuthServer__RequireHttpsMetadata: "false"
EOF

ok "Docker override dosyası oluşturuldu"

# ── 4. Angular environment.ts güncelle ───────────────────────────────────────
log "Angular ortam dosyası güncelleniyor (IP: $LOCAL_IP)..."

cp "$ENV_FILE" "$ENV_BACKUP"

cat > "$ENV_FILE" <<EOF
import { Environment } from '@abp/ng.core';

const baseUrl = 'http://${LOCAL_IP}:4200';

const oAuthConfig = {
  issuer: 'http://${LOCAL_IP}:44381/',
  redirectUri: baseUrl,
  clientId: 'BeeBAK_App',
  responseType: 'code',
  scope: 'offline_access BeeBAK',
  requireHttps: false,
};

export const environment = {
  production: false,
  localization: {
    defaultResourceName: 'BeeBAK',
  },
  application: {
    baseUrl,
    name: 'BeeBAK',
  },
  oAuthConfig,
  apis: {
    default: {
      url: 'http://${LOCAL_IP}:44381',
      rootNamespace: 'BeeBAK',
    },
    AbpAccountPublic: {
      url: oAuthConfig.issuer,
      rootNamespace: 'AbpAccountPublic',
    },
  },
} as Environment;
EOF

ok "environment.ts güncellendi (${LOCAL_IP}:44381)"

# ── 5. Docker servisleri başlat ───────────────────────────────────────────────
echo ""
log "Docker servisleri başlatılıyor..."
docker compose -f "$SCRIPT_DIR/docker-compose.yml" \
               -f "$OVERRIDE_FILE" \
               up -d

ok "Docker servisleri başlatıldı"

# ── 6. API hazır olana kadar bekle ───────────────────────────────────────────
echo ""
log "API hazır olana kadar bekleniyor (maks 2 dakika)..."
WAITED=0
until curl -fs "http://localhost:44381/.well-known/openid-configuration" >/dev/null 2>&1; do
  sleep 3
  WAITED=$((WAITED + 3))
  if [ $WAITED -ge 120 ]; then
    warn "API 2 dakikada hazır olmadı. Logları kontrol et: docker compose logs api"
    break
  fi
  echo -n "."
done
echo ""
ok "API hazır! (${WAITED}s)"

# ── 7. Node modülleri ─────────────────────────────────────────────────────────
if [ ! -d "$ANGULAR_DIR/node_modules" ]; then
  echo ""
  log "node_modules yok, npm install yapılıyor (ilk seferde ~2 dakika sürer)..."
  (cd "$ANGULAR_DIR" && npm install)
  ok "npm install tamamlandı"
fi

# ── 8. Erişim bilgilerini göster ─────────────────────────────────────────────
echo ""
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
echo -e "  ${GREEN}${BOLD}📱 Telefondan eriş (aynı Wi-Fi ağında olmalısın):${RESET}"
echo -e "     ${BOLD}http://${LOCAL_IP}:4200${RESET}"
echo ""
echo -e "  ${CYAN}${BOLD}💻 Mac'ten eriş:${RESET}"
echo -e "     ${BOLD}http://localhost:4200${RESET}"
echo ""
echo -e "  ${YELLOW}🔧 API (backend):${RESET}"
echo -e "     http://${LOCAL_IP}:44381"
echo ""
echo -e "  ${YELLOW}🐇 RabbitMQ yönetim:${RESET}"
echo -e "     http://localhost:15672  (beebak / beebak)"
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
echo ""
echo -e "  ${YELLOW}⚠  Telefon ve Mac AYNI Wi-Fi ağında olmalı!${RESET}"
echo -e "  Durdurmak için: ${BOLD}Ctrl+C${RESET}"
echo ""

# ── 9. Angular başlat ─────────────────────────────────────────────────────────
log "Angular başlatılıyor (http://0.0.0.0:4200)..."
echo ""

cd "$ANGULAR_DIR"
npx ng serve --host 0.0.0.0 --port 4200 --proxy-config proxy.conf.json
