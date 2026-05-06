using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;
using DevSprint.UI.Auth;
using DevSprint.UI.Auth.GitHub;
using DevSprint.UI.Auth.Jira;
using DevSprint.UI.Onboarding;
using DevSprint.UI.Services;
using DevSprint.UI.ViewModels;
using DevSprint.UI.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevSprint.UI
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            var culture = new CultureInfo("en-GB");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            base.OnStartup(e);

            var configuration = BuildConfiguration();

            // ── Onboarding (runs before any auth or DI is built) ───────────
            // The auth services validate config in their constructors and would
            // throw if anything were missing, so we have to gather it first.
            var onboarding = new OnboardingService();
            if (onboarding.NeedsOnboarding(configuration))
            {
                if (!ShowOnboardingWizard(onboarding, configuration))
                {
                    Shutdown();
                    return;
                }
                // Re-read configuration so the freshly written user-secrets values are live.
                configuration = BuildConfiguration();

                if (onboarding.NeedsOnboarding(configuration))
                {
                    MessageBox.Show(
                        "Setup completed but some required values are still missing. The application will close.",
                        "Setup incomplete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Shutdown();
                    return;
                }
            }

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IOnboardingService>(onboarding);

            // ── Auth ────────────────────────────────────────────────────────
            ConfigureAuth(services, configuration);

            // ── Domain services ─────────────────────────────────────────────
            services.AddSingleton<IIdentityService, IdentityService>();

            // JiraService gets:
            //  - JiraBearerTokenHandler: stamps Authorization: Bearer <access_token>
            //  - JiraApiBaseUriHandler: rewrites /rest/... → /ex/jira/{cloudId}/rest/...
            // Constructor no longer touches tokens or the site URL.
            services.AddHttpClient<IJiraService, JiraService>()
                    .AddHttpMessageHandler<JiraBearerTokenHandler>()
                    .AddHttpMessageHandler<JiraApiBaseUriHandler>();

            // GitHubService gets its Authorization header from the bearer handler
            // (see ConfigureAuth) — the service constructor no longer touches tokens.
            services.AddHttpClient<IGitHubService, GitHubService>()
                    .AddHttpMessageHandler<GitHubBearerTokenHandler>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // ── Eager sign-in before the main window appears ────────────────
            // Order: GitHub first (smaller scope), then Jira. Each is independent;
            // if either is cancelled we shut down — both are required for the app.
            var gitHubAuth = _serviceProvider.GetRequiredService<IGitHubAuthService>();
            if (!await gitHubAuth.EnsureSignedInAsync())
            {
                ShowSignInCancelledMessage("GitHub");
                Shutdown();
                return;
            }

            var jiraAuth = _serviceProvider.GetRequiredService<IJiraAuthService>();
            if (!await jiraAuth.EnsureSignedInAsync())
            {
                ShowSignInCancelledMessage("Jira");
                Shutdown();
                return;
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private static void ShowSignInCancelledMessage(string platform) =>
            MessageBox.Show(
                $"{platform} sign-in is required to use DevSprint. The application will close.",
                "Sign-in cancelled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

        /// <summary>
        /// Builds the layered configuration. Order matters — last source wins:
        /// <list type="number">
        ///   <item><c>appsettings.json</c> — non-sensitive, shared defaults (OAuth scope strings).</item>
        ///   <item><c>user-secrets</c> — kept as a one-pass migration fallback for installs that haven't run the wizard yet. Optional.</item>
        ///   <item><c>EncryptedConfigurationStore</c> — DPAPI-encrypted file at <c>%AppData%\TeamHub\config.dat</c>. The canonical store; the wizard writes here.</item>
        /// </list>
        /// </summary>
        private static IConfiguration BuildConfiguration()
        {
            var encryptedStore = new EncryptedConfigurationStore();

            return new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets<App>(optional: true)
                .AddInMemoryCollection(encryptedStore.Load())
                .Build();
        }

        /// <summary>
        /// Shows the setup wizard modally. Returns true if the user saved a
        /// complete config, false if they cancelled or closed the dialog.
        /// </summary>
        private static bool ShowOnboardingWizard(IOnboardingService service, IConfiguration current)
        {
            var snapshot = service.Snapshot(current);
            var vm = new OnboardingWizardViewModel(service, snapshot);
            var dialog = new OnboardingWizardDialog(vm);

            // No real MainWindow exists yet at this point — center on screen.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return dialog.ShowDialog() == true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Registers everything in <c>Auth/</c>: options, token store, device-flow
        /// HTTP client, auth service, bearer handler, and the interactive sign-in
        /// delegate that opens <see cref="DeviceCodeDialog"/>.
        /// </summary>
        private static void ConfigureAuth(IServiceCollection services, IConfiguration configuration)
        {
            // Bind GitHub:OAuth section to options.
            services.Configure<GitHubAuthOptions>(configuration.GetSection(GitHubAuthOptions.SectionName));

            // Token persistence — shared with future Jira auth.
            services.AddSingleton<EncryptedTokenStore>();

            // Dedicated HttpClient for github.com endpoints (NOT api.github.com).
            // Importantly, this client does NOT pass through the bearer handler,
            // otherwise we'd recurse trying to fetch a token to fetch a token.
            services.AddHttpClient<GitHubDeviceFlowClient>(GitHubDeviceFlowClient.HttpClientName, c =>
            {
                c.BaseAddress = new Uri("https://github.com/");
            });

            // Interactive sign-in: opens the WPF dialog on the UI thread, returns the result.
            services.AddSingleton<InteractiveSignInDelegate>(sp => async (options, ct) =>
            {
                var deviceFlow = sp.GetRequiredService<GitHubDeviceFlowClient>();

                // Marshal to UI thread — auth may be triggered from a background
                // continuation when the bearer handler refreshes mid-request.
                AuthTokens? result = null;
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    var vm = new DeviceCodeViewModel(deviceFlow, options);
                    var dialog = new DeviceCodeDialog(vm);

                    // At eager startup the real MainWindow hasn't been instantiated
                    // yet — WPF will treat the first created window as MainWindow,
                    // which is this dialog. Setting Owner to itself throws.
                    var owner = Current.MainWindow;
                    if (owner is not null && !ReferenceEquals(owner, dialog))
                    {
                        dialog.Owner = owner;
                    }
                    else
                    {
                        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }

                    dialog.ShowDialog();
                    result = dialog.Tokens;
                });
                return result;
            });

            services.AddSingleton<IGitHubAuthService, GitHubAuthService>();

            // The bearer handler must be transient — IHttpClientFactory disposes
            // it together with the typed client lifetime.
            services.AddTransient<GitHubBearerTokenHandler>();

            // ── Jira OAuth 2.0 (3LO) ─────────────────────────────────────────
            services.Configure<JiraAuthOptions>(configuration.GetSection(JiraAuthOptions.SectionName));

            // Dedicated HttpClient for auth.atlassian.com / api.atlassian.com
            // OAuth endpoints. Like GitHub, this client does NOT pass through
            // the bearer handler — it's the source of tokens.
            services.AddHttpClient<JiraOAuthClient>(JiraOAuthClient.HttpClientName);

            // Interactive sign-in: opens JiraSignInDialog on the UI thread.
            services.AddSingleton<JiraInteractiveSignInDelegate>(sp => async (options, ct) =>
            {
                var oauthClient = sp.GetRequiredService<JiraOAuthClient>();
                var configuration = sp.GetRequiredService<IConfiguration>();

                AuthTokens? result = null;
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    var vm = new JiraSignInViewModel(oauthClient, options, configuration);
                    var dialog = new JiraSignInDialog(vm);

                    var owner = Current.MainWindow;
                    if (owner is not null && !ReferenceEquals(owner, dialog))
                    {
                        dialog.Owner = owner;
                    }
                    else
                    {
                        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }

                    dialog.ShowDialog();
                    result = dialog.Tokens;
                });
                return result;
            });

            services.AddSingleton<IJiraAuthService, JiraAuthService>();

            services.AddTransient<JiraBearerTokenHandler>();
            services.AddTransient<JiraApiBaseUriHandler>();
        }
    }
}
