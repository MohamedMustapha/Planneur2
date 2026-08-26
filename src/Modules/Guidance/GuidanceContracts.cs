namespace Cracra.Modules.Guidance;

public static class ShellPositions
{
    public const string Member = "member";
    public const string HeadLeaf = "head-leaf";
    public const string HeadBranch = "head-branch";
    public const string HeadTop = "head-top";
    public const string ProductOwner = "po";
    public const string Pmo = "pmo";
    public const string Admin = "admin";
}

public static class ShellSections
{
    public const string Week = "board";
    public const string Node = "node";
    public const string Portfolio = "portfolio";
    public const string Reports = "reports";
    public const string Strategy = "strategy";
    public const string Problems = "problems";
    public const string Meetings = "meetings";
    public const string Kudos = "kudos";
    public const string Finance = "finance";
    public const string Directory = "directory";
    public const string Admin = "admin";
}

public sealed record ShellNavigation(
    string Position,
    string LandingId,
    string FocusId,
    IReadOnlyList<string> Primary,
    IReadOnlyList<string> Secondary);

public sealed record NextAction(
    string Key,
    IReadOnlyDictionary<string, string> Params,
    string Section,
    string ActionKey);

public sealed record Obligation(string Id, string Key, IReadOnlyDictionary<string, string> Params, string Severity);
