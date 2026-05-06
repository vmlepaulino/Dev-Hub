using Microsoft.Extensions.Configuration;

namespace DevSprint.UI.Onboarding;

/// <summary>
/// Default <see cref="IOnboardingService"/> implementation. Inspects any
/// <see cref="IConfiguration"/> to detect missing values; persists wizard
/// answers into the DPAPI-encrypted store at
/// <c>%AppData%\TeamHub\config.dat</c> via <see cref="EncryptedConfigurationStore"/>.
/// </summary>
public sealed class OnboardingService : IOnboardingService
{
    private readonly EncryptedConfigurationStore _store;

    public OnboardingService() : this(new EncryptedConfigurationStore()) { }

    public OnboardingService(EncryptedConfigurationStore store)
    {
        _store = store;
    }

    public bool NeedsOnboarding(IConfiguration configuration)
    {
        foreach (var field in OnboardingCatalog.AllFields)
        {
            if (!field.IsRequired) continue;
            if (string.IsNullOrWhiteSpace(GetCurrentValue(configuration, field))) return true;
        }
        return false;
    }

    public IReadOnlyDictionary<string, string> Snapshot(IConfiguration configuration)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in OnboardingCatalog.AllFields)
        {
            snapshot[field.Key] = GetCurrentValue(configuration, field);
        }
        return snapshot;
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        // Build the flat dictionary the encrypted store wants. Arrays expand to
        // GitHub:Repositories:0, GitHub:Repositories:1, … (the same shape .NET
        // configuration uses for indexed children).
        var updates = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var field in OnboardingCatalog.AllFields)
        {
            if (!values.TryGetValue(field.Key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
                continue;

            if (field.IsArray)
            {
                // Wipe any prior elements first so the new list isn't merged with stale entries.
                await _store.RemoveArrayAsync(field.Key, cancellationToken);

                var items = SplitCsv(rawValue);
                for (int i = 0; i < items.Count; i++)
                    updates[$"{field.Key}:{i}"] = items[i];
            }
            else
            {
                updates[field.Key] = rawValue.Trim();
            }
        }

        await _store.SaveAsync(updates, cancellationToken);
    }

    /// <summary>
    /// Reads either a flat value (<c>Jira:BaseUrl</c>) from configuration, or — for
    /// arrays — joins the indexed children back into a comma-separated string
    /// suitable for the wizard's text input.
    /// </summary>
    private static string GetCurrentValue(IConfiguration configuration, OnboardingFieldDefinition field)
    {
        if (field.IsArray)
        {
            var section = configuration.GetSection(field.Key);
            var children = section.GetChildren()
                                  .Select(c => c.Value ?? string.Empty)
                                  .Where(v => !string.IsNullOrWhiteSpace(v))
                                  .ToList();
            return children.Count > 0 ? string.Join(", ", children) : string.Empty;
        }

        return configuration[field.Key] ?? string.Empty;
    }

    private static IReadOnlyList<string> SplitCsv(string raw) =>
        raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
