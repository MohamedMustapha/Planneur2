namespace Cracra.Modules.Directory.Domain;

public sealed class OrgLevel
{
    public required int LevelNo { get; init; }

    public required string Code { get; set; }

    public required string LabelKey { get; set; }

    public required string LabelPluralKey { get; set; }

    public required string HeadLabelKey { get; set; }

    public bool PeopleAllowed { get; set; } = true;

    public bool IsOptional { get; set; }
}

public sealed class OrgNode
{
    public required Guid Id { get; init; }

    public Guid? ParentId { get; set; }

    public required int LevelNo { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public Guid[] AncestorIds { get; private set; } = [];

    public Guid? HeadPersonId { get; set; }

    public Guid? ProfileId { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }
}
