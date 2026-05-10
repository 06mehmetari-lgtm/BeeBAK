# BeeBAK — Kubernetes deployment

Bu klasör BeeBAK'ı Docker Desktop'un dahili Kubernetes'inde veya başka bir cluster'da
ayağa kaldırmak için minimum manifest setini içerir.

## Bileşenler

| Manifest | Servis |
|---|---|
| `00-namespace.yaml` | `beebak` namespace |
| `10-postgres.yaml` | StatefulSet + Service (Postgres 16) |
| `11-redis.yaml` | Redis 7 (cache + dedup + distributed lock) |
| `12-rabbitmq.yaml` | RabbitMQ 3.13 (background-jobs queue) |
| `13-selenium-grid.yaml` | Selenium Hub + 5 Chrome node + HPA (3→30) |
| `20-secrets.yaml` | Şifreler / connection string'ler |
| `30-dbmigrator-job.yaml` | DB şemasını uygulayan one-shot Job |
| `40-api.yaml` | `BeeBAK.HttpApi.Host` (ABP API) + HPA |
| `41-worker.yaml` | `BeeBAK.WorkerHost` (queue tüketici) + HPA |
| `50-ingress.yaml` | `beebak.local` host'u için nginx ingress |

## İlk kurulum (Docker Desktop / minikube / kind)

```powershell
# 1) Image'ları local registry'e build et
docker build -t beebak/api:latest        -f Dockerfile.api        .
docker build -t beebak/worker:latest     -f Dockerfile.worker     .
docker build -t beebak/dbmigrator:latest -f Dockerfile.dbmigrator .

# 2) Cluster'a uygula
kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/20-secrets.yaml
kubectl apply -f k8s/10-postgres.yaml
kubectl apply -f k8s/11-redis.yaml
kubectl apply -f k8s/12-rabbitmq.yaml
kubectl apply -f k8s/13-selenium-grid.yaml

# 3) Postgres up olur olmaz schema'yı kurmak için Job
kubectl apply -f k8s/30-dbmigrator-job.yaml
kubectl wait --for=condition=complete --timeout=300s job/beebak-dbmigrator -n beebak

# 4) Uygulama servisleri
kubectl apply -f k8s/40-api.yaml
kubectl apply -f k8s/41-worker.yaml
kubectl apply -f k8s/50-ingress.yaml
```

API'ye `http://beebak.local` (ingress) veya `kubectl port-forward svc/beebak-api 44381:80 -n beebak`
ile erişebilirsiniz.

## Sıkça yapılan işlemler

```powershell
# Worker pod'larını manuel ölçekle
kubectl scale deploy/beebak-worker --replicas=10 -n beebak

# Selenium grid'i manuel büyüt (HPA da gerektiğinde otomatik büyütüyor)
kubectl scale deploy/chrome-node --replicas=15 -n beebak

# Queue durumu (RabbitMQ management UI)
kubectl port-forward svc/rabbitmq 15672:15672 -n beebak
# tarayıcı: http://localhost:15672  (kullanıcı: beebak / şifre: beebak)
```



## PC de zaten vardı PC yeniden başlattım ne yapcam

```powershell
# Worker pod'larını manuel ölçekle
1. Proje dizinine geç

cd C:\Users\$env:USERNAME\source\repos\BeeBAK\BeeBAK
2. Image’ları build et

docker build -t beebak/api:latest        -f Dockerfile.api        .
docker build -t beebak/worker:latest     -f Dockerfile.worker     .
docker build -t beebak/dbmigrator:latest -f Dockerfile.dbmigrator .

3. Namespace, secret ve altyapı (Postgres, Redis, RabbitMQ, Selenium)

kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/20-secrets.yaml
kubectl apply -f k8s/10-postgres.yaml
kubectl apply -f k8s/11-redis.yaml
kubectl apply -f k8s/12-rabbitmq.yaml
kubectl apply -f k8s/13-selenium-grid.yaml

4. DB migrator job ve tamamlanmasını bekle

kubectl apply -f k8s/30-dbmigrator-job.yaml
kubectl wait --for=condition=complete --timeout=300s job/beebak-dbmigrator -n beebak
5. API, worker ve ingress

kubectl apply -f k8s/40-api.yaml
kubectl apply -f k8s/41-worker.yaml
kubectl apply -f k8s/50-ingress.yaml
6. (İsteğe bağlı) Erişim — README’deki gibi API için port-forward


kubectl port-forward svc/beebak-api 44381:80 -n beebak
```




