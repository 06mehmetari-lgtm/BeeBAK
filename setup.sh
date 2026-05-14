#!/bin/bash
# ╔══════════════════════════════════════════════════════════════╗
# ║           BeeBAK — Sunucu Kurulum Scripti                   ║
# ║   Hetzner CX33 / Ubuntu 24.04 için hazırlanmıştır          ║
# ║   Kullanım: sudo bash setup.sh                              ║
# ╚══════════════════════════════════════════════════════════════╝

set -euo pipefail

# ── Renkli çıktı ─────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

step()  { echo -e "\n${CYAN}${BOLD}━━━ $1 ${NC}"; }
ok()    { echo -e "${GREEN}  ✓${NC} $1"; }
warn()  { echo -e "${YELLOW}  !${NC} $1"; }
die()   { echo -e "\n${RED}  ✗ HATA: $1${NC}\n"; exit 1; }

REPO_URL="https://github.com/06mehmetari-lgtm/BeeBAK.git"
INSTALL_DIR="/opt/beebak"

# ── Başlık ───────────────────────────────────────────────────────
clear
echo -e "${BOLD}"
echo "  ██████╗ ███████╗███████╗██████╗  █████╗ ██╗  ██╗"
echo "  ██╔══██╗██╔════╝██╔════╝██╔══██╗██╔══██╗██║ ██╔╝"
echo "  ██████╔╝█████╗  █████╗  ██████╔╝███████║█████╔╝ "
echo "  ██╔══██╗██╔══╝  ██╔══╝  ██╔══██╗██╔══██║██╔═██╗ "
echo "  ██████╔╝███████╗███████╗██████╔╝██║  ██║██║  ██╗"
echo "  ╚═════╝ ╚══════╝╚══════╝╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝"
echo -e "${NC}"
echo -e "  ${CYAN}Hetzner CX33 Otomatik Kurulum${NC}"
echo -e "  ${YELLOW}Bu işlem 10–20 dakika sürebilir, lütfen bekleyin.${NC}"
echo ""

# ── Root kontrolü ────────────────────────────────────────────────
[[ $EUID -ne 0 ]] && die "Bu script root olarak çalıştırılmalıdır.\n  Kullanım: sudo bash setup.sh"

# ── Adım 1: Sistem güncelleme ────────────────────────────────────
step "[1/7] Sistem güncelleniyor..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get upgrade -y -qq \
  -o Dpkg::Options::="--force-confdef" \
  -o Dpkg::Options::="--force-confold"
ok "Sistem güncellendi."

# ── Adım 2: Gerekli araçlar ──────────────────────────────────────
step "[2/7] Gerekli araçlar kuruluyor..."
apt-get install -y -qq \
  curl \
  git \
  ca-certificates \
  gnupg \
  lsb-release \
  htop \
  ufw
ok "Araçlar kuruldu."

# ── Adım 3: Docker kurulum ───────────────────────────────────────
step "[3/7] Docker kuruluyor..."
if command -v docker &>/dev/null; then
  warn "Docker zaten kurulu: $(docker --version)"
else
  curl -fsSL https://get.docker.com | sh
  ok "Docker kuruldu: $(docker --version)"
fi

# Docker servisini başlat ve açılışa ekle
systemctl enable docker --quiet
systemctl start docker
ok "Docker servisi etkin ve çalışıyor."

# Docker Compose kontrolü
if ! docker compose version &>/dev/null; then
  die "Docker Compose bulunamadı. Docker kurulumu başarısız olmuş olabilir."
fi
ok "Docker Compose: $(docker compose version --short)"

# ── Adım 4: Güvenlik duvarı ──────────────────────────────────────
step "[4/7] Güvenlik duvarı yapılandırılıyor..."
# SSH portunu aç (22/tcp) — bağlantıyı kesmez
ufw allow 22/tcp    > /dev/null 2>&1 || true
# RabbitMQ management (opsiyonel — gerekirse aç)
# ufw allow 15672/tcp > /dev/null 2>&1
# API portu (gerekirse aç)
# ufw allow 44381/tcp > /dev/null 2>&1
ufw --force enable  > /dev/null 2>&1 || true
ok "Güvenlik duvarı: SSH (22) açık, gereksiz portlar kapalı."

# ── Adım 5: Repo klonlama ────────────────────────────────────────
step "[5/7] BeeBAK kaynak kodu indiriliyor..."
if [[ -d "$INSTALL_DIR/.git" ]]; then
  warn "Mevcut kurulum bulundu — güncelleniyor..."
  cd "$INSTALL_DIR"
  git pull --ff-only
  ok "Repo güncellendi: $(git log -1 --format='%h %s')"
else
  mkdir -p /opt
  git clone "$REPO_URL" "$INSTALL_DIR"
  cd "$INSTALL_DIR"
  ok "Repo indirildi: $(git log -1 --format='%h %s')"
fi

# ── Adım 6: Docker image build ───────────────────────────────────
step "[6/7] Docker image'lar build ediliyor (10-15 dakika)..."
warn "Bu adım uzun sürebilir. Lütfen terminal'i kapatmayın."
echo ""

cd "$INSTALL_DIR"
DOCKER_BUILDKIT=1 docker compose build \
  --parallel \
  2>&1 | while IFS= read -r line; do
    # Build progress'i filtrele — sadece önemli satırları göster
    if echo "$line" | grep -qE "^#[0-9]+ (DONE|ERROR|naming|unpacking|exporting)"; then
      echo -e "  ${CYAN}${line}${NC}"
    elif echo "$line" | grep -qE "^Step|Successfully built|Error"; then
      echo -e "  ${line}"
    fi
  done

ok "Tüm image'lar build edildi."

# ── Adım 7: Servisleri başlat ────────────────────────────────────
step "[7/7] Servisler başlatılıyor..."
cd "$INSTALL_DIR"
docker compose up -d
ok "Tüm servisler başlatıldı."

# ── Sağlık kontrolü ──────────────────────────────────────────────
echo ""
echo -e "${YELLOW}  Servisler hazırlanıyor — sağlık kontrolü başlıyor...${NC}"

MAX_WAIT=180   # maks 3 dakika bekle
INTERVAL=10
WAITED=0
ALL_HEALTHY=false

while [[ $WAITED -lt $MAX_WAIT ]]; do
  sleep $INTERVAL
  WAITED=$((WAITED + INTERVAL))

  # Unhealthy container sayısını bul
  UNHEALTHY=$(docker compose ps --format '{{.Health}}' 2>/dev/null | grep -c "unhealthy" || true)
  NOT_RUNNING=$(docker compose ps --format '{{.State}}' 2>/dev/null | grep -vc "running\|exited" || true)

  if [[ $UNHEALTHY -eq 0 && $NOT_RUNNING -eq 0 ]]; then
    ALL_HEALTHY=true
    break
  fi

  echo -e "  ${YELLOW}Bekleniyor... (${WAITED}s / ${MAX_WAIT}s)${NC}"
done

# ── Sonuç ────────────────────────────────────────────────────────
echo ""
echo -e "${BOLD}════════════════════════════════════════════════════${NC}"

if [[ "$ALL_HEALTHY" == "true" ]]; then
  echo -e "${GREEN}${BOLD}  ✅ BeeBAK başarıyla kuruldu ve çalışıyor!${NC}"
else
  echo -e "${YELLOW}${BOLD}  ⚠️  Kurulum tamamlandı (bazı servisler hâlâ başlıyor)${NC}"
fi

echo -e "${BOLD}════════════════════════════════════════════════════${NC}"
echo ""

# Container durumlarını göster
docker compose -f "$INSTALL_DIR/docker-compose.yml" ps
echo ""

# IP adresi
SERVER_IP=$(hostname -I | awk '{print $1}')

echo -e "${BOLD}━━━ Faydalı Bilgiler ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "  ${CYAN}Sunucu IP:${NC}       $SERVER_IP"
echo -e "  ${CYAN}Kurulum dizini:${NC}  $INSTALL_DIR"
echo ""
echo -e "${BOLD}━━━ Faydalı Komutlar ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo -e "  ${YELLOW}# Worker loglarını izle (Telegram gönderimlerini görmek için):${NC}"
echo -e "  docker compose -f $INSTALL_DIR/docker-compose.yml logs -f worker"
echo ""
echo -e "  ${YELLOW}# Tüm servis durumlarını gör:${NC}"
echo -e "  docker compose -f $INSTALL_DIR/docker-compose.yml ps"
echo ""
echo -e "  ${YELLOW}# Sistem kaynak kullanımı:${NC}"
echo -e "  docker stats"
echo ""
echo -e "  ${YELLOW}# Sistemi güncelle (yeni kod çekip yeniden build et):${NC}"
echo -e "  cd $INSTALL_DIR && git pull && docker compose build && docker compose up -d"
echo ""
echo -e "  ${YELLOW}# Sistemi durdur:${NC}"
echo -e "  docker compose -f $INSTALL_DIR/docker-compose.yml down"
echo ""
echo -e "${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo -e "  ${GREEN}Sunucu yeniden başlatılsa bile sistem otomatik devam eder.${NC}"
echo -e "  ${GREEN}Telegram kanalından takip edebilirsiniz. 🚀${NC}"
echo ""
