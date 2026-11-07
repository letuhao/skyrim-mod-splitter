using System.Diagnostics;
using System.IO;
using System.Windows;
using SkyrimModSplitter.Core.Conversion;
using SkyrimModSplitter.Core.Reports;
using SkyrimModSplitter.Core.Scanning;
using SkyrimModSplitter.Gui.ViewModels;

namespace SkyrimModSplitter.Gui;

public partial class MainWindow : Window
{
    private readonly ScanViewModel _viewModel = new();
    private ModScanResult? _scan;
    private ConversionPlan? _plan;
    private string? _scanFolder;
    private CancellationTokenSource? _applyCts;
    private Core.Split.SplitPlan? _splitPlan;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Skyrim mod folder" };
        if (dialog.ShowDialog(this) == true) _viewModel.Folder = dialog.FolderName;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async void Plan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await EnsureScanAsync() || _scan == null) return;
            _viewModel.IsBusy = true;
            var folder = _viewModel.Folder;
            var overwrite = OverwriteBox.IsChecked == true;
            _plan = await Task.Run(() => new ConversionPlanner().Plan(_scan, new ConversionOptions
            {
                OutputFolder = DefaultOutputFolder(folder),
                Overwrite = overwrite,
            }));
            _viewModel.SetPlan(_plan);
            _viewModel.Status = "Plan ready: " + _plan.Verdict;
        }
        catch (Exception ex)
        {
            _viewModel.Status = "Plan failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Plan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_plan == null) { _viewModel.Status = "Plan first."; return; }
            _viewModel.IsBusy = true;
            _viewModel.ApplyStatus = "Applying conversion...";
            ScanButton.IsEnabled = false;
            PlanButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            _applyCts = new CancellationTokenSource();
            var options = new ConversionOptions { OutputFolder = _plan.OutputFolder, Overwrite = OverwriteBox.IsChecked == true };
            var progress = new Progress<string>(message =>
            {
                _viewModel.ApplyStatus = message;
                _viewModel.Status = "Converting: " + message;
            });
            var result = await Task.Run(() => new EslConverter().Execute(_plan, options, progress, _applyCts.Token));
            _viewModel.SetConversionResult(result);
            _viewModel.AddConversionWarnings(result.Warnings);
            _viewModel.Status = result.Success ? "Conversion complete: " + result.OutputPluginPath : "Conversion failed.";
        }
        catch (Exception ex)
        {
            _viewModel.Status = "Conversion failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Conversion failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.IsBusy = false;
            ScanButton.IsEnabled = true;
            PlanButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _applyCts?.Dispose();
            _applyCts = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _applyCts?.Cancel();
        _viewModel.Status = "Cancel requested...";
    }

    private async void SplitPlan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await EnsureScanAsync()) return;
            _viewModel.IsBusy = true;
            var folder = _viewModel.Folder;
            var options = new Core.Split.SplitOptions
            {
                ModFolder = folder,
                OutputFolder = DefaultSplitOutputFolder(folder),
            };
            _splitPlan = await Task.Run(() => new Core.Split.SplitPlanner().Plan(options));
            _viewModel.SetSplitPlan(_splitPlan);
            _viewModel.Status = "Split plan: feasible=" + _splitPlan.Feasible;
        }
        catch (Exception ex)
        {
            _viewModel.Status = "Split plan failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Split plan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void SplitApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_splitPlan == null) { _viewModel.Status = "Plan the split first."; return; }
            _viewModel.IsBusy = true;
            _viewModel.ApplyStatus = "Applying split...";
            ScanButton.IsEnabled = false;
            PlanButton.IsEnabled = false;
            SplitPlanButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            _applyCts = new CancellationTokenSource();
            var folder = _viewModel.Folder;
            var options = new Core.Split.SplitOptions
            {
                ModFolder = folder,
                OutputFolder = _splitPlan.OutputFolder,
            };
            var progress = new Progress<string>(message =>
            {
                _viewModel.ApplyStatus = message;
                _viewModel.Status = "Splitting: " + message;
            });
            var result = await Task.Run(() => new Core.Split.SplitExecutor().Execute(_splitPlan, options, progress, _applyCts.Token));
            _viewModel.SetSplitResult(result);
            _viewModel.Status = result.Success ? "Split complete: " + result.OutputFolder : "Split failed.";
        }
        catch (Exception ex)
        {
            _viewModel.Status = "Split failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Split failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.IsBusy = false;
            ScanButton.IsEnabled = true;
            PlanButton.IsEnabled = true;
            SplitPlanButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _applyCts?.Dispose();
            _applyCts = null;
        }
    }

    private static string DefaultSplitOutputFolder(string modFolder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(modFolder)));
        return Path.Combine(Environment.CurrentDirectory, "output", Sanitize(name) + "-split");
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var dir = _viewModel.LastOutputDir;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            MessageBox.Show(this, "No output folder yet.", "Open output", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
    }

    private async Task<bool> EnsureScanAsync()
    {
        if (_scan == null || !string.Equals(_scanFolder, _viewModel.Folder, StringComparison.OrdinalIgnoreCase))
            return await RunScanAsync();
        return true;
    }

    private async Task<bool> RunScanAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Folder))
        {
            MessageBox.Show(this, "Choose a mod folder first.", "Scan", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var dataDirs = DataDirBox.Text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToList();
        var options = new ScanOptions { ModFolder = _viewModel.Folder, DataDirectories = dataDirs };
        _viewModel.IsBusy = true;
        _viewModel.Status = "Scanning...";
        _plan = null;
        try
        {
            var result = await Task.Run(() => new ModFolderScanner().Scan(options));
            var outDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "scans", Sanitize(Path.GetFileName(Path.TrimEndingDirectorySeparator(options.ModFolder))));
            Directory.CreateDirectory(outDir);
            ScanReportWriter.WriteJson(result, Path.Combine(outDir, "scan.json"));
            ScanReportWriter.WriteMarkdown(result, Path.Combine(outDir, "SCAN-REPORT.md"));
            _scan = result;
            _scanFolder = _viewModel.Folder;
            _viewModel.Apply(result);
            _viewModel.SetOutputDir(outDir);
            return true;
        }
        catch (Exception ex)
        {
            _viewModel.Status = "Scan failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private static string DefaultOutputFolder(string modFolder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(modFolder)));
        return Path.Combine(Environment.CurrentDirectory, "output", Sanitize(name) + "-ESL");
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
