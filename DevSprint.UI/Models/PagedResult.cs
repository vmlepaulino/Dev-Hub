namespace DevSprint.UI.Models;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int StartAt { get; init; }
    public bool HasMore => StartAt + Items.Count < Total;
    public int NextStartAt => StartAt + Items.Count;
}
