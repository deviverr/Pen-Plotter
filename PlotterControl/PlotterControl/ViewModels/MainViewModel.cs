// PlotterControl/PlotterControl/ViewModels/MainViewModel.cs

using MaterialDesignThemes.Wpf;
using PlotterControl.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PlotterControl.Utils;

namespace PlotterControl.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IPlotterService _plotterService;
        private readonly ConfigManager _configManager;
        private MachineState _machineState;

        // ViewModels for child controls/tabs
        public ControlPanelVM ControlPanelViewModel { get; }
        public CalibrationVM CalibrationViewModel { get; }
        public EditorVM EditorViewModel { get; }
        public SettingsVM SettingsViewModel { get; }

        public MainViewModel(IPlotterService plotterService, ConfigManager configManager,
                             ControlPanelVM controlPanelVM, CalibrationVM calibrationVM,
                             EditorVM editorVM, SettingsVM settingsVM)
        {
            _plotterService = plotterService;
            _configManager = configManager;

            ControlPanelViewModel = controlPanelVM;
            CalibrationViewModel = calibrationVM;
            EditorViewModel = editorVM;
            SettingsViewModel = settingsVM;

            // Subscribe to plotter service state changes
            _plotterService.MachineStateChanged += OnMachineStateChanged;
            _plotterService.LogReceived += OnLogReceived; // Listen to general log messages

            _machineState = _plotterService.MachineState; // Initialize with current state

            // Initialize commands
            DiscoverPortsCommand = new AsyncRelayCommand(DiscoverPortsAsync);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
            MoveToOriginCommand = new AsyncRelayCommand(MoveToOrigin, CanMoveToOrigin);

            ToggleLogPanelCommand = new AsyncRelayCommand(() => { IsLogPanelVisible = !IsLogPanelVisible; return Task.CompletedTask; });
            ClearLogCommand = new AsyncRelayCommand(() => { LogMessages.Clear(); return Task.CompletedTask; });
            SaveLogCommand = new AsyncRelayCommand(SaveLogAsync);

            AvailablePorts = new ObservableCollection<string>();
            RefreshAvailablePorts();
            
            // Auto-connect if configured
            if (_configManager.CurrentConfig.AutoConnect && !string.IsNullOrEmpty(_configManager.CurrentConfig.LastComPort))
            {
                SelectedPort = _configManager.CurrentConfig.LastComPort;
                // Don't await here, let it run in the background
                _ = ConnectAsync(); 
            }
        }

        private bool _wasConnected;
        private void OnMachineStateChanged(MachineState newState)
        {
            // Detect connection loss
            if (_wasConnected && !newState.IsConnected)
            {
                ShowWarning("Connection lost!");
            }
            _wasConnected = newState.IsConnected;

            _machineState = newState;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(FirmwareInfo));
            ((AsyncRelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)MoveToOriginCommand).RaiseCanExecuteChanged();
        }

        private void OnLogReceived(string message)
        {
            System.Diagnostics.Debug.WriteLine($"LOG: {message}");

            // Show snackbar warnings for error responses from firmware
            if (message.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                ShowWarning(message);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => AddLogMessage(message));
            }
            else
            {
                AddLogMessage(message);
            }
        }

        private void AddLogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogMessages.Add($"[{timestamp}] {message}");
            while (LogMessages.Count > 500)
                LogMessages.RemoveAt(0);
        }

        public SnackbarMessageQueue NotificationQueue { get; } = new SnackbarMessageQueue(TimeSpan.FromSeconds(4));

        public void ShowWarning(string message)
        {
            NotificationQueue.Enqueue(message);
        }

        public ObservableCollection<string> AvailablePorts { get; }
        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        private bool _isLogPanelVisible;
        public bool IsLogPanelVisible
        {
            get => _isLogPanelVisible;
            set => SetProperty(ref _isLogPanelVisible, value);
        }

        public ICommand ToggleLogPanelCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand SaveLogCommand { get; }

        private string _selectedPort;
        public string SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (SetProperty(ref _selectedPort, value))
                {
                    // Notify Connect command to re-evaluate CanExecute when port selection changes
                    ((AsyncRelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsConnected => _machineState.IsConnected;
        public string StatusMessage
        {
            get => _machineState.StatusMessage;
            set
            {
                _machineState.StatusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
        public string AppVersion
        {
            get
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
            }
        }

        public string FirmwareInfo
        {
            get
            {
                if (string.IsNullOrEmpty(_machineState.FirmwareName))
                    return "Disconnected";
                return $"{_machineState.FirmwareName} v{_machineState.FirmwareVersion}";
            }
        }

        public ICommand DiscoverPortsCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand MoveToOriginCommand { get; }

        private async Task DiscoverPortsAsync()
        {
            StatusMessage = "Discovering ports...";
            Logger.Info("Discovering serial ports...");

            AvailablePorts.Clear();
            var ports = await _plotterService.DiscoverPortsAsync();

            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
                Logger.Info($"Discovered plotter port: {port}");
            }

            // If no plotter ports found, add all available COM ports
            if (AvailablePorts.Count == 0)
            {
                Logger.Info("No plotter ports found via discovery, listing all COM ports...");
                var allPorts = SerialConnection.GetPortNames();
                foreach (var port in allPorts)
                {
                    AvailablePorts.Add(port);
                }
            }

            StatusMessage = $"Found {AvailablePorts.Count} port(s).";
            Logger.Info($"Port discovery complete: {AvailablePorts.Count} port(s) available.");
        }

        private void RefreshAvailablePorts()
        {
            Logger.Info("Refreshing available ports list...");
            AvailablePorts.Clear();
            var ports = SerialConnection.GetPortNames().ToList(); // Use static method for raw port names
            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
                Logger.Info($"Found port: {port}");
            }
            Logger.Info($"Refresh complete: {AvailablePorts.Count} port(s) available.");
        }

        private bool CanConnect() => !string.IsNullOrEmpty(SelectedPort) && !IsConnected;
        private async Task ConnectAsync()
        {
            try
            {
                StatusMessage = $"Connecting to {SelectedPort} at {_configManager.CurrentConfig.BaudRate}...";
                Logger.Info($"Attempting connection to {SelectedPort}...");

                bool connected = await _plotterService.ConnectAsync(SelectedPort, _configManager.CurrentConfig.BaudRate);

                if (connected)
                {
                    _configManager.CurrentConfig.LastComPort = SelectedPort;
                    _configManager.Save();
                    StatusMessage = "Connected successfully.";
                    Logger.Info("Connection successful.");
                    await _plotterService.GetMachineStateAsync(); // Get full machine state after connection
                }
                else
                {
                    StatusMessage = "Connection failed. Check port and cables.";
                    Logger.Warning("Connection failed - port did not open or firmware not detected.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection error: {ex.Message}";
                Logger.Error($"Connection exception: {ex.Message}", ex);
            }
        }

        private bool CanDisconnect() => IsConnected;
        private async Task DisconnectAsync()
        {
            StatusMessage = "Disconnecting...";
            await _plotterService.DisconnectAsync();

            // Clear firmware info
            _machineState.FirmwareName = "";
            _machineState.FirmwareVersion = "";
            OnPropertyChanged(nameof(FirmwareInfo));

            StatusMessage = "Disconnected.";
        }

        private bool CanMoveToOrigin() => IsConnected && !_machineState.IsBusy;
        private async Task MoveToOrigin()
        {
            StatusMessage = "Moving to origin...";
            // Assumes firmware understands G0 X0 Y0 F<feedrate>
            bool successX = await _plotterService.JogAsync('X', 0, _configManager.CurrentConfig.RapidFeedrate);
            bool successY = await _plotterService.JogAsync('Y', 0, _configManager.CurrentConfig.RapidFeedrate);
            if (successX && successY)
            {
                StatusMessage = "Moved to origin.";
                await _plotterService.GetMachineStateAsync();
            }
            else
            {
                StatusMessage = "Failed to move to origin.";
            }
        }

        private async Task SaveLogAsync()
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"PlotterLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var logContent = string.Join(Environment.NewLine, LogMessages);
                    await System.IO.File.WriteAllTextAsync(saveFileDialog.FileName, logContent);
                    StatusMessage = $"Log saved to {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                    Logger.Info($"Log file saved: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to save log: {ex.Message}";
                Logger.Error($"Error saving log file: {ex.Message}", ex);
            }
        }
    }
}
