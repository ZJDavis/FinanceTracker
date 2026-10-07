using System.Configuration;
using System.Data;
using System.Windows;

namespace FinanceTracker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FinanceTracker", "finance.db");
            var store = new FinanceTracker.Data.SqliteFinanceStore(path);
            var window = new FinanceTracker.Wpf.MainWindow(new FinanceTracker.Wpf.ViewModels.TransactionsViewModel(store));
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("FinanceTracker could not open its database. Your existing file has been preserved.\n\n" + ex.Message,
                "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

