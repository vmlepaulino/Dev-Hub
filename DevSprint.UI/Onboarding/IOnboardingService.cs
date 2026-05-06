using Microsoft.Extensions.Configuration;

namespace DevSprint.UI.Onboarding;

/// <summary>
/// Inspects the resolved configuration to decide whether the wizard needs to
/// run, and persists the user's responses back to user-secrets.
/// </summary>
public interface IOnboardingService
{
    /// <summary>
    /// True when at least one required field defined in <see cref="OnboardingCatalog"/>
    /// is missing or empty in <paramref name="configuration"/>.
    /// </summary>
    bool NeedsOnboarding(IConfiguration configuration);

    /// <summary>
    /// Captures the current value of every catalogue field from <paramref name="configuration"/>,
    /// suitable for pre-filling the wizard form.
    /// </summary>
    IReadOnlyDictionary<string, string> Snapshot(IConfiguration configuration);

    /// <summary>
    /// Persists the supplied values to user-secrets. Keys are nested into the
    /// JSON document by their colon segments. Array fields are written as JSON
    /// arrays, splitting on commas in the supplied string.
    /// </summary>
    Task SaveAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
}
