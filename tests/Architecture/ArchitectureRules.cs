using System.Reflection;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using NetArchTest.Rules;

namespace Cracra.Tests.Architecture;

/// <summary>
/// The rules that keep the modular monolith modular.
/// </summary>
/// <remarks>
/// Several of these pass vacuously in S0 because the modules they constrain do not exist yet. That is intentional:
/// a rule written after the violation is a rule that gets argued with. Written now, the first module to break one
/// fails the build on the commit that broke it, which is the only moment the fix is cheap.
/// </remarks>
public sealed class ArchitectureRules
{
    private static readonly Assembly[] PlatformAssemblies =
    [
        typeof(IMediator).Assembly,
        typeof(ModuleDbContext).Assembly,
        typeof(BuildingBlocks.Web.Users.IUserContext).Assembly,
        typeof(Cracra.Host.Endpoints.PingEndpoint).Assembly,

        // Every module assembly must be listed here, or the module-isolation and layering rules below scan
        // nothing and pass vacuously — which is worse than not having them, because the green tick is read as
        // evidence. Add each new module's assembly and its contracts assembly as the module lands.
        typeof(Cracra.Modules.Directory.DirectoryModule).Assembly,
        typeof(Cracra.Modules.Directory.Contracts.PersonSummary).Assembly,
        typeof(Cracra.Modules.Access.AccessModule).Assembly,
        typeof(Cracra.Modules.Access.Contracts.WhoAmIResponse).Assembly,
        typeof(Cracra.Modules.Projects.ProjectsModule).Assembly,
        typeof(Cracra.Modules.Projects.Contracts.ProjectSummary).Assembly,
        typeof(Cracra.Modules.Portfolio.PortfolioModule).Assembly,
        typeof(Cracra.Modules.Portfolio.Contracts.PortfolioBoard).Assembly,
        typeof(Cracra.Modules.Activities.ActivitiesModule).Assembly,
        typeof(Cracra.Modules.Activities.Contracts.ActivityEntryView).Assembly,
        typeof(Cracra.Modules.Scheduling.SchedulingModule).Assembly,
        typeof(Cracra.Modules.Scheduling.Contracts.BoardPayload).Assembly,
        typeof(Cracra.Modules.Meetings.MeetingsModule).Assembly,
        typeof(Cracra.Modules.Meetings.Contracts.MeetingSeriesView).Assembly,
        typeof(Cracra.Modules.Kudos.KudosModule).Assembly,
        typeof(Cracra.Modules.Kudos.Contracts.KudoView).Assembly,
        typeof(Cracra.Modules.Reporting.ReportingModule).Assembly,
        typeof(Cracra.Modules.Reporting.Contracts.ReportView).Assembly,
    ];

    /// <summary>
    /// Every module on disk is in <see cref="PlatformAssemblies"/>.
    /// </summary>
    /// <remarks>
    /// The list above is what every other rule in this file scans, and a module missing from it is not a rule
    /// failing — it is a rule quietly checking nothing while the build stays green. That is the worst outcome
    /// available here, because a green tick on a layering rule is read as evidence the layering holds.
    ///
    /// Written when the list reached nine modules and adding a tenth was one edit away from being forgotten. It
    /// reads the source tree rather than the build output on purpose: a module that exists but was never added to
    /// the solution should fail this too.
    /// </remarks>
    [Fact]
    public void Every_module_in_the_source_tree_is_scanned_by_these_rules()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the repository root from the test assembly.");

        var onDisk = Directory
            .EnumerateDirectories(Path.Combine(directory.FullName, "src", "Modules"))
            .Select(path => $"Cracra.Modules.{Path.GetFileName(path)}")
            .ToArray();

        onDisk.ShouldNotBeEmpty("No module directories were found; this rule would pass vacuously.");

        var scanned = PlatformAssemblies
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        onDisk.Where(module => !scanned.Contains(module))
            .ShouldBeEmpty("Every module and contracts assembly must be listed in PlatformAssemblies.");
    }

    [Fact]
    public void Nothing_references_MediatR()
    {
        foreach (var assembly in PlatformAssemblies.Concat([typeof(Cracra.Bff.BffUser).Assembly]))
        {
            var mediatR = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(name => name.StartsWith("MediatR", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // conventions.md §1 is explicit: we own the mediator. A transitive MediatR would quietly reintroduce
            // the licensing and versioning question the hand-written one exists to avoid.
            mediatR.ShouldBeEmpty($"{assembly.GetName().Name} must not reference MediatR.");
        }
    }

    [Fact]
    public void Every_request_has_exactly_one_handler()
    {
        var requests = PlatformAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(IsRequestInterface))
            .ToArray();

        var handlers = PlatformAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(IsHandlerInterface)
            .Select(handler => handler.GetGenericArguments()[0])
            .ToArray();

        foreach (var request in requests)
        {
            var count = handlers.Count(handled => handled == request);

            // Two handlers means the DI container silently picks the last registered one, and the other never
            // runs. Zero means a runtime failure at the point of use rather than at startup.
            count.ShouldBe(1, $"{request.FullName} should have exactly one handler, found {count}.");
        }
    }

    [Fact]
    public void Pipeline_behaviors_are_stateless()
    {
        var result = Types.InAssemblies(PlatformAssemblies)
            .That()
            .ImplementInterface(typeof(IPipelineBehavior<,>))
            .Should()
            .BeImmutable()
            .GetResult();

        // Behaviors are resolved per request but shared in shape across all of them. Mutable state on one would
        // leak between requests in a way that is very hard to reproduce and very easy to ship.
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void A_module_domain_layer_depends_on_nothing_but_itself()
    {
        // DDD modules (Projects, Portfolio, Activities, Reporting, Kudos) land from S3 onward. Their Domain
        // namespace must stay free of EF, ASP.NET and every other module.
        var result = Types.InAssemblies(PlatformAssemblies)
            .That()
            .ResideInNamespaceMatching(@"Cracra\.Modules\.\w+\.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "FastEndpoints",
                "Npgsql")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Endpoints_do_not_touch_a_DbContext_directly_in_DDD_modules()
    {
        var result = Types.InAssemblies(PlatformAssemblies)
            .That()
            .ResideInNamespaceMatching(@"Cracra\.Modules\.\w+\.Api")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        // In a DDD module the endpoint maps a request to a command and sends it. Reaching for the DbContext there
        // is how the domain layer gets bypassed one "just this once" at a time.
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    /// <summary>
    /// Endpoints map requests to commands and queries; they never reach into a module's domain.
    /// </summary>
    /// <remarks>
    /// "The assignment side stays isolated", as a rule the build can check. An endpoint that constructs an
    /// aggregate or reads its state has taken a decision the domain was supposed to own, and it is the shortest
    /// path from a thin API layer to a fat one — each individual case looks harmless.
    ///
    /// Note this is stricter than the DbContext rule above and subsumes none of it: a handler may legitimately do
    /// both, an endpoint may do neither.
    /// </remarks>
    [Fact]
    public void Endpoints_do_not_reach_into_a_module_domain()
    {
        foreach (var assembly in PlatformAssemblies)
        {
            var moduleName = assembly.GetName().Name;

            if (moduleName is null || !moduleName.StartsWith("Cracra.Modules.", StringComparison.Ordinal))
            {
                continue;
            }

            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceMatching(@"Cracra\.Modules\.\w+\.Api")
                .ShouldNot()
                .HaveDependencyOnAny([.. assembly.GetTypes()
                    .Select(type => type.Namespace)
                    .Where(space => space is not null && space.EndsWith(".Domain", StringComparison.Ordinal))
                    .Distinct()!])
                .GetResult();

            result.IsSuccessful.ShouldBeTrue($"{moduleName}: {Describe(result)}");
        }
    }

    /// <summary>
    /// A module may reference another module's contracts assembly, and nothing else of it.
    /// </summary>
    /// <remarks>
    /// Checked on assembly references rather than namespaces. Namespace prefix matching cannot express this rule:
    /// forbidding <c>Cracra.Modules.Access</c> also forbids <c>Cracra.Modules.Access.Contracts</c>, which is the
    /// one reference that is explicitly allowed. Assemblies draw the line exactly where architecture.md §2 draws
    /// it — the contracts assembly is the sanctioned door, the implementation assembly is not.
    /// </remarks>
    [Fact]
    public void A_module_references_only_other_modules_contracts()
    {
        const string modulePrefix = "Cracra.Modules.";

        var moduleAssemblies = PlatformAssemblies
            .Where(assembly => assembly.GetName().Name?.StartsWith(modulePrefix, StringComparison.Ordinal) == true)
            .ToArray();

        // Without at least two modules this rule cannot catch anything, and a green tick would be meaningless.
        moduleAssemblies.Length.ShouldBeGreaterThan(1, "Module isolation needs more than one module to mean anything.");

        foreach (var assembly in moduleAssemblies)
        {
            var name = assembly.GetName().Name!;

            if (name.EndsWith(".Contracts", StringComparison.Ordinal))
            {
                continue;
            }

            var ownModule = name[modulePrefix.Length..];

            var forbidden = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(reference => reference.StartsWith(modulePrefix, StringComparison.Ordinal))
                .Where(reference => !reference.EndsWith(".Contracts", StringComparison.Ordinal))
                .Where(reference => reference[modulePrefix.Length..] != ownModule)
                .ToArray();

            forbidden.ShouldBeEmpty(
                $"{name} may reference other modules' contracts only, but references: {string.Join(", ", forbidden)}");
        }
    }

    /// <summary>
    /// A 2-layer module is two layers: an endpoint and a service.
    /// </summary>
    /// <remarks>
    /// conventions.md §2 gives two archetypes and a decision rule, and the failure mode it guards against is a
    /// module that quietly becomes both — an Endpoints folder next to an Application folder, half its writes
    /// going through a service and half through a command, and nobody able to say which is the way in.
    ///
    /// Expressed on namespaces because that is what the archetype is: the folder layout in each archetype's
    /// listing is the whole of the rule.
    /// </remarks>
    [Fact]
    public void A_module_picks_one_archetype_and_keeps_to_it()
    {
        foreach (var assembly in ModuleAssemblies())
        {
            var namespaces = assembly.GetTypes()
                .Select(type => type.Namespace)
                .Where(space => space is not null)
                .Distinct()
                .ToArray();

            var twoLayer = namespaces.Any(space => space!.Contains(".Endpoints", StringComparison.Ordinal));
            var ddd = namespaces.Any(space =>
                space!.Contains(".Application", StringComparison.Ordinal)
                || space.Contains(".Api", StringComparison.Ordinal));

            (twoLayer && ddd).ShouldBeFalse(
                $"{assembly.GetName().Name} has both an Endpoints namespace and an Application/Api one. "
                + "A module is 2-layer or DDD (conventions.md §2), never halfway.");
        }
    }

    /// <summary>
    /// In a 2-layer module the endpoint calls a service; it does not open the DbContext itself.
    /// </summary>
    /// <remarks>
    /// The DDD rule above says the same thing about an Api namespace, and this is deliberately not the same rule
    /// with a wider net: there, the point is that the domain must not be bypassed. Here there is no domain to
    /// bypass, and the point is that a module's data access stays in one layer — otherwise the RLS-scoped query,
    /// the validation and the outbox enqueue end up in three different places depending on which endpoint ran.
    /// </remarks>
    [Fact]
    public void Two_layer_endpoints_go_through_a_service()
    {
        var result = Types.InAssemblies(PlatformAssemblies)
            .That()
            .ResideInNamespaceMatching(@"Cracra\.Modules\.\w+\.Endpoints")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    /// <summary>
    /// The on-prem model is reached through a port, never directly.
    /// </summary>
    /// <remarks>
    /// S8 asks for this by name, and the reason is not layering purity. The AI client is the one dependency in
    /// the system that is slow, non-deterministic and impossible to run in CI; a handler that named it could not
    /// be tested without either a GPU or a mock of an HTTP client. Behind a port, the whole summary path runs
    /// against the stub through exactly the production code.
    ///
    /// Infrastructure is exempt: that is where the adapter lives, and it must name what it adapts.
    /// </remarks>
    [Fact]
    public void The_AI_client_is_reached_only_through_a_port()
    {
        var result = Types.InAssemblies(PlatformAssemblies)
            .That()
            .ResideInNamespaceMatching(@"Cracra\.Modules\.\w+\.(Application|Domain|Api)")
            .ShouldNot()
            .HaveDependencyOn("Cracra.BuildingBlocks.Ai")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static IEnumerable<Assembly> ModuleAssemblies() =>
        PlatformAssemblies.Where(assembly =>
            assembly.GetName().Name is { } name
            && name.StartsWith("Cracra.Modules.", StringComparison.Ordinal)
            && !name.EndsWith(".Contracts", StringComparison.Ordinal));

    private static bool IsRequestInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>);

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>);

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is { Count: > 0 }
            ? "Offending types: " + string.Join(", ", result.FailingTypeNames)
            : "No offending types reported.";
}
