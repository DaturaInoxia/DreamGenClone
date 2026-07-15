using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Selects which characters should speak on an overflow continue click.
/// Returns an ordered list of candidate names from the available character pool.
///
/// Decision path priority:
/// 1. Cache fingerprint match → cached ordering rotated by recency.
/// 2. Model configured → LLM call, parse, cache.
/// 3. No model configured → scoring-ordered candidates (base path).
/// 4. LLM fails → scoring-ordered candidates (explicit fallback).
///
/// No-fallback compliance: scoring IS the base path, never a hidden alternate.
/// </summary>
public interface IActorSelectionService
{
    /// <summary>
    /// Selects up to <paramref name="request"/>'s <c>BatchSize</c> characters from
    /// <paramref name="request"/>'s <c>Candidates</c>, in the order they should speak.
    /// </summary>
    Task<Models.ActorSelectionResponse> SelectActorsAsync(
        Models.ActorSelectionRequest request,
        CancellationToken cancellationToken = default);
}
