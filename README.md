# MojTermin — Ops i Deploy

Ovaj dokument pokriva produkcijski deploy za:
- `MojTermin.Api` (ASP.NET Core Web API, `net8.0`)
- `mojtermin-web` (Angular aplikacija)

## 1) Production pre-check

- .NET SDK 8+ instaliran
- Node.js 20+ i npm instalirani
- SQL Server instanca dostupna
- DNS i TLS certifikat spremni (npr. `api.mojtermin.ai`, `app.mojtermin.ai`)

## 2) API konfiguracija (production)

Podešavaj kroz environment varijable ili `appsettings.Production.json`.

Obavezno:
- `ConnectionStrings__DefaultConnection`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__SecretKey` (min 32 chars, ne default vrijednost)
- `Auth__AllowPublicRegistration=false` (javna registracija isključena — sajt čita isti flag preko `GET /api/public/site-config`)
- `Cors__AllowedOrigins__0=https://app.mojtermin.ai`
- `Cors__AllowedOrigins__1=https://www.app.mojtermin.ai` (opcionalno)

Napomena: aplikacija sada fail-a na startupu u production ako je JWT secret slab/prazan/default.

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 48
$rng.GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

Output proslijedi u env-var `Jwt__SecretKey` (npr. App Service Configuration → Application settings).

Demo seed (`demo-salon` business i `owner` user) više se NE kreira u produkciji.

Production tenant (ručni onboarding, preporučeno uz `Auth__AllowPublicRegistration=false`):

- dogovor i račun/virman s klijentom, zatim privremeno `Auth__AllowPublicRegistration=true` → `POST /api/businesses/register` (ili forma na webu) → nakon kreiranja tenant-a vrati `false`; ili
- isti POST iz sigurnog okruženja dok je flag kratko uključen.

## 3) EF migracije (production DB)

Iz root foldera rješenja:

```powershell
dotnet ef database update `
  --project ".\MojTermin.Api\MojTermin.Api.csproj" `
  --startup-project ".\MojTermin.Api\MojTermin.Api.csproj" `
  --configuration Release
```

## 4) API publish

```powershell
dotnet publish ".\MojTermin.Api\MojTermin.Api.csproj" `
  -c Release `
  -o ".\artifacts\api"
```

`artifacts/api` deployaj na server (IIS/Kestrel+reverse proxy/Docker).

## 5) Frontend build i deploy

`mojtermin-web/src/environments/environment.prod.ts` treba pokazivati na produkcijski API:
- `apiBaseUrl: 'https://api.mojtermin.ai/api'`
- `contactSalesPhone` — broj za **ponudu / demo** kad je javna registracija isključena (prikaz i `tel:` link na početnoj i `/register-business`). Stavi svoj mobilni/fiksni prije builda.

Build:

```powershell
cd ".\mojtermin-web"
npm ci
npm run build
```

Output:
- `mojtermin-web/dist/mojtermin-web/browser`

Taj folder deployaj kao statički sajt (Nginx/IIS/Cloud static hosting).

## 6) Smoke test nakon deploya

Provjeri redom:
- `GET https://api.mojtermin.ai/health` -> `200`
- `GET https://api.mojtermin.ai/swagger` (ako je izloženo u tom env-u)
- Public stranica radi: `/b/demo-salon`
- Public booking radi za validan slot
- Admin login radi
- Services/Clients/Appointments CRUD radi
- 401 sa isteklim access tokenom radi refresh i nastavlja request

## 7) Brzi rollback plan

- API: vrati prethodni deploy package i restart servisa
- DB: ne rollback migracije automatski bez plana; radi forward-fix
- Frontend: vrati prethodni static build artefact

## 8) Operativne napomene

- Seed podaci se kreiraju ako je baza prazna.
- Endpoint za health check: `/health`
- CORS u production dolazi iz `Cors:AllowedOrigins`; bez konfiguracije neće dozvoliti cross-origin promet.

## 9) Brzi go-live smoke test (skripta)

Nakon deploya možeš pokrenuti:

```powershell
.\scripts\smoke-test.ps1 `
  -ApiBaseUrl "https://api.mojtermin.ai" `
  -FrontendBaseUrl "https://app.mojtermin.ai" `
  -BusinessSlug "demo-salon"
```

Skripta provjerava:
- API health
- public business/profile endpoint
- public services endpoint
- public working-hours endpoint
- frontend business stranicu

## 10) One-click pre-release check

Pokreće redom:
- `dotnet build` (Release)
- `dotnet test` (Release)
- `npm run build` (frontend)
- smoke test (ako su URL-ovi zadani)

```powershell
.\scripts\pre-release.ps1 `
  -ApiBaseUrl "https://api.mojtermin.ai" `
  -FrontendBaseUrl "https://app.mojtermin.ai" `
  -BusinessSlug "demo-salon"
```

Ako ne proslijediš URL-ove, smoke test se preskače:

```powershell
.\scripts\pre-release.ps1
```

## 11) Docker (lokalni full-stack)

Cijeli stack (SQL Server 2022 + API + Angular SPA iza Nginx-a) pokreće se sa:

```powershell
# Prvi put:
copy .env.example .env
# Otvori .env i postavi SA_PASSWORD i JWT_SECRET (sa instrukcijama iz sekcije 2).

docker compose up --build
```

Servisi:
- API: http://localhost:5080  (health: http://localhost:5080/health)
- Web: http://localhost:8080
- DB:  localhost:1433  (SA login iz `SA_PASSWORD`)

Stop + brisanje stanja:

```powershell
docker compose down -v
```

Postoje 3 docker fajla na rootu:
- `Dockerfile.api` — multi-stage build za .NET 8 API (non-root user, healthcheck na `/health`)
- `Dockerfile.web` — Angular prod build u Nginx-u sa SPA history fallback + security headers
- `docker-compose.yml` — full stack za lokalni dev / staging

`appsettings.Production.json` u image-u ostaje sa praznim placeholderima. Sve secrets dolaze isključivo iz environment varijabli iz `.env` (lokalno) ili App Service / K8s Secret-a (prod). Aplikacija fail-uje startup ako su nepostavljeni — to je namjerno.

## 12) GitHub Actions CI

Workflow `.github/workflows/ci.yml` se pokreće na svaki push i PR na `main`/`master`/`develop`:

- **backend job**: `dotnet restore` + `dotnet build` (Release) + `dotnet test`. Upload-uje `.trx` test rezultate i publish artifact za API.
- **frontend job**: `npm ci` + `npm run build` (prod). Upload-uje statički build artifact.
- **docker job**: gradi oba image-a (API + Web) sa BuildKit cache-om u GHA. Ne push-uje na registry — to je tvoj posao prilikom production deploy-a (dodaj korake za `docker push` u svoj private registry ili u GHA secrets ako želiš automatski push na DockerHub/GHCR).

Artifakti se zadržavaju 30 dana, test rezultati 14 dana.

Concurrency je postavljen tako da push na isti branch otkazuje prethodni run u tijeku — ne troši CI minute na zastarjeli kod.
