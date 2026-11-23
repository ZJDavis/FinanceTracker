using FinanceTracker.Data;
using System.Windows;

namespace FinanceTracker.Wpf;

/// <summary>
/// Shows parsed CSV rows so the user can decide what to import.
/// </summary>
public partial class CsvImportPreviewWindow : Window
{
    public CsvImportPreviewWindow(IReadOnlyList<CsvImportPreviewRow> previewRows)
    {
        InitializeComponent();
        PreviewGrid.ItemsSource = previewRows;

        var readyCount = previewRows.Count(row => row.IsValid && !row.IsDuplicate);
        var duplicateCount = previewRows.Count(row => row.IsDuplicate);
        var invalidCount = previewRows.Count(row => !row.IsValid);
        PreviewSummary.Text = $"{readyCount} ready, {duplicateCount} possible duplicates, {invalidCount} invalid.";
    }

    private void ImportSelectedRows_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (PreviewGrid.ItemsSource is not IEnumerable<CsvImportPreviewRow> rows || !rows.Any(row => row.Include && row.IsValid))
        {
            MessageBox.Show(this, "Select at least one valid row to import.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
