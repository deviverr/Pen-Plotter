using PlotterControl.Models;
using PlotterControl.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PlotterControl.Utils;
using Microsoft.Win32;
// HumanizationSettings and HumanizingFilter are in PlotterControl.Models / PlotterControl.Services

namespace PlotterControl.ViewModels
{
    /// <summary>
    /// Wrapper to display both Hershey fonts and system fonts in the same ComboBox.
    /// </summary>
    public class FontItem
    {
        public string Name { get; set; }
        public bool IsSystemFont { get; set; }
        public Font HersheyFont { get; set; } // null for system fonts
    }

    /// <summary>
    /// Preset paper/drawing format sizes.
    /// </summary>
    public class FormatPreset
    {
        public string Name { get; set; }
        public double Width { get; set; }  // mm
        public double Height { get; set; } // mm
    }

    public class EditorVM : BaseViewModel
    {
        private readonly TextRenderer _textRenderer;
        private readonly ConfigManager _configManager;
        private readonly GCodeGenerator _gcodeGenerator;
        private readonly IPlotterService _plotterService;
        private readonly ImageProcessor _imageProcessor;
        private readonly SystemFontRenderer _systemFontRenderer;
        private readonly HumanizingFilter _humanizingFilter;
        private PlotterConfig _currentConfig;

        private CancellationTokenSource _plotCancellationTokenSource;

        public EditorVM(TextRenderer textRenderer, ConfigManager configManager,
            GCodeGenerator gcodeGenerator, IPlotterService plotterService,
            ImageProcessor imageProcessor, SystemFontRenderer systemFontRenderer,
            HumanizingFilter humanizingFilter)
        {
            _textRenderer = textRenderer;
            _configManager = configManager;
            _gcodeGenerator = gcodeGenerator;
            _plotterService = plotterService;
            _imageProcessor = imageProcessor;
            _systemFontRenderer = systemFontRenderer;
            _humanizingFilter = humanizingFilter;

            _currentConfig = _configManager.CurrentConfig;
            HumanSettings = new HumanizationSettings();

            _plotterService.MachineStateChanged += OnMachineStateChanged;
            _plotterService.PlotProgressChanged += OnPlotProgressChanged;

            AvailableFonts = new ObservableCollection<FontItem>();
            AvailableFormats = new ObservableCollection<FormatPreset>
            {
                new FormatPreset { Name = "A4 (210x297mm)", Width = 210, Height = 297 },
                new FormatPreset { Name = "A5 (148x210mm)", Width = 148, Height = 210 },
                new FormatPreset { Name = "Letter (216x279mm)", Width = 216, Height = 279 },
                new FormatPreset { Name = "Legal (216x356mm)", Width = 216, Height = 356 },
                new FormatPreset { Name = "Custom", Width = 0, Height = 0 }
            };
            SelectedFormat = AvailableFormats.LastOrDefault(); // Default to "Custom"

            InputText = "Hello World!";
            FontSize = 10.0;
            IsTextMode = true;

            // Image mode defaults
            _imageThreshold = 128;
            _imageLineSpacing = 1.0;
            _useDithering = false;

            Task.Run(LoadFonts);
            UpdateDrawableArea();

            // Commands
            RenderTextCommand = new AsyncRelayCommand(RenderText);
            GenerateGCodeCommand = new AsyncRelayCommand(GenerateGCode);
            PlotGCodeCommand = new AsyncRelayCommand(PlotGCode, CanPlotGCode);
            PausePlotCommand = new AsyncRelayCommand(PausePlot, CanPausePlot);
            ResumePlotCommand = new AsyncRelayCommand(ResumePlot, CanResumePlot);
            StopPlotCommand = new AsyncRelayCommand(StopPlot, CanStopPlot);
            LoadImageCommand = new AsyncRelayCommand(LoadImage);
            ProcessImageCommand = new AsyncRelayCommand(ProcessImage);
        }

        private void OnMachineStateChanged(MachineState newState)
        {
            OnPropertyChanged(nameof(IsPlotting));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(PlotButtonDisabledReason));
            ((AsyncRelayCommand)PlotGCodeCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)PausePlotCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ResumePlotCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopPlotCommand).RaiseCanExecuteChanged();
        }

        private void OnPlotProgressChanged(PlotProgress progress)
        {
            PlotProgress = progress;
            if (progress != null)
            {
                var eta = progress.EstimatedTimeRemaining;
                string etaStr = eta.TotalHours >= 1
                    ? $"{(int)eta.TotalHours}h {eta.Minutes:D2}m"
                    : eta.TotalMinutes >= 1
                        ? $"{(int)eta.TotalMinutes}m {eta.Seconds:D2}s"
                        : $"{eta.Seconds}s";
                string paused = progress.IsPaused ? " [PAUSED]" : "";
                PlotProgressDisplay = $"{progress.PercentComplete:F1}% | {progress.LinesCompleted}/{progress.TotalLines} lines | ETA: {etaStr}{paused}";
            }
            else
            {
                PlotProgressDisplay = string.Empty;
            }
            ((AsyncRelayCommand)PausePlotCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ResumePlotCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopPlotCommand).RaiseCanExecuteChanged();
        }

        private async Task LoadFonts()
        {
            // Load Hershey fonts
            string[] fontNames = { "hershey_simplex", "hershey_script", "hershey_gothic" };
            var items = new List<FontItem>();

            foreach (var name in fontNames)
            {
                var font = await _textRenderer.LoadFontAsync(name);
                if (font != null)
                {
                    items.Add(new FontItem
                    {
                        Name = font.Name,
                        IsSystemFont = false,
                        HersheyFont = font
                    });
                }
            }

            // Load system fonts
            try
            {
                var sysNames = _systemFontRenderer.GetSystemFontNames();
                foreach (var name in sysNames)
                {
                    items.Add(new FontItem
                    {
                        Name = $"[SYS] {name}",
                        IsSystemFont = true,
                        HersheyFont = null
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not load system fonts: {ex.Message}");
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in items)
                {
                    AvailableFonts.Add(item);
                }
                SelectedFont = AvailableFonts.FirstOrDefault();
            });
        }

        private void UpdateDrawableArea()
        {
            DrawableArea = new System.Windows.Rect(
                _currentConfig.CalibrationOrigin.X,
                _currentConfig.CalibrationOrigin.Y,
                _currentConfig.CalibrationWidth,
                _currentConfig.CalibrationHeight);
            MachineWidth = _currentConfig.XMaxMM;
            MachineHeight = _currentConfig.YMaxMM;
            PaperOriginX = _currentConfig.CalibrationOrigin.X;
            PaperOriginY = _currentConfig.CalibrationOrigin.Y;
        }

        // --- Mode toggle ---
        private bool _isTextMode = true;
        public bool IsTextMode
        {
            get => _isTextMode;
            set
            {
                if (SetProperty(ref _isTextMode, value))
                {
                    OnPropertyChanged(nameof(IsImageMode));
                }
            }
        }

        public bool IsImageMode
        {
            get => !_isTextMode;
            set
            {
                IsTextMode = !value;
            }
        }

        // --- Text mode properties ---
        private string _inputText;
        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        private FontItem _selectedFont;
        public FontItem SelectedFont
        {
            get => _selectedFont;
            set => SetProperty(ref _selectedFont, value);
        }

        private double _fontSize;
        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, value);
        }

        public ObservableCollection<FontItem> AvailableFonts { get; }

        // --- Format presets ---
        public ObservableCollection<FormatPreset> AvailableFormats { get; }

        private FormatPreset _selectedFormat;
        public FormatPreset SelectedFormat
        {
            get => _selectedFormat;
            set
            {
                if (SetProperty(ref _selectedFormat, value) && value != null)
                {
                    if (value.Name != "Custom")
                    {
                        // Update drawable area to match preset
                        DrawableArea = new System.Windows.Rect(
                            _currentConfig.CalibrationOrigin.X,
                            _currentConfig.CalibrationOrigin.Y,
                            value.Width,
                            value.Height);
                    }
                }
            }
        }

        // --- Image mode properties ---
        private string _imageFilePath;
        private string _imageFileName;
        public string ImageFileName
        {
            get => _imageFileName;
            private set => SetProperty(ref _imageFileName, value);
        }

        private int _imageThreshold;
        public int ImageThreshold
        {
            get => _imageThreshold;
            set => SetProperty(ref _imageThreshold, value);
        }

        private double _imageLineSpacing;
        public double ImageLineSpacing
        {
            get => _imageLineSpacing;
            set => SetProperty(ref _imageLineSpacing, value);
        }

        private bool _useDithering;
        public bool UseDithering
        {
            get => _useDithering;
            set => SetProperty(ref _useDithering, value);
        }

        private bool _useContourMode;
        public bool UseContourMode
        {
            get => _useContourMode;
            set => SetProperty(ref _useContourMode, value);
        }

        private BitmapImage _imagePreview;
        public BitmapImage ImagePreview
        {
            get => _imagePreview;
            private set => SetProperty(ref _imagePreview, value);
        }

        // --- Shared properties ---
        private List<DrawingPath> _renderedPaths;
        public List<DrawingPath> RenderedPaths
        {
            get => _renderedPaths;
            private set => SetProperty(ref _renderedPaths, value);
        }

        private System.Windows.Rect _drawableArea;
        public System.Windows.Rect DrawableArea
        {
            get => _drawableArea;
            private set => SetProperty(ref _drawableArea, value);
        }

        private double _machineWidth = 220;
        public double MachineWidth
        {
            get => _machineWidth;
            private set => SetProperty(ref _machineWidth, value);
        }

        private double _machineHeight = 220;
        public double MachineHeight
        {
            get => _machineHeight;
            private set => SetProperty(ref _machineHeight, value);
        }

        private double _paperOriginX;
        public double PaperOriginX
        {
            get => _paperOriginX;
            private set => SetProperty(ref _paperOriginX, value);
        }

        private double _paperOriginY;
        public double PaperOriginY
        {
            get => _paperOriginY;
            private set => SetProperty(ref _paperOriginY, value);
        }

        private string _generatedGCode;
        public string GeneratedGCode
        {
            get => _generatedGCode;
            private set => SetProperty(ref _generatedGCode, value);
        }

        private string _estimatedPlotInfo;
        public string EstimatedPlotInfo
        {
            get => _estimatedPlotInfo;
            private set => SetProperty(ref _estimatedPlotInfo, value);
        }

        private string _plotProgressDisplay;
        public string PlotProgressDisplay
        {
            get => _plotProgressDisplay;
            private set => SetProperty(ref _plotProgressDisplay, value);
        }

        private PlotProgress _plotProgress;
        public PlotProgress PlotProgress
        {
            get => _plotProgress;
            private set => SetProperty(ref _plotProgress, value);
        }

        public bool IsPlotting => _plotterService.MachineState.IsPlotting;
        public bool IsConnected => _plotterService.MachineState.IsConnected;
        public bool IsBusy => _plotterService.MachineState.IsBusy;

        // --- Humanization settings ---
        private HumanizationSettings _humanSettings;
        public HumanizationSettings HumanSettings
        {
            get => _humanSettings;
            set => SetProperty(ref _humanSettings, value);
        }

        // --- Commands ---
        public ICommand RenderTextCommand { get; }
        public ICommand GenerateGCodeCommand { get; }
        public ICommand PlotGCodeCommand { get; }
        public ICommand PausePlotCommand { get; }
        public ICommand ResumePlotCommand { get; }
        public ICommand StopPlotCommand { get; }
        public ICommand LoadImageCommand { get; }
        public ICommand ProcessImageCommand { get; }

        // --- Text rendering ---
        private async Task RenderText()
        {
            if (string.IsNullOrEmpty(InputText) || SelectedFont == null)
            {
                RenderedPaths = null;
                GeneratedGCode = string.Empty;
                return;
            }

            if (SelectedFont.IsSystemFont)
            {
                // Extract actual font family name (remove "[SYS] " prefix)
                var familyName = SelectedFont.Name.Substring(6);
                RenderedPaths = await Task.Run(() =>
                    _systemFontRenderer.RenderSystemFont(InputText, familyName, FontSize,
                        _currentConfig.PenDownZ, _currentConfig.PenUpZ));
            }
            else if (SelectedFont.HersheyFont != null)
            {
                var settings = HumanSettings;
                RenderedPaths = _textRenderer.RenderText(InputText, SelectedFont.HersheyFont, FontSize, settings);
            }
            else
            {
                RenderedPaths = null;
                GeneratedGCode = string.Empty;
                return;
            }

            Debug.WriteLine($"Rendered {RenderedPaths?.Count ?? 0} paths for text.");
            await GenerateGCode();
        }

        // --- Image loading ---
        private Task LoadImage()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
                Title = "Select Image"
            };

            if (dialog.ShowDialog() == true)
            {
                _imageFilePath = dialog.FileName;
                ImageFileName = System.IO.Path.GetFileName(_imageFilePath);

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_imageFilePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ImagePreview = bitmap;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to load image preview: {ex.Message}");
                }
            }
            return Task.CompletedTask;
        }

        // --- Image processing ---
        private async Task ProcessImage()
        {
            if (string.IsNullOrEmpty(_imageFilePath))
                return;

            var ditherMode = UseDithering
                ? ImageProcessor.DitherMode.FloydSteinberg
                : ImageProcessor.DitherMode.Threshold;

            var drawMode = UseContourMode
                ? ImageProcessor.DrawMode.Contours
                : ImageProcessor.DrawMode.ScanLines;

            var bedW = _currentConfig.CalibrationWidth;
            var bedH = _currentConfig.CalibrationHeight;

            RenderedPaths = await Task.Run(() =>
                _imageProcessor.ProcessImage(_imageFilePath, bedW, bedH,
                    ImageLineSpacing, ImageThreshold, ditherMode,
                    _currentConfig.PenDownZ, _currentConfig.PenUpZ, drawMode));

            Debug.WriteLine($"Processed image into {RenderedPaths?.Count ?? 0} paths.");
            await GenerateGCode();
        }

        // --- G-Code generation ---
        private async Task GenerateGCode()
        {
            if (RenderedPaths == null || RenderedPaths.Count == 0)
            {
                GeneratedGCode = string.Empty;
                EstimatedPlotInfo = string.Empty;
                return;
            }

            var paths = new List<DrawingPath>(RenderedPaths);

            // Apply humanization filter (jitter + baseline wobble) before path optimization
            if (HumanSettings?.Enabled == true)
            {
                paths = await Task.Run(() => _humanizingFilter.Apply(paths, HumanSettings));
            }

            var optimizedPaths = _gcodeGenerator.OptimizePaths(paths);
            GeneratedGCode = _gcodeGenerator.GenerateGCode(optimizedPaths, HumanSettings);
            Debug.WriteLine("G-code generated.");

            // Estimate plot time from generated G-code
            var (estTime, totalLines, totalDist) = _gcodeGenerator.EstimatePlotTime(GeneratedGCode);
            string timeStr = estTime.TotalHours >= 1
                ? $"{(int)estTime.TotalHours}h {estTime.Minutes:D2}m"
                : estTime.TotalMinutes >= 1
                    ? $"{(int)estTime.TotalMinutes}m {estTime.Seconds:D2}s"
                    : $"{estTime.Seconds}s";
            EstimatedPlotInfo = $"Est. time: {timeStr} | {totalLines} moves | {totalDist:F0} mm";

            // Refresh Plot button state now that G-code is available
            OnPropertyChanged(nameof(PlotButtonDisabledReason));
            ((AsyncRelayCommand)PlotGCodeCommand).RaiseCanExecuteChanged();
        }

        // --- Plotting ---
        public string PlotButtonDisabledReason
        {
            get
            {
                if (!_plotterService.MachineState.IsConnected) return "Not connected to plotter";
                if (_plotterService.MachineState.IsPlotting) return "Already plotting";
                if (_plotterService.MachineState.IsBusy) return "Machine is busy";
                if (string.IsNullOrEmpty(GeneratedGCode)) return "No G-code generated";
                return "Ready to plot";
            }
        }

        private bool CanPlotGCode() =>
            _plotterService.MachineState.IsConnected &&
            !_plotterService.MachineState.IsPlotting &&
            !_plotterService.MachineState.IsBusy &&
            !string.IsNullOrEmpty(GeneratedGCode);

        private async Task PlotGCode()
        {
            if (string.IsNullOrEmpty(GeneratedGCode)) return;

            // Step 1: Confirmation dialog
            var confirmResult = MessageBox.Show(
                $"Ready to plot?\n\n{EstimatedPlotInfo}\n\nThe machine will auto-home if needed, then preview the plot area with the pen raised.",
                "Start Plot", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirmResult != MessageBoxResult.OK) return;

            try
            {
                // Step 2: Auto-home if not homed
                if (!_plotterService.MachineState.IsHomed)
                {
                    Logger.Info("Machine not homed, auto-homing...");
                    bool homeSuccess = await _plotterService.HomeAllAxesAsync();
                    if (!homeSuccess)
                    {
                        MessageBox.Show("Homing failed. Cannot proceed with plotting.",
                            "Homing Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Step 3: Corner preview - move pen (raised) to bounding box corners
                var bounds = ComputeGCodeBounds(GeneratedGCode);
                if (bounds.HasValue)
                {
                    var (minX, minY, maxX, maxY) = bounds.Value;
                    double feedrate = _currentConfig.RapidFeedrate;

                    Logger.Info($"Previewing plot area: ({minX:F1},{minY:F1}) to ({maxX:F1},{maxY:F1})");
                    await _plotterService.PenUpAsync();
                    await _plotterService.SendGCodeAndAwaitOkAsync("G90");

                    // Visit all 4 corners
                    await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{minX:F3} Y{minY:F3} F{feedrate:F0}");
                    await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{maxX:F3} Y{minY:F3} F{feedrate:F0}");
                    await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{maxX:F3} Y{maxY:F3} F{feedrate:F0}");
                    await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{minX:F3} Y{maxY:F3} F{feedrate:F0}");
                    await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{minX:F3} Y{minY:F3} F{feedrate:F0}");

                    // Step 4: Confirm after preview
                    var previewResult = MessageBox.Show(
                        "The pen traced the plot area corners (pen up).\n\nIs the paper aligned correctly?\n\nClick OK to start plotting, or Cancel to abort.",
                        "Confirm Plot Area", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (previewResult != MessageBoxResult.OK) return;
                }

                // Step 5: Execute plot
                Logger.Info("Starting G-code plot...");
                _plotCancellationTokenSource = new CancellationTokenSource();
                var progress = new Progress<PlotProgress>();
                progress.ProgressChanged += (sender, p) => PlotProgress = p;

                bool success = await _plotterService.ExecutePlotAsync(GeneratedGCode, _plotCancellationTokenSource.Token, progress);
                if (success)
                    Logger.Info("Plotting finished successfully.");
                else
                    Logger.Info("Plotting finished with errors.");
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Plotting canceled by user.");
            }
            catch (Exception ex)
            {
                Logger.Info($"Plotting error: {ex.Message}");
                MessageBox.Show($"Plotting error: {ex.Message}", "Plot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _plotCancellationTokenSource?.Dispose();
                _plotCancellationTokenSource = null;
                PlotProgress = null;
            }
        }

        private (double minX, double minY, double maxX, double maxY)? ComputeGCodeBounds(string gcode)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool found = false;

            var xRegex = new Regex(@"X([\d.-]+)");
            var yRegex = new Regex(@"Y([\d.-]+)");

            foreach (var line in gcode.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("G0") && !trimmed.StartsWith("G1")) continue;

                var xMatch = xRegex.Match(trimmed);
                var yMatch = yRegex.Match(trimmed);

                if (xMatch.Success)
                {
                    double x = double.Parse(xMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    found = true;
                }
                if (yMatch.Success)
                {
                    double y = double.Parse(yMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    found = true;
                }
            }

            return found ? (minX, minY, maxX, maxY) : null;
        }

        private bool CanPausePlot() => _plotterService.MachineState.IsPlotting && (PlotProgress == null || !PlotProgress.IsPaused);
        private Task PausePlot()
        {
            _plotterService.PausePlot();
            return Task.CompletedTask;
        }

        private bool CanResumePlot() => _plotterService.MachineState.IsPlotting && (PlotProgress != null && PlotProgress.IsPaused);
        private Task ResumePlot()
        {
            _plotterService.ResumePlot();
            return Task.CompletedTask;
        }

        private bool CanStopPlot() => _plotterService.MachineState.IsPlotting;
        private Task StopPlot()
        {
            _plotterService.StopPlot();
            return Task.CompletedTask;
        }
    }
}
