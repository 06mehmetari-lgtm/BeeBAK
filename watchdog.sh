#!/bin/bash
# BeeBAK Watchdog — Sistem takıldığında otomatik yeniden başlatır
# Kurulum: crontab -e → */5 * * * * /opt/beebak/watchdog.sh

COMPOSE_DIR="/opt/beebak"
LOG_FILE="/opt/beebak/watchdog.log"
MAX_LOG_LINES=5000   # Log dosyası bu satırı geçerse eski yarısı silinir
STUCK_MINUTES=8      # Bu kadar dakika log yoksa → takılı say

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"
}

trim_log() {
    if [ -f "$LOG_FILE" ]; then
        local lines
        lines=$(wc -l < "$LOG_FILE")
        if [ "$lines" -gt "$MAX_LOG_LINES" ]; then
            tail -n $((MAX_LOG_LINES / 2)) "$LOG_FILE" > "${LOG_FILE}.tmp" && mv "${LOG_FILE}.tmp" "$LOG_FILE"
        fi
    fi
}

cd "$COMPOSE_DIR" || { echo "HATA: $COMPOSE_DIR bulunamadı"; exit 1; }

trim_log

# ── 1) Kritik container'lar ayakta mı? ────────────────────────────────────────
for SVC in postgres redis rabbitmq worker; do
    STATUS=$(docker compose ps --format "{{.State}}" "$SVC" 2>/dev/null | head -1)
    if [ "$STATUS" != "running" ]; then
        log "[$SVC] durdu (status='$STATUS') → docker compose up -d başlatılıyor"
        docker compose up -d >> "$LOG_FILE" 2>&1
        log "[$SVC] yeniden başlatma tamamlandı"
        exit 0
    fi
done

# ── 2) Worker takılı mı? (son N dakikada hiç log yok mu?) ────────────────────
SINCE="${STUCK_MINUTES}m"
RECENT=$(docker compose logs --since="$SINCE" worker 2>/dev/null | wc -l)

if [ "$RECENT" -lt 3 ]; then
    log "Worker ${STUCK_MINUTES} dakikadır sessiz (log satırı: $RECENT) → yeniden başlatılıyor"
    docker compose restart worker >> "$LOG_FILE" 2>&1
    log "Worker yeniden başlatıldı"
    exit 0
fi

# ── 3) RabbitMQ bağlantısı var mı? ───────────────────────────────────────────
RMQ_OK=$(docker exec beebak-rabbitmq rabbitmq-diagnostics -q ping 2>/dev/null | grep -c "Ping succeeded")
if [ "$RMQ_OK" -lt 1 ]; then
    log "RabbitMQ ping başarısız → rabbitmq + worker yeniden başlatılıyor"
    docker compose restart rabbitmq >> "$LOG_FILE" 2>&1
    sleep 15
    docker compose restart worker >> "$LOG_FILE" 2>&1
    log "RabbitMQ + Worker yeniden başlatıldı"
    exit 0
fi

# ── 4) Her şey sağlıklı ───────────────────────────────────────────────────────
log "Sistem sağlıklı — worker=$RECENT log/$SINCE, tüm container'lar running"
