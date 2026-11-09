# Biletado reservations-v4

REST backend for room reservations, built with C# 12, ASP.NET Core 8 and PostgreSQL 17.
Author: Nicolai Stoetzel, TINF24B3. License: MIT.

Repository: https://github.com/Chickenniggo/Reservation

## Scope and contract

The assignment uses reservations API **4.0.0**. Public endpoints are read-only; writes require a valid JWT.

- Contract: https://gitlab.com/biletado/apidocs/-/blob/d9dc8aae746b7bf137630eddab4e0f4e7e0d7480/specs/v4/reservations-v4.yaml
- Official v4 tests: https://gitlab.com/biletado/apidocs/-/tree/fa1d632edde44e8c68da7e2938bb400e2b76bbee/httpyac
- Environment: https://gitlab.com/biletado/kustomize/-/tree/f9ed3b8e452ee2e1e48981d266b2a327900ab674
- Quickstart: https://gitlab.com/biletado/quickstart

## Structure and behavior

- `src/Reservations.Api`: Minimal APIs, validation, Npgsql persistence, JWT validation and assets HTTP client.
- `tests/Reservations.Api.Tests`: Own xUnit validation, authentication and PostgreSQL integration tests.
- `deploy`: Overlay replacing the reservations deployment in the supplied environment.
- `scripts`: Verification and submission packaging.

Dates are calendar dates. Intervals are half-open: adjacent bookings do not conflict. List filters include equal boundary dates, per specification.
Public reads never expose deleted records. Protected reads can include them with `include_deleted=true`.
PUT creates missing IDs, replaces active reservations or restores deleted ones with explicit `deleted_at: null`.
DELETE soft-deletes by default; `permanent=true` removes the record. ETags change on every modification.
PostgreSQL exclusion constraints prevent concurrent overlapping active reservations. Transactions and advisory locks serialize changes and If-Match checks for the same ID.
All request-controlled SQL values are parameters. Room existence is checked only over HTTP, never against the assets database.

## Requirements

- .NET SDK 8 or a newer SDK supporting `net8.0`; .NET 8 runtime for local execution.
- PostgreSQL 17, or the database in the supplied environment.
- Complete deployment additionally needs Docker/Podman, kind, kubectl and a running container engine.
- The assignment stays on .NET 8. Its support ends in November 2026; this setup is intended for the course environment.

## Deploy in the supplied environment

Follow the upstream kind setup first. Use the actual cluster name if different:

```powershell
docker build -t reservations-api:local .
kind load docker-image reservations-api:local --name kind-cluster
kubectl create namespace biletado
kubectl apply -k deploy -n biletado
kubectl rollout status deployment/reservations -n biletado --timeout=180s
```

If the namespace already exists, skip its creation. The upstream setup exposes ingress on port 9090.
API: `http://localhost:9090/api/v4/reservations/status`.
The overlay uses the locally loaded image. For a registry deployment, replace `image` in `deploy/reservations-patch.yaml`
with `ghcr.io/chickenniggo/reservation:<commit-sha>`. Set the GHCR package visibility to public, or supply image pull credentials.

The existing reservations table and upstream example data are reused. Startup idempotently adds the version column, date constraint and overlap constraint.
The database user must be able to create `btree_gist` and alter the table. Invalid/overlapping existing active data causes migration to fail, not silent deletion.
For production, run the embedded SQL with a migration account, then set `Database__Initialize=false` for the restricted runtime account.
Upstream development passwords and HTTP IAM configuration are only for the provided development environment.

## Run locally

With the upstream environment already running, forward PostgreSQL in one terminal:

```powershell
kubectl port-forward service/postgres 5432:5432 -n biletado
```

In another terminal:

```powershell
dotnet run --project src/Reservations.Api --urls http://localhost:5080 -- --environment Development
```

Open `http://localhost:5080/swagger` or `http://localhost:5080/api/v4/reservations/status`.
For startup without a database set `$env:Database__Initialize='false'`; data operations remain unavailable and readiness returns 503.
Short aliases `/status`, `/health*`, `/reservations` and `/public-reservations` also work directly; ingress exposes only the canonical prefix.

## Configuration

Environment variables override appsettings files. Use `__` for nested keys.

| Variable | Default / meaning |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | Container: `8080` |
| `ASPNETCORE_ENVIRONMENT` | `Development` enables the local HTTP IAM configuration |
| `ConnectionStrings__Reservations` | Optional full Npgsql connection string; overrides individual database settings |
| `POSTGRES_RESERVATIONS_HOST` | `localhost`; supplied deployment sets `postgres` |
| `POSTGRES_RESERVATIONS_PORT` | `5432` |
| `POSTGRES_RESERVATIONS_DBNAME` | `reservations_v4` |
| `POSTGRES_RESERVATIONS_USER` | `postgres` (development only) |
| `POSTGRES_RESERVATIONS_PASSWORD` | `postgres` (development only) |
| `Database__Initialize` | `true`; idempotent schema migration on startup |
| `ASSETS_API_HOST` | `http://localhost:9090`; origin without API path |
| `Assets__BaseUrl` | Optional full base URL including `/api/v3/assets/` and trailing slash |
| `Assets__TimeoutSeconds` | `5` |
| `KEYCLOAK_HOST`, `KEYCLOAK_REALM` | `localhost:9090`, `biletado`; derive fallback authority |
| `Jwt__Authority` | Optional OIDC authority; match the actual IAM setup |
| `Jwt__MetadataAddress` | Optional discovery URL; otherwise derived from authority |
| `Jwt__Issuer` | Expected token issuer; otherwise authority |
| `Jwt__JwksUri` | Optional direct signing-key URL, replaces discovery; supports internal Keycloak DNS |
| `Jwt__Audience` | `account`; match the audience actually issued by IAM |
| `Jwt__RequireHttpsMetadata` | `true`; explicitly false only for HTTP development IAM |
| `Cors__Origins__0`, `Cors__Origins__1` | Allowed origins; default localhost ports 9090 and 5080 |
| `Authors__0`, `Authors__1` | Actual authors; default Nicolai Stoetzel |
| `Logging__LogLevel__Default` | `Information`; JSON console logs; Debug/Warning/Error supported |
| `AllowedHosts` | ASP.NET host filtering, default `*`; restrict in production |

JWT signatures, expiry, issuer and audience are checked; only RS256 is accepted. Scopes are not required.
Microsoft's configuration manager caches/refreshes signing keys. No signing secret is hard-coded.
The overlay separates the public issuer `http://localhost:9090/auth/realms/biletado` from key retrieval through internal `http://keycloak/...`.
For another hostname adjust issuer/authority and browser origins. Use HTTPS outside the local environment.
Writes log operation, object type, object ID and JWT subject. Tokens/request bodies are not logged.
Liveness is dependency-independent. Readiness checks the reservations table and assets `GET /api/v3/assets/health/live` returning `live: true`.

## Verification and CI

```powershell
./scripts/verify.ps1
```

For database tests point to an **isolated disposable database**, never production:

```powershell
$env:POSTGRES_TEST_CONNECTION='Host=localhost;Database=webeng2_test;Username=postgres;Password=YOUR_LOCAL_TEST_PASSWORD;Max Auto Prepare=20'
./scripts/verify.ps1
```

Without that variable database tests are explicitly skipped. With it the suite uses real HTTP requests through the app and real PostgreSQL transactions.
Assets and the IAM key source are test doubles; tokens are actually RSA-signed and validated.
Results: `artifacts/test-results/integration.trx`. Release output: `artifacts/publish/`.
`./scripts/verify.ps1 -Container` additionally builds the image with Docker.
If Windows blocks unsigned scripts, permit execution only in the current terminal with `Set-ExecutionPolicy -Scope Process Bypass` after inspecting the scripts; no permanent policy change is required.

The GitHub Actions workflow has three jobs:

1. `test`: runs the own tests against PostgreSQL and publishes the Release build.
2. `integration`: builds the container, loads it into kind and runs the official v4 httpyac tests with real Keycloak and assets.
3. `image`: publishes `ghcr.io/chickenniggo/reservation` with the commit SHA and `latest` tags after both test jobs succeed.

Pull requests build and test without publishing. Registry authentication uses the job's `GITHUB_TOKEN`.
Test reports and cluster logs are available as `test-results` and `cluster-integration` artifacts in the Actions run.
GHCR packages may initially be private; their visibility must be set separately before an unauthenticated deployment can pull them.

The pinned upstream test harness misses the v4 URL and v4 schema directory. `scripts/prepare-httpyac.cjs` adds these two configuration entries;
it does not alter the test assertions. The API contract and test revision are linked above.

## Submission

```powershell
./scripts/package.ps1
```

Output: `abgabe/programmentwurf.zip`. The already submitted questionnaire is kept unchanged.
The archive contains source, lockfiles, tests, configuration, container/deployment files, documentation, license and latest test report if present.
It excludes build output, local secrets, the assignment PDF and previous archives.
For an OCI image archive: `podman save --format oci-archive -o reservations-api.tar reservations-api:local`.

For submission, use a successful Actions run, include its test reports, and regenerate the source archive.
The container image must be accessible to the examiner, either through public GHCR visibility or an OCI archive.