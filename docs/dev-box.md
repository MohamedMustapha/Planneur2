# The dev box

One command brings up the whole platform: Postgres, Keycloak (LDAP-shaped realm, seeded people), RustFS, SEQ,
Prometheus, Grafana, an on-prem-shaped LLM stub, a stub standing in for Azure DevOps and ServiceNow, the API,
the BFF and the Angular dev server.

```
aspire run --project src/AppHost
```

.NET Aspire orchestrates local development. `deploy/docker-compose.yml` stays the **deployment** artifact — see
[Deployment](#deployment) for why both exist and how they are kept in step.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.200+ | pinned in `global.json` |
| Aspire CLI | 13.4+ | `dotnet tool install -g aspire.cli`, or run the AppHost with `dotnet run` |
| Node.js | **24.15+** | Angular 22 refuses to build on anything older |
| npm | 11+ | ships with Node |
| Docker | running | Postgres, Keycloak, RustFS, SEQ, Prometheus and Grafana are containers |

The dev box binds **4400** (web), **5000** (BFF) and **5100** (API). Windows reserves TCP ranges for Hyper-V and
WSL — commonly 4103-4202, which is why the conventional Angular port 4200 is not used. If one of these collides on
your machine, change it in `AppHost.cs` **and** in the realm's `redirectUris` (`build-realm.py`), because Keycloak
matches the browser's origin literally:

```
netsh interface ipv4 show excludedportrange protocol=tcp
```

Check with `dotnet --version && node -v && npm -v && docker info --format "{{.ServerVersion}}"`.

---

## First run

### 1. Secrets

The AppHost takes every credential as an Aspire parameter, held in .NET user secrets. Nothing is committed and
nothing has a default — a missing one fails the AppHost at startup rather than silently booting an unsecured
service.

```
cd src/AppHost
dotnet user-secrets set "Parameters:postgres-password"        "<choose one>"
dotnet user-secrets set "Parameters:app-owner-password"       "<choose one>"
dotnet user-secrets set "Parameters:app-rw-password"          "<choose one>"
dotnet user-secrets set "Parameters:keycloak-admin-password"  "<choose one>"
dotnet user-secrets set "Parameters:bff-client-secret"        "cracra-bff-dev-secret"
dotnet user-secrets set "Parameters:storage-access-key"       "<choose one>"
dotnet user-secrets set "Parameters:storage-secret-key"       "<choose one>"
```

`bff-client-secret` must match the `cracra-bff` client secret in the realm export. The committed realm uses
`cracra-bff-dev-secret`; change both together or neither.

### 2. Web dependencies

```
cd web && npm install
```

Mobiscroll is **not** installed from a registry. It is a commercial package delivered as a zip, unpacked into
`web/src/lib/mobiscroll` and resolved through a `@mobiscroll/angular` path mapping in `web/tsconfig.json`. It is
committed, so `npm install` does not need registry credentials.

Two things to preserve when upgrading (replace the contents of `web/src/lib/mobiscroll` from a fresh download):

- The path mapping points at the **ESM** build (`esm5/`), not the UMD one. The UMD wrapper does a dynamic
  `require("@angular/core")`; esbuild rewrites that during `ng build`, so the UMD build passes CI and then throws
  *"Dynamic require of @angular/core is not supported"* the first time a developer opens the page under
  `ng serve`. It also tree-shakes worse — the ESM build is about 200 kB smaller.
- `esm5/mobiscroll.angular.min.d.ts` is a hand-written shim re-exporting the typings, because Mobiscroll only
  ships `.d.ts` files next to the UMD build. Re-extracting the zip deletes it; put it back.

### 3. Run

```
aspire run --project src/AppHost
```

The dashboard opens with every resource, its logs and its endpoints. First run pulls container images and imports
the Keycloak realm, so allow a few minutes.

---

## What you get

| Resource | Where | What for |
|---|---|---|
| **web** | http://localhost:4400 | Angular dev server, proxies `/api` and `/bff` to the BFF |
| **bff** | http://localhost:5000 | YARP + OIDC. The only thing that ever holds a token |
| **api** | http://localhost:5100 | The modular monolith |
| **keycloak** | http://localhost:8080 | realm `cracra`, admin user `admin` |
| **postgres** | via pgAdmin in the dashboard | one database, one schema per module |
| **rustfs** | http://localhost:9010 | S3-compatible object storage |
| **seq** | dashboard link | structured logs, filter by `CorrelationId` |
| **prometheus** | http://localhost:9090 | scrapes `/metrics` from the API and the BFF |
| **grafana** | http://localhost:3000 | anonymous admin, "Cracra — platform baseline" dashboard |
| **llm** | dashboard link | OpenAI-compatible stub so nobody needs a GPU |
| **providers** | dashboard link | Azure DevOps + ServiceNow stub, so the pull works with no corporate network |

### Seeded people

Every account uses the password `cracra`. The ids match `SeedOrganisation` in the test fixtures and the
`deploy/keycloak/build-realm.py` source, so an integration test and a browser session refer to the same person.

| Username | Unit | Department | Contextual roles |
|---|---|---|---|
| `camille.villeneuve` | Infrastructure & Réseaux | DSI | member |
| `mehdi.sadaoui` | Infrastructure & Réseaux | DSI | member |
| `anais.lefevre` | Infrastructure & Réseaux | DSI | member |
| `thomas.berthier` | Infrastructure & Réseaux | DSI | member, unit-head |
| `julie.ondracek` | Études & Développement | DSI | member |
| `olivier.marchand` | Études & Développement | DSI | member, dept-head |
| `sofia.navarro` | Comptabilité | Direction Financière | member |
| `laurent.bouchard` | Contrôle de gestion | Direction Financière | member, dept-head |
| `nadia.kessler` | Études & Développement | DSI | member, pmo |
| `pierre.dubois` | Études & Développement | DSI | member, project-lead |

Sign in as `camille.villeneuve` for the ordinary-employee view, `olivier.marchand` to see the department and
finance entries appear in the rail, `nadia.kessler` for the portfolio-wide view.

### Connecting Azure DevOps and ServiceNow

Nothing is connected out of the box, deliberately: S10 mirrors external work items and a mirror with invented
rows in it would be worse than an empty one. The dev box ships the far end — the **providers** stub answers the
two API shapes the adapters are written against — and connecting it is the same three steps a real deployment
takes.

Sign in as `olivier.marchand` (dept-head) and open **Paramètres → Intégrations**:

1. **Create a connection.** Provider *Azure DevOps*, base URL the **providers** endpoint from the dashboard, team
   project `CRACRA`, current sprint `Sprint 42`, secret reference `dev-devops`, interval `0` (on demand).
2. **Map a vocabulary.** Add an *area path* mapping from `CRACRA\Platform` to one of your projects. Without it
   the items still mirror; they simply have no project, so picking one in the dropdown pre-fills nothing.
3. **Press Synchroniser.** The card reports the outcome. "Voir les éléments recopiés" shows what came back.

Camille then finds those tasks in her board's quick-add dropdown. For the RUN side, repeat with provider
*ServiceNow*, assignment group `Helpdesk N1`, secret reference `dev-servicenow`, and an *assignment-group*
mapping to the Infrastructure unit; the tickets appear in the 6a work-order pool.

The secret references are names, not tokens: `AppHost.cs` provides `dev-devops` and `dev-servicenow` as
environment variables, exactly as a deployment's vault would. A connection whose `auth_ref` names nothing
reports a failed pull with the reason, which is worth seeing once.

### Reading the capex/opex view

**Paramètres → nothing**: the capitalization view lives on its own rail entry, and only a department head or the
PMO sees it. Sign in as `olivier.marchand` and open **Finance**.

Out of the box a department has no rate card, so the view shows the split in hours plus whatever manual project
costs exist, and says as much at the top. Two things change that:

1. **Règles et taux → a treatment.** Each of the four activity buckets maps to capex, opex or excluded. The
   defaults are BUILD → capex, RUN → opex, quality of life → opex, administration → excluded; a department that
   capitalizes its maintenance changes one dropdown and the figures move.
2. **Règles et taux → a rate.** Pick a functional role, an hourly rate and a start date. Hours logged by people
   holding that role are then valued, and the effort columns fill in. Cards are effective-dated and may not
   overlap for one role, so last quarter's figures do not move when this year's rates are entered.

**Exporter (Excel)** renders the same view to a workbook, stores it in RustFS and hands back a short-lived link.
The link is shown rather than followed, because a download that starts on its own is indistinguishable from one
that failed.

Sign in as `camille.villeneuve` and go to `/finance` directly to see the other half of the matrix: the rail never
offered the entry, and the API refuses the request outright rather than returning an empty department.

The realm is generated, not hand-edited:

```
python deploy/keycloak/build-realm.py
```

Edit `build-realm.py` and re-run it. Editing `cracra-realm.json` directly means the next regeneration silently
discards the change.

---

## Proving the chain works

`GET /api/ping` is the one endpoint whose job is to describe itself. It reports both what the application believes
about the caller and what Postgres actually sees in its session:

```json
{
  "identity":        { "userId": "…", "unitId": "…", "departmentIds": ["…"], "roles": ["member"] },
  "databaseSession": { "userId": "…", "unitId": "…", "departmentIds": ["…"], "roles": ["member"], "isScoped": true }
}
```

If those two disagree, RLS is not protecting anything — which is why the S0 acceptance test compares them rather
than just asserting a 200.

---

## Tests

```
dotnet test tests/Unit               # domain, mediator, claim parsing — no I/O
dotnet test tests/Architecture       # module isolation, no MediatR, layer rules
dotnet test tests/Integration        # Testcontainers Postgres, real RLS, real outbox
dotnet test tests/E2E                # boots the full Aspire stack, drives a browser
```

Integration and E2E need Docker running. E2E also needs the Playwright browsers once:

```
tests/E2E/bin/Debug/net10.0/playwright.ps1 install chromium
```

---

## Deployment

`deploy/docker-compose.yml` is the deployment artifact; Aspire is the dev box. Both exist on purpose: Aspire gives
a developer one command and a dashboard, while the deployment target is a plain Docker host with no orchestrator.

They are kept in step by hand, which is why the AppHost passes **explicit environment variables** rather than
relying on Aspire's connection-string conventions. Every variable in `AppHost.cs` has a visible counterpart in the
compose file, so a drift between them is a diff rather than a mystery. When you add configuration, add it in both.

```
cd deploy
cp .env.example .env      # then fill it in
docker compose up -d --build
```

The compose build compiles the Angular app into the BFF image's `wwwroot`, so the deployed BFF serves the client
and proxies the API from the same origin.
