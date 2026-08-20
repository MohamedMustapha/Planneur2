namespace Cracra.BuildingBlocks.Abstractions;

/// <summary>
/// A business invariant was broken. Maps to 422 — the request was well-formed, and the domain said no.
/// </summary>
/// <remarks>
/// Lives here rather than beside the HTTP handler that maps it so a Domain layer can throw it without taking a
/// dependency on the web stack. The aggregate should not know that its refusal becomes a status code.
/// </remarks>
public class DomainRuleViolationException(string message) : Exception(message);

/// <summary>
/// The row does not exist <em>or</em> row-level security filtered it out.
/// </summary>
/// <remarks>
/// Deliberately indistinguishable. If a caller could tell "you may not see it" from "it is not there", the status
/// code would confirm the existence of projects they have no business knowing about.
/// </remarks>
public class ResourceNotFoundException(string message) : Exception(message);

/// <summary>The row changed since it was read. Maps to 409.</summary>
public class ConcurrencyConflictException(string message) : Exception(message);
