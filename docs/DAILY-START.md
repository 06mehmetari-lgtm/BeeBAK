# BeeBAK — Günlük Başlatma Rehberi

> Bu doküman **her şey kurulu** bir bilgisayarda (Docker Desktop, Node 22, Yarn, Git, repo klonlanmış, `yarn install` ve `docker compose build` daha önce yapılmış) sistemi her açılışta nasıl ayağa kaldıracağını anlatır.
> Sıfırdan kurulum için: [`SETUP-WINDOWS.md`](SETUP-WINDOWS.md)

---

## Hızlı Başlatma (3 adım)

### 1) Docker Desktop'ı aç

```powershell
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
# Tray ikonu yeşil "Engine running" olana kadar bekle (~20-30 sn)
```

> Docker Desktop "General" ayarlarında **"Start Docker Desktop when you sign in"** işaretliyse bu adım otomatik yapılır.

### 2) Backend stack'i kaldır (Postgres + Redis + RabbitMQ + Selenium + API + Worker)

```powershell
cd C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK
docker compose up -d
```

Bu komut zaten build edilmiş image'ları kullanır, ~10-20 sn'de hepsi ayağa kalkar.

Sağlık kontrolü:

```powershell
docker compose ps
# Tüm satırlar Up (healthy) veya Up; dbmigrator Exited (0) olmalı
```

### 3) Angular dev server'ı başlat (ayrı PowerShell penceresinde)

```powershell
cd C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK\angular
npm install -g yarn
yarn install
yarn install
# İlk derleme ~30-60 sn; "Local: http://localhost:4200" yazınca hazır
```

---

## Tek-Tık Alternatifi (opsiyonel)

Aşağıdaki `start-beebak.ps1` script'ini repo köküne (veya istediğin bir konuma) kaydet, ardından masaüstü kısayolu yarat. Tek tıkla 3 adımı tamamlar.

```powershell
# start-beebak.ps1
$ErrorActionPreference = "Stop"
$root = "C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK"

Write-Host "Docker Desktop başlatılıyor..." -ForegroundColor Cyan
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"

# Docker engine hazır olana kadar bekle
do {
    Start-Sleep -Seconds 3
    $ok = (docker info 2>$null) -ne $null
} until ($ok)

Set-Location $root
Write-Host "Backend stack ayağa kaldırılıyor..." -ForegroundColor Cyan
docker compose up -d

Write-Host "Angular dev server yeni pencerede açılıyor..." -ForegroundColor Cyan
Start-Process pwsh -ArgumentList "-NoExit","-Command","cd '$root\angular'; yarn start"

Write-Host "Hazır → http://localhost:4200 (admin / 1q2w3E*)" -ForegroundColor Green
```

Masaüstü kısayolu (1 sefer):

```powershell
$repoRoot = "C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK"
$WshShell = New-Object -ComObject WScript.Shell
$lnk = $WshShell.CreateShortcut("$env:USERPROFILE\Desktop\BeeBAK Start.lnk")
$lnk.TargetPath = "pwsh.exe"
$lnk.Arguments  = "-NoProfile -ExecutionPolicy Bypass -File `"$repoRoot\start-beebak.ps1`""
$lnk.Save()
```

---

## Açılışta Otomatik Başlatma (Task Scheduler — opsiyonel)

Logon olunca otomatik tüm stack'in ayağa kalkmasını istiyorsan:

```powershell
$repoRoot = "C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK"
$action  = New-ScheduledTaskAction -Execute "pwsh.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$repoRoot\start-beebak.ps1`""
$trigger = New-ScheduledTaskTrigger -AtLogOn
Register-ScheduledTask -TaskName "BeeBAK AutoStart" -Action $action -Trigger $trigger -RunLevel Highest
```

---

## Ayakta Olması Gereken Local URL'ler

| Servis | URL | Kullanıcı / Şifre | Container | Port |
|---|---|---|---|---|
| **Angular UI** | http://localhost:4200 | `admin` / `1q2w3E*` | host (yarn) | 4200 |
| **API** (REST) | http://localhost:44381 | (Bearer token) | `beebak-api` | 44381 |
| **API Swagger** | http://localhost:44381/swagger | — | `beebak-api` | 44381 |
| **API Health** | http://localhost:44381/health-status | — | `beebak-api` | 44381 |
| **RabbitMQ Management** | http://localhost:15672 | `beebak` / `beebak` | `beebak-rabbitmq` | 15672 |
| **Selenium Grid UI** | http://localhost:4444/ui/ | — | `beebak-selenium-hub` | 4444 |
| **PostgreSQL** | localhost:5432 (db: `BeeBAK`) | `postgres` / `11051994` | `beebak-postgres` | 5432 |
| **Redis** | localhost:6379 | (şifresiz) | `beebak-redis` | 6379 |

Hepsi yeşil mi diye tek bakışta görmek:

```powershell
docker compose ps
# Beklenen: postgres, redis, rabbitmq, selenium-hub, chrome-node-1..4,
#           api hepsinde "Up (healthy)" / "Up";
#           dbmigrator "Exited (0)";
#           worker-1, worker-2 "Up"
```

Tarayıcıdan smoke-test:

- http://localhost:4444/ui/ → 4 chrome node "Idle" görünmeli
- http://localhost:15672 → login sonrası "Queues" sekmesinde `BackgroundJobQueue` consumer'ı 2 olmalı
- http://localhost:44381/health-status → JSON `Healthy` dönmeli
- http://localhost:4200 → admin / 1q2w3E\* ile login → ana sayfa

---

## Durdurma / Yönetim

```powershell
# Tüm sistemi kapat (data kalır)
docker compose down

# Angular dev server'ı durdur: ilgili PowerShell'de Ctrl+C

# Sadece bir servisi yeniden başlat
docker compose restart api
docker compose restart worker

# Logları izle
docker compose logs -f api worker

# Sıfırdan başla (DB volume da silinir)
docker compose down -v
```

---

## Sorun Giderme

| Sorun | Çözüm |
|---|---|
| `docker: command not found` | Docker Desktop tray'de yeşil değil; bekle veya manuel aç |
| Port 5432 / 6379 / 15672 / 44381 / 4200 dolu | Çakışan local servisi (IIS, başka Postgres vb.) durdur |
| Angular login fail | `docker compose ps` → API `Up (healthy)` mi? `dbmigrator` `Exited (0)` mi? |
| Worker scrape başlatınca cevap yok | RabbitMQ UI → Queues → `BackgroundJobQueue` → Consumers >= 1 mi? |
| Selenium "no available node" | http://localhost:4444/ui/ → 4 node `Idle` mi? Değilse `docker compose restart chrome-node` |
| Yeniden build sonrası eski kod çalışıyor | `docker compose up -d --no-deps --force-recreate api worker` |
| `yarn` bulunamıyor | `corepack enable` + PowerShell'i yeniden aç |

---

## Cheat Sheet

```powershell
# AYAĞA KALDIR (her açılışta)
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
cd C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK
docker compose up -d
cd angular; yarn start

# DURDURMA
docker compose down
# (Angular için Ctrl+C)
```

Tarayıcı: http://localhost:4200 → `admin` / `1q2w3E*`
