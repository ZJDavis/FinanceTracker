using System.Configuration;
using System.Data;
using System.Net.Http;
using System.Windows;

namespace FinanceTracker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private IDisposable? activeFinanceClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settings = FinanceTracker.Data.DesktopSettings.Load();
            settings.Save();

            // Load SQLite first so the window is useful even while the server is starting.
            var databasePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FinanceTracker", "finance.db");
            var localClient = new FinanceTracker.Data.SqliteFinanceClient(new FinanceTracker.Data.SqliteFinanceStore(databasePath));
            var viewModel = await FinanceTracker.Wpf.ViewModels.TransactionsViewModel.CreateAsync(localClient);
            var window = new FinanceTracker.Wpf.MainWindow(viewModel);
            MainWindow = window;
            window.Show();

            // Backend discovery runs after the local window is visible.
            _ = ConnectToBackendWhenAvailableAsync(window, settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show("FinanceTracker could not open its database. Your existing file has been preserved.\n\n" + ex.Message,
                "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task ConnectToBackendWhenAvailableAsync(
        FinanceTracker.Wpf.MainWindow window,
        FinanceTracker.Data.DesktopSettings settings)
    {
        while (true)
        {
            using var probeClient = new HttpClient
            {
                BaseAddress = new Uri(settings.ApiBaseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(3)
            };

            try
            {
                using var response = await probeClient.GetAsync("health/ready");
                if (response.IsSuccessStatusCode)
                {
                    var apiHttpClient = new HttpClient
                    {
                        BaseAddress = new Uri(settings.ApiBaseUrl, UriKind.Absolute),
                        Timeout = TimeSpan.FromSeconds(15)
                    };
                    var apiClient = new FinanceTracker.Data.HttpFinanceClient(apiHttpClient);
                    var apiViewModel = await FinanceTracker.Wpf.ViewModels.TransactionsViewModel.CreateAsync(apiClient);

                    await Dispatcher.InvokeAsync(() => window.DataContext = apiViewModel);
                    activeFinanceClient = apiClient;
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The backend can be started later; the user can retry without restarting the app.
            }
            catch (TaskCanceledException)
            {
                // A slow or unreachable server is treated like an unavailable server.
            }

            var choice = await Dispatcher.InvokeAsync(() => MessageBox.Show(
                window,
                $"FinanceTracker could not find the backend at {settings.ApiBaseUrl}.\n\n" +
                $"Settings are saved in {FinanceTracker.Data.DesktopSettings.FilePath}.\n\n" +
                "Yes: retry the server check.\nNo: continue in offline mode.\nCancel: close FinanceTracker.",
                "Backend unavailable",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information));

            if (choice == MessageBoxResult.Yes) continue;
            if (choice == MessageBoxResult.Cancel) Shutdown();
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        activeFinanceClient?.Dispose();
        base.OnExit(e);
    }
}

