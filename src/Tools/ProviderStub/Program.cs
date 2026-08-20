using System.Globalization;

// =================================================================================================================
// Azure DevOps and ServiceNow, enough of them to be pulled from.
//
// It exists for the same reason the LLM stub does: `aspire run` must bring up a working platform on a laptop with
// no corporate network behind it, and an E2E test that asserts "the sprint task reached the dropdown" must be
// asserting about our sync rather than about somebody else's staging environment.
//
// It answers the two shapes the adapters actually use — WIQL then a work-item batch, and the ServiceNow Table API
// — with a deterministic set derived from the query, so a test can name an item and still be right next week. It
// is never deployed, and it deliberately implements no write endpoint at all: a stub that could be written to
// would make the read-only guarantee untestable here, which is where it is cheapest to test.
// =================================================================================================================

var builder = WebApplication.CreateSlimBuilder(args);

var app = builder.Build();

// --- Azure DevOps ------------------------------------------------------------------------------------------------
// The real API is two calls: a query that answers with ids, then a batch that answers with fields. The stub keeps
// the shape, because the shape is what the adapter is written against.

app.MapPost("/{project}/_apis/wit/wiql", (string project) => Results.Ok(new
{
    queryType = "flat",
    workItems = DevOpsItems(project).Select(item => new { id = item.Id }).ToArray(),
}));

app.MapGet("/_apis/wit/workitems", (string ids) =>
{
    var wanted = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(id => long.TryParse(id, out var parsed) ? parsed : -1)
        .ToHashSet();

    // Every project's items, filtered to the ids asked for — which is what the real batch endpoint does: it is
    // collection-scoped, not project-scoped, and takes ids the query already produced.
    var value = StubProjects()
        .SelectMany(DevOpsItems)
        .Where(item => wanted.Contains(item.Id))
        .Select(item => new
        {
            id = item.Id,
            fields = new Dictionary<string, object?>
            {
                ["System.Id"] = item.Id,
                ["System.Title"] = item.Title,
                ["System.WorkItemType"] = item.Type,
                ["System.State"] = item.State,
                ["System.AssignedTo"] = item.AssignedTo is null
                    ? null
                    : new { uniqueName = item.AssignedTo, displayName = item.AssignedTo },
                ["System.AreaPath"] = item.AreaPath,
                ["System.IterationPath"] = item.IterationPath,
                ["System.ChangedDate"] = item.ChangedOn.ToString("O", CultureInfo.InvariantCulture),
                ["Microsoft.VSTS.Scheduling.RemainingWork"] = item.RemainingWork,
            },
        })
        .ToArray();

    return Results.Ok(new { count = value.Length, value });
});

// --- ServiceNow --------------------------------------------------------------------------------------------------

app.MapGet("/api/now/table/task", (string? sysparm_query) =>
{
    // The adapter builds "assignment_group.name=<queue>^active=true^ORDERBY…". Only the group is interesting
    // here, and parsing it back is what makes two connections to two queues return two different sets — which is
    // the property the RLS test depends on.
    var group = Between(sysparm_query, "assignment_group.name=", '^') ?? "Helpdesk N1";

    var result = ServiceNowItems(group).Select(item => new Dictionary<string, object?>
    {
        ["sys_id"] = item.SysId,
        ["number"] = item.Number,
        ["short_description"] = item.Title,
        ["sys_class_name"] = item.Class,
        ["state"] = item.State,
        ["assigned_to.user_name"] = item.AssignedTo,
        ["assignment_group.name"] = group,
        ["sys_updated_on"] = item.UpdatedOn.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
    }).ToArray();

    return Results.Ok(new { result });
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

await app.RunAsync();

// The reference date. Fixed rather than "now", so a snapshot of what the stub returns is the same on any day and
// an E2E assertion on ordering does not depend on when the suite ran.
static DateTimeOffset Reference() => new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

static string? Between(string? value, string prefix, char terminator)
{
    if (string.IsNullOrEmpty(value))
    {
        return null;
    }

    var start = value.IndexOf(prefix, StringComparison.Ordinal);

    if (start < 0)
    {
        return null;
    }

    var rest = value[(start + prefix.Length)..];
    var end = rest.IndexOf(terminator);

    return end < 0 ? rest : rest[..end];
}

/// <summary>
/// The team projects the stub knows about.
/// </summary>
/// <remarks>
/// Named for the seeded organization, so what a browser sees in the dropdown reads like the department it logged
/// into rather than like test data.
/// </remarks>
static string[] StubProjects() => ["CRACRA"];

static DevOpsItem[] DevOpsItems(string project)
{
    // Assigned to the seeded people by their LDAP uid — the same uids deploy/keycloak/build-realm.py creates and
    // the same ones SeedOrganisation names. That is what makes "Camille sees her own sprint task" true end to end
    // rather than true only inside one suite's fixtures.
    var reference = Reference();

    return
    [
        new DevOpsItem(4301, $"Migrer le socle {project} vers .NET 10", "Task", "Active",
            "camille.villeneuve", $"{project}\\Platform", $"{project}\\Sprint 42", reference, 6m),
        new DevOpsItem(4302, "Corriger la pagination de l'annuaire", "Bug", "Active",
            "camille.villeneuve", $"{project}\\Platform", $"{project}\\Sprint 42", reference.AddHours(-2), 2m),
        new DevOpsItem(4303, "Câbler la synchronisation des habilitations", "Task", "New",
            "mehdi.sadaoui", $"{project}\\Platform", $"{project}\\Sprint 42", reference.AddHours(-5), 4m),

        // Deliberately in the previous iteration, so "current sprint" is a filter with something to exclude.
        new DevOpsItem(4290, "Nettoyer les scripts de migration", "Task", "Active",
            "camille.villeneuve", $"{project}\\Platform", $"{project}\\Sprint 41", reference.AddDays(-14), 1m),

        // Deliberately unassigned, so the mirror has a DevOps item nobody owns.
        new DevOpsItem(4304, "Documenter le pipeline de déploiement", "Task", "New",
            null, $"{project}\\Platform", $"{project}\\Sprint 42", reference.AddHours(-8), null),
    ];
}

/// <summary>FNV-1a, because it is four lines and gives the same answer in every process that runs it.</summary>
static int StableHash(string value)
{
    unchecked
    {
        var hash = 2166136261u;

        foreach (var character in value)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return (int)(hash % int.MaxValue);
    }
}

static ServiceNowItem[] ServiceNowItems(string group)
{
    var reference = Reference();

    // A stable suffix per queue, so two units never see each other's tickets and the same unit sees the same
    // three every time — which is what lets an E2E assertion name one. Hashed by hand rather than with
    // string.GetHashCode: .NET randomizes that per process, so the "stable" numbers would change every restart.
    var suffix = StableHash(group) % 1000;

    return
    [
        new ServiceNowItem($"snow-{suffix}-001", $"INC{suffix:000}101", "Poste bloqué au démarrage",
            "incident", "2", null, reference),
        new ServiceNowItem($"snow-{suffix}-002", $"INC{suffix:000}102", "Imprimante hors service",
            "incident", "1", null, reference.AddHours(-1)),
        new ServiceNowItem($"snow-{suffix}-003", $"REQ{suffix:000}045", "Accès VPN pour un prestataire",
            "sc_request", "2", "thomas.berthier", reference.AddHours(-3)),
    ];
}

internal sealed record DevOpsItem(
    long Id,
    string Title,
    string Type,
    string State,
    string? AssignedTo,
    string AreaPath,
    string IterationPath,
    DateTimeOffset ChangedOn,
    decimal? RemainingWork);

internal sealed record ServiceNowItem(
    string SysId,
    string Number,
    string Title,
    string Class,
    string State,
    string? AssignedTo,
    DateTimeOffset UpdatedOn);
