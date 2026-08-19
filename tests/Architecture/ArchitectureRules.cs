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
    ];

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

    [Fact]
    public void No_module_references_another_modules_internals()
    {
        var moduleTypes = PlatformAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace?.StartsWith("Cracra.Modules.", StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var type in moduleTypes)
        {
            var ownModule = type.Namespace!.Split('.')[2];

            var forbidden = Types.InAssemblies(PlatformAssemblies)
                .That()
                .ResideInNamespace(type.Namespace)
                .ShouldNot()
                .HaveDependencyOnAny(
                    [.. moduleTypes
                        .Select(other => other.Namespace!)
                        .Where(ns => ns.Split('.')[2] != ownModule)
                        // Contracts are the sanctioned door between modules: integration events and read DTOs.
                        .Where(ns => !ns.Contains(".Contracts", StringComparison.Ordinal))
                        .Distinct()])
                .GetResult();

            forbidden.IsSuccessful.ShouldBeTrue(Describe(forbidden));
        }
    }

    private static bool IsRequestInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>);

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>);

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is { Count: > 0 }
            ? "Offending types: " + string.Join(", ", result.FailingTypeNames)
            : "No offending types reported.";
}
