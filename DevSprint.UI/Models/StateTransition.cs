namespace DevSprint.UI.Models;

public sealed class StateTransition
{
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public DateTime TransitionDate { get; set; }
    public int DaysInState { get; set; }
}
