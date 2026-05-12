#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
#  BeeBAK — Mac Başlatıcı
#
#  Kullanım:
#    ./start-mac.sh           → İnternet üzerinden erişim (ngrok, her yerden)
#    ./start-mac.sh --local   → Aynı Wi-Fi erişimi (ngrok gerekmez)
#
#  İlk kullanımda (sadece bir kez):
#    1. https://dashboard.ngrok.com/signup  →  ücretsiz kayıt ol
#    2. Dashboard → Your Authtoken → kopyala
#    3. Terminale yapıştır:  ngrok config add-authtoken SENIN_TOKEN
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ANGULAR_DIR="$SCRIPT_DIR/angular"
ENV_FILE="$ANGULAR_DIR/src/environments/environment.ts"
ENV_BACKUP="$ANGULAR_DIR/src/environments/environment.ts.mac.bak"
OVERRIDE_FILE="$SCRIPT_DIR/docker-compose.mac-override.yml"
NGROK_CFG="$SCRIPT_DIR/.ngrok-beebak.yml"
NGROK_PID=""

MODE="public"   # varsayılan: ngrok
[[ "${1:-}" == "--local" ]] && MODE="local"

# ── Renk kodları ─────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'

log()  { echo -e "${CYAN}▶${RESET}  $*"; }
ok()   { echo -e "${GREEN}✓${RESET}  $*"; }
warn() { echo -e "${YELLOW}⚠${RESET}  $*"; }
err()  { echo -e "${RED}✗${RESET}  $*"; exit 1; }
box()  { echo -e "${BOLD}$*${RESET}"; }

# ── Çıkışta temizlik ─────────────────────────────────────────────────────────
cleanup() {
  echo ""
  log "Temizleniyor..."

  [ -n "$NGROK_PID" ] && kill "$NGROK_PID" 2>/dev/null && ok "ngrok durduruldu"

  if [ -f "$ENV_BACKUP" ]; then
    mv "$ENV_BACKUP" "$ENV_FILE"
    ok "environment.ts geri yüklendi"
  fi

  rm -f "$OVERRIDE_FILE" "$NGROK_CFG"
  ok "Geçici dosyalar silindi"
  echo ""
  echo "  Docker servisleri hâlâ arka planda çalışıyor."
  echo -e "  Durdurmak için: ${BOLD}docker compose down${RESET}"
}
trap cleanup EXIT INT TERM

# ═══════════════════════════════════════════════════════════════════════════════
echo ""
box "╔══════════════════════════════════════════╗"
box "║        BeeBAK — Mac Başlatıcı            ║"
box "╚══════════════════════════════════════════╝"
echo ""

# ── Docker hazır mı? ─────────────────────────────────────────────────────────
log "Docker kontrol ediliyor..."
docker info >/dev/null 2>&1 || err "Docker çalışmıyor. Docker Desktop'ı başlat ve tekrar dene."
ok "Docker hazır"

# ═══════════════════════════════════════════════════════════════════════════════
#  LOCAL MOD  (aynı Wi-Fi)
# ═══════════════════════════════════════════════════════════════════════════════
if [ "$MODE" == "local" ]; then

  log "Yerel IP adresi tespit ediliyor..."
  LOCAL_IP=""
  for iface in en0 en1 en2 en3; do
    IP=$(ipconfig getifaddr "$iface" 2>/dev/null || true)
    if [ -n "$IP" ]; then LOCAL_IP="$IP"; ok "IP: ${BOLD}$LOCAL_IP${RESET}  ($iface)"; break; fi
  done
  [ -z "$LOCAL_IP" ] && warn "IP bulunamadı, localhost kullanılıyor." && LOCAL_IP="localhost"

  APP_URL="http://${LOCAL_IP}:4200"
  API_URL="http://${LOCAL_IP}:44381"

else
# ═══════════════════════════════════════════════════════════════════════════════
#  PUBLIC MOD  (ngrok — her yerden erişim)
# ═══════════════════════════════════════════════════════════════════════════════

  # ── ngrok kurulu mu? ───────────────────────────────────────────────────────
  log "ngrok kontrol ediliyor..."
  if ! command -v ngrok &>/dev/null; then
    log "ngrok bulunamadı, Homebrew ile kuruluyor..."
    if ! command -v brew &>/dev/null; then
      err "Homebrew bulunamadı. https://brew.sh adresinden kur, sonra tekrar dene."
    fi
    brew install ngrok/ngrok/ngrok
  fi
  ok "ngrok mevcut: $(ngrok version)"

  # ── Auth token var mı? ─────────────────────────────────────────────────────
  if ! ngrok config check &>/dev/null 2>&1 || \
     ! grep -q "authtoken" "$HOME/.config/ngrok/ngrok.yml" 2>/dev/null && \
     ! grep -q "authtoken" "$HOME/.ngrok2/ngrok.yml" 2>/dev/null; then
    echo ""
    echo -e "${RED}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
    echo -e "${BOLD}  ngrok auth token bulunamadı!${RESET}"
    echo ""
    echo "  Tek seferlik kurulum (ücretsiz):"
    echo "  1. https://dashboard.ngrok.com/signup  →  kayıt ol"
    echo "  2. Dashboard → 'Your Authtoken' → kopyala"
    echo "  3. Şu komutu çalıştır:"
    echo ""
    echo -e "     ${BOLD}ngrok config add-authtoken SENIN_TOKEN${RESET}"
    echo ""
    echo "  Sonra tekrar: ./start-mac.sh"
    echo -e "${RED}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
    exit 1
  fi
  ok "ngrok auth token mevcut"

  # ── ngrok config dosyası oluştur (2 tünel) ────────────────────────────────
  log "ngrok yapılandırılıyor (2 tünel: API + Angular)..."
  cat > "$NGROK_CFG" <<EOF
version: "3"
tunnels:
  beebak-api:
    proto: http
    addr: 44381
    request_header_add:
      - "ngrok-skip-browser-warning: true"
  beebak-angular:
    proto: http
    addr: 4200
    request_header_add:
      - "ngrok-skip-browser-warning: true"
EOF

  # ── Docker başlat (API hazır olsun, ngrok tünel kurmadan önce) ────────────
  log "Docker servisleri başlatılıyor (ngrok tunnel öncesi)..."
  docker compose -f "$SCRIPT_DIR/docker-compose.yml" up -d

  log "API hazır olana kadar bekleniyor..."
  WAITED=0
  until curl -fs "http://localhost:44381/.well-known/openid-configuration" >/dev/null 2>&1; do
    sleep 3; WAITED=$((WAITED+3)); echo -n "."
    [ $WAITED -ge 120 ] && echo "" && warn "API 120s'de hazır olmadı, devam ediliyor..." && break
  done
  echo ""; ok "API hazır!"

  # ── ngrok başlat ──────────────────────────────────────────────────────────
  log "ngrok tünelleri başlatılıyor..."
  ngrok start --all --config "$NGROK_CFG" --log=stdout > /tmp/ngrok-beebak.log 2>&1 &
  NGROK_PID=$!

  log "ngrok URL'leri alınıyor (15 saniye bekleniyor)..."
  sleep 15

  # ngrok local API'den URL'leri oku
  TUNNELS=$(curl -s http://localhost:4040/api/tunnels 2>/dev/null || echo "")
  if [ -z "$TUNNELS" ]; then
    err "ngrok API'ye ulaşılamadı. /tmp/ngrok-beebak.log dosyasını kontrol et."
  fi

  # URL'leri çıkar (jq varsa kullan, yoksa grep/sed)
  if command -v jq &>/dev/null; then
    API_URL=$(echo "$TUNNELS" | jq -r '.tunnels[] | select(.name=="beebak-api") | .public_url' 2>/dev/null || echo "")
    APP_URL=$(echo "$TUNNELS" | jq -r '.tunnels[] | select(.name=="beebak-angular") | .public_url' 2>/dev/null || echo "")
  else
    # jq yoksa: tüm https URL'leri sıraya diz, portlara göre eşleştir
    API_URL=$(echo "$TUNNELS" | grep -o '"public_url":"https://[^"]*"' | grep -v "4200" | head -1 | sed 's/"public_url":"//;s/"//')
    APP_URL=$(echo "$TUNNELS" | grep -o '"public_url":"https://[^"]*"' | tail -1 | sed 's/"public_url":"//;s/"//')
  fi

  # Eğer port eşleştirme tam olmadıysa addr alanından bul
  if [ -z "$API_URL" ] || [ -z "$APP_URL" ]; then
    ALL_URLS=$(echo "$TUNNELS" | grep -o '"public_url":"https://[^"]*"' | sed 's/"public_url":"//g;s/"//g')
    URL1=$(echo "$ALL_URLS" | sed -n '1p')
    URL2=$(echo "$ALL_URLS" | sed -n '2p')
    # addr alanından hangi port hangisi?
    if echo "$TUNNELS" | grep -A2 "\"$URL1\"" | grep -q "44381"; then
      API_URL="$URL1"; APP_URL="$URL2"
    else
      API_URL="$URL2"; APP_URL="$URL1"
    fi
  fi

  [ -z "$API_URL" ] && err "API ngrok URL'si alınamadı. Log: cat /tmp/ngrok-beebak.log"
  [ -z "$APP_URL" ] && err "Angular ngrok URL'si alınamadı. Log: cat /tmp/ngrok-beebak.log"

  ok "API tüneli  → ${BOLD}$API_URL${RESET}"
  ok "App tüneli  → ${BOLD}$APP_URL${RESET}"

fi  # end PUBLIC MODE

# ═══════════════════════════════════════════════════════════════════════════════
#  ORTAK: environment.ts + docker-compose override + Angular
# ═══════════════════════════════════════════════════════════════════════════════

# ── Angular environment.ts güncelle ─────────────────────────────────────────
log "Angular environment.ts güncelleniyor..."
cp "$ENV_FILE" "$ENV_BACKUP"

cat > "$ENV_FILE" <<EOF
import { Environment } from '@abp/ng.core';

const baseUrl = '${APP_URL}';

const oAuthConfig = {
  issuer: '${API_URL}/',
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
      url: '${API_URL}',
      rootNamespace: 'BeeBAK',
    },
    AbpAccountPublic: {
      url: oAuthConfig.issuer,
      rootNamespace: 'AbpAccountPublic',
    },
  },
} as Environment;
EOF
ok "environment.ts güncellendi"

# ── Docker compose override ──────────────────────────────────────────────────
log "Docker CORS/Auth ayarları yapılandırılıyor..."
cat > "$OVERRIDE_FILE" <<EOF
# Otomatik oluşturuldu — start-mac.sh tarafından
services:
  api:
    environment:
      App__SelfUrl: "${API_URL}"
      App__CorsOrigins: "http://localhost:4200,https://localhost:4200,${APP_URL},"
      App__RedirectAllowedUrls: "http://localhost:4200,${APP_URL},"
      AuthServer__Authority: "${API_URL}"
      AuthServer__RequireHttpsMetadata: "false"
EOF

# ── Docker servislerini override ile yeniden başlat ──────────────────────────
log "Docker API servisi yeniden başlatılıyor (yeni CORS ayarları ile)..."
docker compose -f "$SCRIPT_DIR/docker-compose.yml" \
               -f "$OVERRIDE_FILE" \
               up -d api
ok "API yeniden başlatıldı"

# API hazır olana kadar bekle
log "API hazır olana kadar bekleniyor..."
WAITED=0
until curl -fs "${API_URL}/.well-known/openid-configuration" >/dev/null 2>&1 || \
      curl -fs "http://localhost:44381/.well-known/openid-configuration" >/dev/null 2>&1; do
  sleep 3; WAITED=$((WAITED+3)); echo -n "."
  [ $WAITED -ge 60 ] && echo "" && warn "API bekleniyor..." && break
done
echo ""; ok "API hazır!"

# ── node_modules ─────────────────────────────────────────────────────────────
if [ ! -d "$ANGULAR_DIR/node_modules" ]; then
  log "npm install yapılıyor (ilk seferde ~2 dk)..."
  (cd "$ANGULAR_DIR" && npm install)
  ok "npm install tamamlandı"
fi

# ── Erişim bilgilerini göster ────────────────────────────────────────────────
echo ""
box "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [ "$MODE" == "public" ]; then
echo -e "  ${GREEN}${BOLD}📱 Telefon / Her Yerden Erişim:${RESET}"
echo -e "     ${BOLD}${APP_URL}${RESET}"
echo ""
echo -e "  ${YELLOW}🔧 API (ngrok):${RESET}"
echo -e "     ${API_URL}"
else
echo -e "  ${GREEN}${BOLD}📱 Telefon (Aynı Wi-Fi):${RESET}"
echo -e "     ${BOLD}${APP_URL}${RESET}"
echo ""
echo -e "  ${YELLOW}🔧 API:${RESET}"
echo -e "     ${API_URL}"
fi
echo ""
echo -e "  ${CYAN}${BOLD}💻 Mac'ten:${RESET}  http://localhost:4200"
echo -e "  ${YELLOW}🐇 RabbitMQ:${RESET} http://localhost:15672  (beebak/beebak)"
box "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
[ "$MODE" == "public" ] && echo -e "  ${YELLOW}⚠  URL'ler ngrok yeniden başlayınca değişir!${RESET}"
echo -e "  Durdurmak için: ${BOLD}Ctrl+C${RESET}"
echo ""

# ── Angular başlat ───────────────────────────────────────────────────────────
log "Angular başlatılıyor..."
echo ""
cd "$ANGULAR_DIR"
npx ng serve --host 0.0.0.0 --port 4200 --proxy-config proxy.conf.json
