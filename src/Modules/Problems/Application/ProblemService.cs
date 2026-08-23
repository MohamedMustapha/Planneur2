using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Problems.Contracts;
using Cracra.Modules.Problems.Data;
using Cracra.Modules.Problems.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Problems.Application;

public sealed record FileProblemRequest(
    string Title,
    string? Description,
    string? Category,
    string? OriginScopeType,
    Guid? OriginScopeId,
    Guid? NodeId,
    decimal? ImpactTimeLoss,
    string? ImpactFrequency,
    int? AffectedPeopleEstimate);

public sealed record TriageRequest(string Decision, string? Reason, Guid? DuplicateOf);

public sealed record ConvertRequest(string? Type, string? Category, Guid? OwnerNodeId);

public sealed record ProblemFilter(string? Scope, string? Category, string? Status, string? Sort, int Limit);

/// <param name="Suggestions">
/// Problems that already sound like this one. Returned on filing rather than blocking it: the point is that
/// somebody finds an existing pain, not that the form argues with them.
/// </param>
public sealed record FiledProblem(Guid Id, IReadOnlyList<ProblemCard> Suggestions);

public interface IProblemService
{
    Task<FiledProblem> FileAsync(FileProblemRequest request, CancellationToken ct);

    Task<IReadOnlyList<ProblemCard>> ListAsync(ProblemFilter filter, CancellationToken ct);

    Task<ProblemDetail> DetailAsync(Guid problemId, CancellationToken ct);

    Task<IReadOnlyList<ProblemCard>> SearchAsync(string query, int limit, CancellationToken ct);

    Task<Guid> ProposeAsync(Guid problemId, string description, decimal? effortGuess, CancellationToken ct);

    Task<bool> VoteAsync(Guid problemId, CancellationToken ct);

    Task<Guid> CommentAsync(Guid problemId, string body, CancellationToken ct);

    Task TriageAsync(Guid problemId, TriageRequest request, CancellationToken ct);

    Task<Guid> ConvertAsync(Guid problemId, ConvertRequest request, CancellationToken ct);

    Task ResolveAsync(Guid problemId, string? note, CancellationToken ct);
}

/// <summary>
/// The Problems module's one service.
/// </summary>
/// <remarks>
/// Thin on purpose: the lifecycle rules live on the aggregate, and what is left here is gathering the evidence it
/// cannot gather for itself — where the reporter sits, whether the item was created — plus the projections the
/// board draws.
/// </remarks>
internal sealed class ProblemService(
    ProblemsDbContext context,
    IOrgNodeReader nodes,
    IDirectoryReader directory,
    IPortfolioPort portfolio,
    IUserContext user) : IProblemService
{
    public async Task<FiledProblem> FileAsync(FileProblemRequest request, CancellationToken ct)
    {
        var node = request.NodeId
                   ?? user.NodeId
                   ?? (await nodes.GetHomeScopeAsync(user.UserId, ct))?.NodeId
                   ?? throw new DomainRuleViolationException(
                       "Your session does not say where you sit, so this problem has nowhere to belong.");

        var now = DateTimeOffset.UtcNow;

        var problem = Problem.File(
            await CodeAsync(ct),
            request.Title,
            request.Description,
            request.Category ?? ProblemCategories.Other,
            request.OriginScopeType ?? OriginScopes.Node,
            request.OriginScopeId ?? node,
            node,
            user.UserId,
            request.ImpactTimeLoss,
            ImpactFrequencies.Parse(request.ImpactFrequency ?? ImpactFrequencies.Occasional),
            request.AffectedPeopleEstimate,
            now);

        context.Problems.Add(problem);

        await context.SaveChangesAsync(ct);

        // The duplicate hint §05.3 asks for, answered after the fact. Refusing to file until somebody has read a
        // list is how a "report a problem" button stops being pressed.
        return new FiledProblem(problem.Id, await SearchAsync(request.Title, 5, ct, problem.Id));
    }

    public async Task<IReadOnlyList<ProblemCard>> ListAsync(ProblemFilter filter, CancellationToken ct)
    {
        var query = context.Problems.AsNoTracking();

        if (filter.Category is { Length: > 0 } category)
        {
            query = query.Where(problem => problem.Category == category);
        }

        if (filter.Status is { Length: > 0 } status)
        {
            var parsed = ParseStatus(status);
            query = query.Where(problem => problem.Status == parsed);
        }

        if (filter.Scope is { Length: > 0 } scope && Guid.TryParse(scope, out var scopeId))
        {
            query = query.Where(problem => problem.NodeId == scopeId);
        }

        var problems = await query
            .Include(problem => problem.Votes)
            .Include(problem => problem.Proposals)
            .AsSplitQuery()
            .ToListAsync(ct);

        var ordered = filter.Sort?.Trim().ToLowerInvariant() switch
        {
            "votes" => problems.OrderByDescending(problem => problem.VoteCount)
                .ThenByDescending(problem => problem.CreatedAt),
            "recent" => problems.OrderByDescending(problem => problem.CreatedAt),
            _ => problems.OrderByDescending(problem => problem.AnnualHoursLost)
                .ThenByDescending(problem => problem.VoteCount),
        };

        return await CardsAsync([.. ordered.Take(filter.Limit)], ct);
    }

    public async Task<ProblemDetail> DetailAsync(Guid problemId, CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        var card = (await CardsAsync([problem], ct)).Single();

        var people = await directory.GetPersonNamesAsync(
            [
                .. problem.Proposals.Select(proposal => proposal.AuthorPersonId)
                    .Concat(problem.Comments.Select(comment => comment.AuthorPersonId))
                    .Distinct(),
            ],
            ct);

        return new ProblemDetail(
            card,
            [
                .. problem.Proposals
                    .OrderBy(proposal => proposal.CreatedAt)
                    .Select(proposal => new ProposalView(
                        proposal.Id,
                        proposal.AuthorPersonId,
                        people.GetValueOrDefault(proposal.AuthorPersonId),
                        proposal.Description,
                        proposal.EffortGuess,
                        proposal.CreatedAt)),
            ],
            [
                .. problem.Comments
                    .OrderBy(comment => comment.CreatedAt)
                    .Select(comment => new ProblemCommentView(
                        comment.Id,
                        comment.AuthorPersonId,
                        people.GetValueOrDefault(comment.AuthorPersonId),
                        comment.Body,
                        comment.CreatedAt)),
            ]);
    }

    public async Task<IReadOnlyList<ProblemCard>> SearchAsync(string query, int limit, CancellationToken ct) =>
        await SearchAsync(query, limit, ct, null);

    public async Task<Guid> ProposeAsync(
        Guid problemId,
        string description,
        decimal? effortGuess,
        CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        var proposal = problem.Propose(user.UserId, description, effortGuess, DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(ct);

        return proposal.Id;
    }

    public async Task<bool> VoteAsync(Guid problemId, CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        var counted = problem.Vote(user.UserId, DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(ct);

        return counted;
    }

    public async Task<Guid> CommentAsync(Guid problemId, string body, CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        var comment = problem.Comment(user.UserId, body, DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(ct);

        return comment.Id;
    }

    public async Task TriageAsync(Guid problemId, TriageRequest request, CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        problem.Triage(request.Decision, request.Reason, request.DuplicateOf, DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Turns an accepted problem into a portfolio item, pre-filled from the pain (v2 §05.2).
    /// </summary>
    /// <remarks>
    /// The item is created through Portfolio's own contract on the caller's session, so who may own it is decided
    /// by that module's rules and not restated here. The two ends are linked in the same transaction — an item
    /// created without the link, or a problem marked converted without an item, is the shadow-IT hole this whole
    /// slice exists to close.
    /// </remarks>
    public async Task<Guid> ConvertAsync(Guid problemId, ConvertRequest request, CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        if (problem.Status is not ProblemStatus.Accepted)
        {
            // Asked before creating anything, so a refused conversion does not leave an orphan item behind.
            throw new DomainRuleViolationException(
                $"Only an accepted problem becomes a portfolio item; this one is {ProblemStatuses.Wire(problem.Status)}.");
        }

        var itemId = await portfolio.CreateFromProblemAsync(
            problem.Title,
            problem.Description,
            request.Type,
            request.Category ?? SuggestedCategory(problem.Category),
            request.OwnerNodeId ?? user.NodeId ?? problem.NodeId,
            ct);

        problem.Convert(itemId, DateTimeOffset.UtcNow);

        context.Enqueue(new ProblemConverted(problem.Id, itemId, problem.Title));

        await context.SaveChangesAsync(ct);

        return itemId;
    }

    public async Task ResolveAsync(Guid problemId, string? note, CancellationToken ct)
    {
        var problem = await LoadAsync(problemId, ct);

        problem.Resolve(note, DateTimeOffset.UtcNow);

        context.Enqueue(new ProblemResolved(problem.Id, problem.ConvertedItemId));

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The category the item starts with, suggested from the pain's own.
    /// </summary>
    /// <remarks>
    /// A suggestion, overridable in the request. Categories are the deployment's vocabulary, so this maps the two
    /// the platform does fix — tooling and data — and otherwise says nothing rather than inventing a word.
    /// </remarks>
    private static string? SuggestedCategory(string problemCategory) => problemCategory switch
    {
        ProblemCategories.Tooling => "tooling",
        ProblemCategories.Data => "data",
        _ => null,
    };

    private async Task<IReadOnlyList<ProblemCard>> SearchAsync(
        string query,
        int limit,
        CancellationToken ct,
        Guid? excluding)
    {
        var terms = Terms(query);

        if (terms.Length == 0)
        {
            return [];
        }

        var patterns = terms.Select(term => $"%{term}%").ToArray();

        // ILIKE ANY rather than a chain of ORs built in C#: one parameter, one plan, and the number of terms stops
        // being something the query shape depends on. RLS applies to it exactly as to any other read.
        var found = await context.Problems
            .FromSql($@"select * from problems.problem p
                        where p.title ilike any({patterns})
                           or (p.description is not null and p.description ilike any({patterns}))")
            .AsNoTracking()
            .Where(problem => excluding == null || problem.Id != excluding)
            .Include(problem => problem.Votes)
            .Include(problem => problem.Proposals)
            .AsSplitQuery()
            .OrderByDescending(problem => problem.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return await CardsAsync(found, ct);
    }

    /// <summary>
    /// The words worth matching on.
    /// </summary>
    /// <remarks>
    /// Matching the whole phrase would find nothing: "ticket closing is slow" and "ticket closing takes too many
    /// steps" are the same pain and share no substring long enough to hit. Short words are dropped because
    /// matching on "the" would return the entire backlog, which is the same as returning nothing.
    /// </remarks>
    private static readonly char[] Separators =
        [' ', ',', '.', ';', ':', '!', '?', '\'', '"', '(', ')', '-', '/'];

    private static string[] Terms(string query) =>
        [
            .. query
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Trim())
                .Where(word => word.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6),
        ];

    private async Task<Problem> LoadAsync(Guid problemId, CancellationToken ct) =>
        await context.Problems
            .Include(problem => problem.Proposals)
            .Include(problem => problem.Votes)
            .Include(problem => problem.Comments)
            .AsSplitQuery()
            .AsTracking()
            .SingleOrDefaultAsync(problem => problem.Id == problemId, ct)
        ?? throw new ResourceNotFoundException($"No problem {problemId}.");

    private async Task<IReadOnlyList<ProblemCard>> CardsAsync(
        IReadOnlyList<Problem> problems,
        CancellationToken ct)
    {
        if (problems.Count == 0)
        {
            return [];
        }

        var names = await directory.GetPersonNamesAsync(
            [.. problems.Select(problem => problem.ReporterPersonId).Distinct()],
            ct);

        return
        [
            .. problems.Select(problem => new ProblemCard(
                problem.Id,
                problem.Code,
                problem.Title,
                problem.Description,
                problem.Category,
                problem.OriginScopeType,
                problem.OriginScopeId,
                problem.NodeId,
                problem.ReporterPersonId,
                names.GetValueOrDefault(problem.ReporterPersonId),
                problem.ImpactTimeLoss,
                problem.ImpactFrequency.ToString().ToLowerInvariant(),
                problem.AffectedPeopleEstimate,
                problem.AnnualHoursLost,
                problem.VoteCount,
                problem.Proposals.Count,
                problem.Votes.Any(vote => vote.PersonId == user.UserId),
                ProblemStatuses.Wire(problem.Status),
                problem.ConvertedItemId,
                problem.DuplicateOfProblemId,
                problem.DecisionReason,
                problem.CreatedAt)),
        ];
    }

    private async Task<string> CodeAsync(CancellationToken ct)
    {
        var year = DateTimeOffset.UtcNow.Year;

        var taken = await context.Problems
            .AsNoTracking()
            .CountAsync(problem => problem.Code.StartsWith($"PB-{year}-"), ct);

        return $"PB-{year}-{taken + 1:000}";
    }

    private static ProblemStatus ParseStatus(string status) => status.Trim().ToLowerInvariant() switch
    {
        ProblemStatuses.New => ProblemStatus.New,
        ProblemStatuses.Triaged => ProblemStatus.Triaged,
        ProblemStatuses.Accepted => ProblemStatus.Accepted,
        ProblemStatuses.Converted => ProblemStatus.Converted,
        ProblemStatuses.Resolved => ProblemStatus.Resolved,
        ProblemStatuses.Declined => ProblemStatus.Declined,
        ProblemStatuses.Duplicate => ProblemStatus.Duplicate,
        _ => throw new DomainRuleViolationException($"'{status}' is not a problem status."),
    };
}

/// <summary>What Problems needs from Portfolio when a pain becomes work.</summary>
public interface IPortfolioPort
{
    Task<Guid> CreateFromProblemAsync(
        string title,
        string? description,
        string? type,
        string? category,
        Guid ownerNodeId,
        CancellationToken ct);
}
