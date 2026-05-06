using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Auth;
using DevSprint.UI.Auth.GitHub;

namespace DevSprint.UI.ViewModels;

/// <summary>
/// Drives <see cref="Views.DeviceCodeDialog"/>. Performs the GitHub Device Flow
/// poll loop. Exposes the user code, verification URL, and a status message;
/// raises <see cref="Completed"/> when sign-in succeeds, fails, or is cancelled.
/// </summary>
public sealed partial class DeviceCodeViewModel : ObservableObject
{
    private readonly GitHubDeviceFlowClient _deviceFlow;
    private readonly GitHubAuthOptions _options;
    private readonly CancellationTokenSource _cts = new();

    private string _deviceCode = string.Empty;
    private int _pollIntervalSeconds;

    [ObservableProperty] private string _userCode = string.Empty;
    [ObservableProperty] private string _verificationUri = string.Empty;
    [ObservableProperty] private string _statusMessage = "Requesting code from GitHub…";
    [ObservableProperty] private bool _isWorking = true;
    [ObservableProperty] private bool _hasError;

    /// <summary>Raised exactly once when the flow ends. Carries the resulting tokens, or null on cancel/failure.</summary>
    public event EventHandler<AuthTokens?>? Completed;

    public DeviceCodeViewModel(GitHubDeviceFlowClient deviceFlow, GitHubAuthOptions options)
    {
        _deviceFlow = deviceFlow;
        _options = options;
    }

    /// <summary>Kicks off the device-code request and the subsequent poll loop. Call once when the dialog loads.</summary>
    public async Task StartAsync()
    {
        try
        {
            var code = await _deviceFlow.RequestDeviceCodeAsync(_options.ClientId, _options.Scopes, _cts.Token);
            _deviceCode = code.DeviceCode;
            _pollIntervalSeconds = Math.Max(code.Interval, _options.MinimumPollIntervalSeconds);

            UserCode = code.UserCode;
            VerificationUri = code.VerificationUri;
            StatusMessage = "Open the URL, paste the code, then come back here.";

            await PollAsync(code.ExpiresIn);
        }
        catch (OperationCanceledException)
        {
            // user cancelled — Completed already raised by CancelCommand
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Sign-in failed: {ex.Message}";
            IsWorking = false;
            Completed?.Invoke(this, null);
        }
    }

    private async Task PollAsync(int totalSecondsAvailable)
    {
        var deadline = DateTime.UtcNow.AddSeconds(totalSecondsAvailable);

        while (!_cts.Token.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), _cts.Token);

            var result = await _deviceFlow.PollForTokenAsync(_options.ClientId, _deviceCode, _cts.Token);
            switch (result.Outcome)
            {
                case DeviceTokenOutcome.Success:
                    StatusMessage = "Signed in.";
                    IsWorking = false;
                    Completed?.Invoke(this, result.Tokens);
                    return;

                case DeviceTokenOutcome.Pending:
                    // keep polling
                    break;

                case DeviceTokenOutcome.SlowDown:
                    _pollIntervalSeconds += 5;
                    break;

                case DeviceTokenOutcome.Expired:
                    HasError = true;
                    StatusMessage = "The code expired before sign-in completed. Close and try again.";
                    IsWorking = false;
                    Completed?.Invoke(this, null);
                    return;

                case DeviceTokenOutcome.Denied:
                    HasError = true;
                    StatusMessage = "Sign-in was denied on github.com.";
                    IsWorking = false;
                    Completed?.Invoke(this, null);
                    return;

                default:
                    HasError = true;
                    StatusMessage = $"Sign-in failed ({result.ErrorCode}).";
                    IsWorking = false;
                    Completed?.Invoke(this, null);
                    return;
            }
        }

        if (!_cts.Token.IsCancellationRequested)
        {
            HasError = true;
            StatusMessage = "Timed out waiting for sign-in. Close and try again.";
            IsWorking = false;
            Completed?.Invoke(this, null);
        }
    }

    [RelayCommand]
    private void CopyAndOpen()
    {
        if (string.IsNullOrEmpty(UserCode) || string.IsNullOrEmpty(VerificationUri)) return;

        try { Clipboard.SetText(UserCode); }
        catch { /* clipboard access can fail; non-fatal */ }

        try
        {
            Process.Start(new ProcessStartInfo(VerificationUri) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        StatusMessage = "Sign-in cancelled.";
        IsWorking = false;
        Completed?.Invoke(this, null);
    }
}
