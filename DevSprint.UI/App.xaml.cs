using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;
using DevSprint.UI.Auth;
using DevSprint.UI.Auth.GitHub;
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

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets<App>(optional: true)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);

            // ── Auth ────────────────────────────────────────────────────────
            ConfigureAuth(services, configuration);

            // ── Domain services ─────────────────────────────────────────────
            services.AddSingleton<IIdentityService, IdentityService>();
            services.AddHttpClient<IJiraService, JiraService>();

            // GitHubService gets its Authorization header from the bearer handler
            // (see ConfigureAuth) — the service constructor no longer touches tokens.
            services.AddHttpClient<IGitHubService, GitHubService>()
                    .AddHttpMessageHandler<GitHubBearerTokenHandler>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // ── Eager sign-in before the main window appears ────────────────
            var gitHubAuth = _serviceProvider.GetRequiredService<IGitHubAuthService>();
            var signedIn = await gitHubAuth.EnsureSignedInAsync();
            if (!signedIn)
            {
                MessageBox.Show(
                    "GitHub sign-in is required to use DevSprint. The application will close.",
                    "Sign-in cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
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
        }
    }
}
