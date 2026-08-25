using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;
using Cracra.Modules.Integrations.Sync;

namespace Cracra.Tests.Unit.Integrations;

/// <summary>
/// Turning a provider's snapshot into a mirror row.
/// </summary>
/// <remarks>
/// The part of sync worth testing exhaustively, and the part with no I/O in it: which local project or unit an
/// item lands in, whose it is, and what happens the second time the same item comes back. Everything that talks
/// to a database or a provider is covered in the integration suite instead, where it can be true rather than
/// mocked.
/// </remarks>
public sealed class MirrorMapperTests
{
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Project = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherProject = Guid.Parse("d0000000-0000-0000-0000-000000000002");
    private static readonly Guid Unit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Camille = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_area_path_resolves_to_the_project_it_is_mapped_to()
    {
        var (projectId, unitId) = MirrorMapper.ResolveTarget(
            [new MappingHint(MappingKinds.AreaPath, @"CRACRA\Platform")],
            [Mapping(MappingKinds.AreaPath, @"CRACRA\Platform", project: Project)]);

        projectId.ShouldBe(Project);
        unitId.ShouldBeNull();
    }

    [Fact]
    public void The_area_wins_over_the_iteration_when_a_team_maps_both()
    {
        // The DevOps adapter offers the area first because an area outlives the fortnight an iteration is named
        // for. A team that maps both has said the same thing twice; the stable half is the one to believe.
        var (projectId, _) = MirrorMapper.ResolveTarget(
            [
                new MappingHint(MappingKinds.AreaPath, @"CRACRA\Platform"),
                new MappingHint(MappingKinds.Iteration, @"CRACRA\Sprint 42"),
            ],
            [
                Mapping(MappingKinds.Iteration, @"CRACRA\Sprint 42", project: OtherProject),
                Mapping(MappingKinds.AreaPath, @"CRACRA\Platform", project: Project),
            ]);

        projectId.ShouldBe(Project);
    }

    [Fact]
    public void An_assignment_group_resolves_to_a_unit()
    {
        var (projectId, unitId) = MirrorMapper.ResolveTarget(
            [new MappingHint(MappingKinds.AssignmentGroup, "Helpdesk N1")],
            [Mapping(MappingKinds.AssignmentGroup, "Helpdesk N1", unit: Unit)]);

        unitId.ShouldBe(Unit);
        projectId.ShouldBeNull();
    }

    [Fact]
    public void Matching_a_mapping_ignores_case_but_not_kind()
    {
        // Case, because the two systems disagree with themselves about it and an administrator typing an area
        // path by hand should not have to match DevOps' capitalization exactly.
        var (byCase, _) = MirrorMapper.ResolveTarget(
            [new MappingHint(MappingKinds.AreaPath, @"cracra\platform")],
            [Mapping(MappingKinds.AreaPath, @"CRACRA\Platform", project: Project)]);

        byCase.ShouldBe(Project);

        // Kind, because "Platform" as an area and "Platform" as an assignment group are two different things
        // that happen to share a word.
        var (byKind, _) = MirrorMapper.ResolveTarget(
            [new MappingHint(MappingKinds.AreaPath, "Platform")],
            [Mapping(MappingKinds.AssignmentGroup, "Platform", unit: Unit)]);

        byKind.ShouldBeNull();
    }

    [Fact]
    public void An_unmapped_item_still_mirrors_with_no_target()
    {
        var item = MirrorMapper.Create(Snapshot(), Connection(), [], People(), Now);

        // Refusing to mirror it would mean a developer's own assigned task vanishing from their dropdown because
        // somebody had not finished the mapping table — and the failure would look like a broken integration.
        item.ProjectId.ShouldBeNull();
        item.UnitId.ShouldBeNull();

        // The branch is the backstop scope: an item nothing maps is still somebody's to look at.
        item.NodeId.ShouldBe(Department);
        item.MirrorState.ShouldBe(MirrorStates.Open);
    }

    [Fact]
    public void An_assignee_the_directory_knows_is_resolved_to_a_person()
    {
        var item = MirrorMapper.Create(Snapshot(), Connection(), [], People(), Now);

        item.AssignedPersonId.ShouldBe(Camille);
        item.AssignedToLdapUid.ShouldBe("camille.villeneuve");
    }

    [Fact]
    public void An_assignee_it_does_not_know_keeps_the_uid_and_stays_unresolved()
    {
        var item = MirrorMapper.Create(
            Snapshot(assignedTo: "external.contractor"),
            Connection(),
            [],
            People(),
            Now);

        // The contractor case. Visibly assigned to somebody the platform has never heard of beats looking free
        // and being dragged out of the pool by a lead who thinks it is.
        item.AssignedPersonId.ShouldBeNull();
        item.AssignedToLdapUid.ShouldBe("external.contractor");
    }

    [Fact]
    public void A_reassigned_item_stops_belonging_to_whoever_had_it()
    {
        var item = MirrorMapper.Create(Snapshot(), Connection(), [], People(), Now);

        MirrorMapper.Apply(item, Snapshot(assignedTo: null), Connection(), [], People(), Now.AddHours(1));

        // The source is authoritative about all of it. Keeping the old assignee because it was set once is how a
        // ticket somebody handed over goes on showing in the first agent's dropdown indefinitely.
        item.AssignedPersonId.ShouldBeNull();
        item.AssignedToLdapUid.ShouldBeNull();
    }

    [Fact]
    public void Seeing_a_closed_item_again_reopens_it()
    {
        var item = MirrorMapper.Create(Snapshot(), Connection(), [], People(), Now);

        item.MirrorState = MirrorStates.Closed;
        item.ClosedAt = Now;

        MirrorMapper.Apply(item, Snapshot(state: "Active"), Connection(), [], People(), Now.AddDays(1));

        // An incident reopened at the source is live work again, and leaving it closed here would hide it from
        // the very pool that exists to surface it.
        item.MirrorState.ShouldBe(MirrorStates.Open);
        item.ClosedAt.ShouldBeNull();
        item.SyncedAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void An_item_that_moved_area_moves_project_with_it()
    {
        var mappings = new[]
        {
            Mapping(MappingKinds.AreaPath, @"CRACRA\Platform", project: Project),
            Mapping(MappingKinds.AreaPath, @"CRACRA\Tooling", project: OtherProject),
        };

        var item = MirrorMapper.Create(Snapshot(), Connection(), mappings, People(), Now);

        item.ProjectId.ShouldBe(Project);

        MirrorMapper.Apply(item, Snapshot(area: @"CRACRA\Tooling"), Connection(), mappings, People(), Now);

        item.ProjectId.ShouldBe(OtherProject);
    }

    // --- Fixture ---------------------------------------------------------------------------------------------------

    private static ExternalWorkItemSnapshot Snapshot(
        string? assignedTo = "camille.villeneuve",
        string state = "New",
        string area = @"CRACRA\Platform") =>
        new(
            "4301",
            "AB#4301",
            "Migrer le socle vers .NET 10",
            "Task",
            state,
            assignedTo,
            "Sprint 42",
            [new MappingHint(MappingKinds.AreaPath, area)],
            "https://devops.intranet/CRACRA/_workitems/edit/4301",
            6m,
            Now.AddHours(-1));

    private static ExternalConnection Connection() => new()
    {
        Id = Guid.Parse("e0000000-0000-0000-0000-000000000001"),
        NodeId = Department,
        Provider = ExternalProviders.AzureDevOps,
        Name = "IS — DevOps",
        BaseUrl = "https://devops.intranet",
        AuthRef = "is-devops",
        ProjectOrQueue = "CRACRA",
        CurrentSprint = "Sprint 42",
    };

    private static ExternalMapping Mapping(string kind, string value, Guid? project = null, Guid? unit = null) => new()
    {
        Id = Guid.CreateVersion7(),
        ConnectionId = Guid.Parse("e0000000-0000-0000-0000-000000000001"),
        NodeId = Department,
        Kind = kind,
        ExternalValue = value,
        ProjectId = project,
        UnitId = unit,
    };

    private static Dictionary<string, Guid> People() =>
        new(StringComparer.OrdinalIgnoreCase) { ["camille.villeneuve"] = Camille };
}
