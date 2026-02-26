// PlotterControl/PlotterControl/ViewModels/ControlPanelVM.cs

using PlotterControl.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using PlotterControl.Utils;

namespace PlotterControl.ViewModels
{
    public class ControlPanelVM : BaseViewModel
    {
        private readonly IPlotterService _plotterService;
        private readonly ConfigManager _configManager;

        public ControlPanelVM(IPlotterService plotterService, ConfigManager configManager)
        {
            _plotterService = plotterService;
            _configManager = configManager;
            _plotterService.MachineStateChanged += OnMachineStateChanged;
            // Removed _plotterService.LogReceived += OnLogReceived;

            // Initialize Commands with CanExecute predicates
            JogXPlusCommand = new AsyncRelayCommand(() => JogAxis('X', JogStep), CanJog);
            JogXMinusCommand = new AsyncRelayCommand(() => JogAxis('X', -JogStep), CanJog);
            JogYPlusCommand = new AsyncRelayCommand(() => JogAxis('Y', JogStep), CanJog);
            JogYMinusCommand = new AsyncRelayCommand(() => JogAxis('Y', -JogStep), CanJog);
            JogZPlusCommand = new AsyncRelayCommand(() => JogAxis('Z', JogStep), CanJog); // For manual Z jog
            JogZMinusCommand = new AsyncRelayCommand(() => JogAxis('Z', -JogStep), CanJog); // For manual Z jog
            PenUpCommand = new AsyncRelayCommand(PenUp, CanJog);
            PenDownCommand = new AsyncRelayCommand(PenDown, CanJog);
            HomeAllCommand = new AsyncRelayCommand(HomeAllAxes, CanHome);
            SetSpeedOverrideCommand = new AsyncRelayCommand(SetSpeedOverride, CanJog);

            // Default jog step options
            JogSteps = new List<double> { 0.1, 0.5, 1, 5, 10, 25, 50, 100 };
            JogStep = JogSteps[2]; // Default to 1mm
            _speedOverridePercent = 100;
        }

        private void OnMachineStateChanged(MachineState newState)
        {
            // newState is already _plotterService.MachineState, so directly update relevant properties
            // Notify UI of changes to relevant properties
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsHomed));
            OnPropertyChanged(nameof(CurrentX));
            OnPropertyChanged(nameof(CurrentY));
            OnPropertyChanged(nameof(CurrentZ));
            OnPropertyChanged(nameof(StatusMessage));

            // Invalidate all commands when machine state changes
            ((AsyncRelayCommand)JogXPlusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogXMinusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogYPlusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogYMinusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogZPlusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogZMinusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)PenUpCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)PenDownCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)HomeAllCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SetSpeedOverrideCommand).RaiseCanExecuteChanged();
        }

        // Properties bound to UI
        public bool IsConnected => _plotterService.MachineState.IsConnected;
        public bool IsBusy => _plotterService.MachineState.IsBusy;
        public bool IsHomed => _plotterService.MachineState.IsHomed;
        public double CurrentX => _plotterService.MachineState.CurrentX;
        public double CurrentY => _plotterService.MachineState.CurrentY;
        public double CurrentZ => _plotterService.MachineState.CurrentZ;
        public string StatusMessage => _plotterService.MachineState.StatusMessage;

        public double BedWidth => _configManager.CurrentConfig.CalibrationWidth;
        public double BedHeight => _configManager.CurrentConfig.CalibrationHeight;
        public double MachineWidth => _configManager.CurrentConfig.XMaxMM;
        public double MachineHeight => _configManager.CurrentConfig.YMaxMM;
        public double PaperOriginX => _configManager.CurrentConfig.CalibrationOrigin.X;
        public double PaperOriginY => _configManager.CurrentConfig.CalibrationOrigin.Y;

        private double _jogStep;
        public double JogStep
        {
            get => _jogStep;
            set => SetProperty(ref _jogStep, value);
        }

        public List<double> JogSteps { get; } // Options for jog step size

        private string _customJogStepText;
        public string CustomJogStepText
        {
            get => _customJogStepText;
            set
            {
                if (SetProperty(ref _customJogStepText, value))
                {
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed > 0 && parsed <= 500)
                    {
                        JogStep = parsed;
                    }
                }
            }
        }

        private int _speedOverridePercent;
        public int SpeedOverridePercent
        {
            get => _speedOverridePercent;
            set => SetProperty(ref _speedOverridePercent, Math.Clamp(value, 10, 200));
        }

        // Commands
        public ICommand JogXPlusCommand { get; }
        public ICommand JogXMinusCommand { get; }
        public ICommand JogYPlusCommand { get; }
        public ICommand JogYMinusCommand { get; }
        public ICommand JogZPlusCommand { get; }
        public ICommand JogZMinusCommand { get; }
        public ICommand PenUpCommand { get; }
        public ICommand PenDownCommand { get; }
        public ICommand HomeAllCommand { get; }
        public ICommand SetSpeedOverrideCommand { get; }

        // CanExecute predicates for commands
        private bool CanJog() => IsConnected && !IsBusy;
        private bool CanHome() => IsConnected && !IsBusy;

        // Command Implementations
        private async Task JogAxis(char axis, double distance)
        {
            if (!IsConnected || IsBusy) return;

            // Jog feedrate (mm/min)
            double jogFeedrate = (axis == 'Z') ? 600 : 5000; // Z is slower (leadscrew vs belt)

            // JogAsync uses G91 relative mode, so pass the step distance directly.
            // Firmware handles soft limits; no absolute position calculation needed here.
            bool success = await _plotterService.JogAsync(axis, distance, jogFeedrate);
            if (success)
            {
                await _plotterService.GetMachineStateAsync();
            }
        }

        private async Task PenUp()
        {
            if (!IsConnected)
            {
                Logger.Warning("Cannot move pen up: Not connected");
                return;
            }
            if (IsBusy)
            {
                Logger.Warning("Cannot move pen up: Machine busy");
                return;
            }
            bool success = await _plotterService.PenUpAsync();
            if (success) await _plotterService.GetMachineStateAsync();
        }

        private async Task PenDown()
        {
            if (!IsConnected)
            {
                Logger.Warning("Cannot move pen down: Not connected");
                return;
            }
            if (IsBusy)
            {
                Logger.Warning("Cannot move pen down: Machine busy");
                return;
            }
            bool success = await _plotterService.PenDownAsync();
            if (success) await _plotterService.GetMachineStateAsync();
        }

        private async Task HomeAllAxes()
        {
            if (!IsConnected)
            {
                Logger.Warning("Cannot home: Not connected");
                return;
            }
            if (IsBusy)
            {
                Logger.Warning("Cannot home: Machine busy");
                return;
            }
            bool success = await _plotterService.HomeAllAxesAsync();
            if (success) await _plotterService.GetMachineStateAsync();
        }

        private async Task SetSpeedOverride()
        {
            if (!IsConnected || IsBusy) return;
            await _plotterService.SetSpeedOverrideAsync(SpeedOverridePercent);
        }
    }
}