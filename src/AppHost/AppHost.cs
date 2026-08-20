// =================================================================================================================
// The dev box.
//
// `aspire run` brings up the whole platform — Postgres, Keycloak, RustFS, SEQ, Prometheus, Grafana, the on-prem LLM
// stub, the API, the BFF and the Angular dev server — with one command and no hand-written compose file. The
// deployment artifact stays /deploy/docker-compose.yml; this file is what a developer actually runs.
//
// Everything a service needs arrives as an explicit environment variable. No service resolves an Aspire connection
// string by convention, which keeps the application code identical whether it was started by Aspire, by compose,
// or by an integration test.
// =================================================================================================================

var builder = DistributedApplication.CreateBuilder(args);

// --- Secrets -----------------------------------------------------------------------------------------------------
// Generated per developer and held in user secrets. Nothing here is committed.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var appOwnerPassword = builder.AddParameter("app-owner-password", secret: true);
var appRuntimePassword = builder.AddParameter("app-rw-password", secret: true);
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var bffClientSecret = builder.AddParameter("bff-client-secret", secret: true);
var syncClientSecret = builder.AddParameter("sync-client-secret", secret: true);
var storageAccessKey = builder.AddParameter("storage-access-key", secret: true);
var storageSecretKey = builder.AddParameter("storage-secret-key", secret: true);

// --- Data --------------------------------------------------------------------------------------------------------
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume("cracra-postgres")
    .WithPgAdmin();

var database = postgres.AddDatabase("cracradb");

// --- Identity ----------------------------------------------------------------------------------------------------
// The realm export carries the clients, the scopes, the protocol mappers the UserContextMiddleware depends on, and
// a seeded set of people across two departments so the visibility matrix has something to be true about.
// A plain container rather than Aspire.Hosting.Keycloak, for two reasons.
//
// First, that integration runs Keycloak HTTPS-only behind an Aspire-issued dev certificate. Every participant
// then has to trust it — the BFF as a host process, the browser in the E2E suite, and any curl a developer
// reaches for — which is a lot of ceremony for a local identity server that is fronted by a reverse proxy in
// production anyway.
//
// Second, and more importantly, this block is now line-for-line the same configuration as the keycloak service in
// deploy/docker-compose.yml. The two files are kept in step by hand; the less they differ in shape, the less
// there is to keep in step.
//
// No data volume, deliberately: Keycloak only imports a realm that does not already exist, so a persisted volume
// means edits to build-realm.py silently never take effect — the realm you are running stops matching the realm
// in source control, and it surfaces as a rejected login with no obvious cause.
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.4")
    .WithArgs("start-dev", "--import-realm")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_HTTP_ENABLED", "true")
    .WithEnvironment("KC_HOSTNAME_STRICT", "false")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithBindMount("../../deploy/keycloak", "/opt/keycloak/data/import", isReadOnly: true)
    // Readiness is the realm's discovery document, not the server's own health endpoint: "Keycloak is up" and
    // "the cracra realm is imported and serving" are different facts, and only the second one lets the BFF work.
    .WithHttpHealthCheck("/realms/cracra/.well-known/openid-configuration");

// --- Object storage ------------------------------------------------------------------------------------------------
var rustfs = builder.AddContainer("rustfs", "rustfs/rustfs", "latest")
    .WithHttpEndpoint(port: 9010, targetPort: 9000, name: "s3")
    .WithEnvironment("RUSTFS_ACCESS_KEY", storageAccessKey)
    .WithEnvironment("RUSTFS_SECRET_KEY", storageSecretKey)
    .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "true")
    .WithVolume("cracra-rustfs", "/data");

var rustfsEndpoint = rustfs.GetEndpoint("s3");

// --- Observability -------------------------------------------------------------------------------------------------
var seq = builder.AddSeq("seq")
    .WithDataVolume("cracra-seq")
    .WithEnvironment("ACCEPT_EULA", "Y");

var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "latest")
    .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
    .WithBindMount("../../deploy/prometheus", "/etc/prometheus", isReadOnly: true)
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml");

builder.AddContainer("grafana", "grafana/grafana", "latest")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
    .WithBindMount("../../deploy/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
    .WithBindMount("../../deploy/grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
    .WithEnvironment("GF_SECURITY_ALLOW_EMBEDDING", "true")
    .WaitFor(prometheus);

// --- On-prem LLM ---------------------------------------------------------------------------------------------------
// A stub locally so nobody needs a GPU to run the stack. Point Cracra:Ai:BaseUrl at the real on-prem endpoint to
// swap it; the application code cannot tell the difference.
var llm = builder.AddProject<Projects.Cracra_Tools_LlmStub>("llm")
    .WithHttpHealthCheck("/health");

// --- API -----------------------------------------------------------------------------------------------------------
var api = builder.AddProject<Projects.Cracra_Host>("api")
    .WithReference(database)
    .WaitFor(database)
    .WaitFor(keycloak)
    .WithEnvironment("Cracra__Database__AdminConnectionString", database)
    .WithEnvironment("Cracra__Database__OwnerPassword", appOwnerPassword)
    .WithEnvironment("Cracra__Database__RuntimePassword", appRuntimePassword)
    .WithEnvironment("Cracra__Keycloak__Authority", $"{keycloak.GetEndpoint("http")}/realms/cracra")
    .WithEnvironment("Cracra__Keycloak__Audience", "cracra-api")
    .WithEnvironment("Cracra__Storage__ServiceUrl", rustfsEndpoint)
    .WithEnvironment("Cracra__Storage__AccessKey", storageAccessKey)
    .WithEnvironment("Cracra__Storage__SecretKey", storageSecretKey)
    .WithEnvironment("Cracra__Ai__BaseUrl", llm.GetEndpoint("http"))
    .WithEnvironment("Cracra__Directory__Sync__KeycloakBaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Cracra__Directory__Sync__Realm", "cracra")
    .WithEnvironment("Cracra__Directory__Sync__ClientId", "cracra-sync")
    .WithEnvironment("Cracra__Directory__Sync__ClientSecret", syncClientSecret)
    // The dev box stands in for Azure DevOps and ServiceNow until S10 brings the real adapters, so the
    // pull-a-task flow is exercisable here. Sample tasks are derived from the caller's own projects, never
    // invented, and this flag stays off in every real deployment.
    .WithEnvironment("Cracra__Activities__AssignableTasks__SeedSampleTasks", "true")
    // Likewise for the S6 work-order pool: the dev box stands in for ServiceNow so the drag-from-queue journey
    // is exercisable before S10's adapters exist. Samples derive from the unit that asked and never leave it.
    .WithEnvironment("Cracra__Scheduling__Pool__SeedSampleWorkOrders", "true")
    // The zone a meeting's wall-clock time is read in. A 09:00 stand-up is 09:00 all year, so the series stores
    // the local time and this says which clock that is — the one knob S7 needs from a deployment.
    .WithEnvironment("Cracra__Meetings__DefaultTimeZoneId", "Europe/Paris")
    // The stub answers as this model name, so the dev box's summaries carry a model somebody can recognize in a
    // report footer rather than a placeholder that looks like a bug.
    .WithEnvironment("Cracra__Ai__Model", "local-model")
    .WithEnvironment("Cracra__Storage__Bucket", "cracra")
    .WithEnvironment("ConnectionStrings__seq", seq.GetEndpoint("http"))
    .WithHttpHealthCheck("/alive")
    // Pinned, not dynamic: Prometheus scrapes /metrics from a static target list, and the Angular dev proxy needs
    // a stable address for the BFF. Both ports sit outside the Windows Hyper-V/WSL reserved ranges.
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5100;
        endpoint.TargetPort = 5100;
        endpoint.IsProxied = false;
    })
    .WaitFor(llm)
    .WaitFor(rustfs);

// --- BFF -----------------------------------------------------------------------------------------------------------
var bff = builder.AddProject<Projects.Cracra_Bff>("bff")
    .WithReference(api)
    .WaitFor(api)
    .WaitFor(keycloak)
    .WithEnvironment("Cracra__Keycloak__Authority", $"{keycloak.GetEndpoint("http")}/realms/cracra")
    .WithEnvironment("Cracra__Keycloak__ClientId", "cracra-bff")
    .WithEnvironment("Cracra__Keycloak__ClientSecret", bffClientSecret)
    .WithEnvironment("ReverseProxy__Clusters__api__Destinations__primary__Address", api.GetEndpoint("http"))
    .WithEnvironment("ConnectionStrings__seq", seq.GetEndpoint("http"))
    .WithHttpHealthCheck("/alive")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5000;
        endpoint.TargetPort = 5000;
        endpoint.IsProxied = false;
    });

// --- Angular ---------------------------------------------------------------------------------------------------------
// The dev server proxies /api and /bff back to the BFF (see web/proxy.conf.json) so the browser stays same-origin
// and the HttpOnly session cookie behaves exactly as it will in production.
// Pinned to 4400, not the conventional 4200. Windows reserves TCP ranges for Hyper-V and WSL (4103-4202 and
// 4203-4302 on a typical machine), so ng serve on 4200 dies with EACCES before printing anything useful. It has
// to be pinned rather than dynamic because the browser's origin becomes the OIDC redirect_uri, and Keycloak
// matches that against a fixed allowlist in the realm — a moving port means a rejected login every run.
// Check yours with: netsh interface ipv4 show excludedportrange protocol=tcp
builder.AddJavaScriptApp("web", "../../web", "start")
    .WithNpm()
    .WithHttpEndpoint(port: 4400, targetPort: 4400, env: "PORT", isProxied: false)
    .WithEnvironment("CRACRA_BFF_URL", bff.GetEndpoint("http"))
    .WithReference(bff)
    .WaitFor(bff)
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();
