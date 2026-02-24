// PlotterControl/PlotterControl/ViewModels/CalibrationVM.cs

using PlotterControl.Models;
using PlotterControl.Services;
using PlotterControl.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlotterControl.ViewModels
{
    public class CalibrationVM : BaseViewModel
    {
        private readonly IPlotterService _plotterService;
        private readonly ConfigManager _configManager;
        private PlotterConfig _currentConfig;

        // --- Machine state properties (bound to UI) ---
        public bool IsConnected => _plotterService.MachineState.IsConnected;
        public bool IsBusy => _plotterService.MachineState.IsBusy;
        public double CurrentX => _plotterService.MachineState.CurrentX;
        public double CurrentY => _plotterService.MachineState.CurrentY;
        public double CurrentZ => _plotterService.MachineState.CurrentZ;

        // --- Calibration points ---
        private PlotterPoint _point1;
        public PlotterPoint Point1
        {
            get => _point1;
            set => SetProperty(ref _point1, value);
        }

        private PlotterPoint _point2;
        public PlotterPoint Point2
        {
            get => _point2;
            set => SetProperty(ref _point2, value);
        }

        private double _calibrationWidth;
        public double CalibrationWidth
        {
            get => _calibrationWidth;
            set => SetProperty(ref _calibrationWidth, value);
        }

        private double _calibrationHeight;
        public double CalibrationHeight
        {
            get => _calibrationHeight;
            set => SetProperty(ref _calibrationHeight, value);
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _point1Captured = false;

        private bool _isCalibrating;
        public bool IsCalibrating
        {
            get => _isCalibrating;
            set
            {
                if (SetProperty(ref _isCalibrating, value))
                {
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        // --- Jog properties ---
        private double _jogStep;
        public double JogStep
        {
            get => _jogStep;
            set => SetProperty(ref _jogStep, value);
        }

        public List<double> JogSteps { get; } = new List<double> { 0.1, 1, 10, 50 };

        // --- Pen height calibration properties ---
        public double DisplayPenDownZ => _configManager.CurrentConfig.PenDownZ;
        public double DisplayPenUpZ => _configManager.CurrentConfig.PenUpZ;

        // --- Commands: Calibration workflow ---
        public ICommand StartCalibrationCommand { get; }
        public ICommand SetPoint1Command { get; }
        public ICommand SetPoint2Command { get; }
        public ICommand SaveCalibrationCommand { get; }

        // --- Commands: Jog ---
        public ICommand JogXPlusCommand { get; }
        public ICommand JogXMinusCommand { get; }
        public ICommand JogYPlusCommand { get; }
        public ICommand JogYMinusCommand { get; }
        public ICommand JogZPlusCommand { get; }
        public ICommand JogZMinusCommand { get; }

        // --- Commands: Pen & Home ---
        public ICommand PenUpCommand { get; }
        public ICommand PenDownCommand { get; }
        public ICommand HomeAllCommand { get; }

        // --- Commands: Corner navigation ---
        public ICommand GoToOriginCommand { get; }
        public ICommand GoToBottomLeftCommand { get; }
        public ICommand GoToBottomRightCommand { get; }
        public ICommand GoToTopLeftCommand { get; }
        public ICommand GoToTopRightCommand { get; }
        public ICommand GoToCenterCommand { get; }
        public ICommand TracePerimeterCommand { get; }

        // --- Commands: Pen Z calibration ---
        public ICommand SetPenDownPositionCommand { get; }

        public CalibrationVM(IPlotterService plotterService, ConfigManager configManager)
        {
            _plotterService = plotterService;
            _configManager = configManager;
            _currentConfig = _configManager.CurrentConfig;

            // Subscribe to machine state changes (fixes gray buttons)
            _plotterService.MachineStateChanged += OnMachineStateChanged;

            // Initialize properties from config
            Point1 = _currentConfig.CalibrationOrigin;
            Point2 = new PlotterPoint(_currentConfig.CalibrationOrigin.X + _currentConfig.CalibrationWidth,
                                      _currentConfig.CalibrationOrigin.Y + _currentConfig.CalibrationHeight);
            CalibrationWidth = _currentConfig.CalibrationWidth;
            CalibrationHeight = _currentConfig.CalibrationHeight;

            // Jog defaults
            JogStep = JogSteps[1]; // 1mm

            // Calibration workflow commands
            StartCalibrationCommand = new AsyncRelayCommand(StartCalibration, CanStartCalibration);
            SetPoint1Command = new AsyncRelayCommand(SetPoint1, CanSetPoint1);
            SetPoint2Command = new AsyncRelayCommand(SetPoint2, CanSetPoint2);
            SaveCalibrationCommand = new AsyncRelayCommand(SaveCalibration, CanSaveCalibration);

            // Jog commands
            JogXPlusCommand = new AsyncRelayCommand(() => JogAxis('X', JogStep), CanJog);
            JogXMinusCommand = new AsyncRelayCommand(() => JogAxis('X', -JogStep), CanJog);
            JogYPlusCommand = new AsyncRelayCommand(() => JogAxis('Y', JogStep), CanJog);
            JogYMinusCommand = new AsyncRelayCommand(() => JogAxis('Y', -JogStep), CanJog);
            JogZPlusCommand = new AsyncRelayCommand(() => JogAxis('Z', JogStep), CanJog);
            JogZMinusCommand = new AsyncRelayCommand(() => JogAxis('Z', -JogStep), CanJog);

            // Pen & home commands
            PenUpCommand = new AsyncRelayCommand(PenUp, CanJog);
            PenDownCommand = new AsyncRelayCommand(PenDown, CanJog);
            HomeAllCommand = new AsyncRelayCommand(HomeAllAxes, CanJog);

            // Corner navigation commands
            GoToOriginCommand = new AsyncRelayCommand(GoToOrigin, CanJog);
            GoToBottomLeftCommand = new AsyncRelayCommand(GoToBottomLeft, CanJog);
            GoToBottomRightCommand = new AsyncRelayCommand(GoToBottomRight, CanJog);
            GoToTopLeftCommand = new AsyncRelayCommand(GoToTopLeft, CanJog);
            GoToTopRightCommand = new AsyncRelayCommand(GoToTopRight, CanJog);
            GoToCenterCommand = new AsyncRelayCommand(GoToCenter, CanJog);
            TracePerimeterCommand = new AsyncRelayCommand(TracePerimeter, CanJog);

            // Pen Z calibration
            SetPenDownPositionCommand = new AsyncRelayCommand(SetPenDownPosition, CanJog);

            StatusMessage = "Load existing calibration or start new.";
        }

        // --- Machine state subscription ---

        private void OnMachineStateChanged(MachineState newState)
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CurrentX));
            OnPropertyChanged(nameof(CurrentY));
            OnPropertyChanged(nameof(CurrentZ));

            RaiseAllCanExecuteChanged();
        }

        private void RaiseAllCanExecuteChanged()
        {
            ((AsyncRelayCommand)StartCalibrationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SetPoint1Command).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SetPoint2Command).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SaveCalibrationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogXPlusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogXMinusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogYPlusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogYMinusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogZPlusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)JogZMinusCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)PenUpCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)PenDownCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)HomeAllCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)GoToOriginCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)GoToBottomLeftCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)GoToBottomRightCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)GoToTopLeftCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)GoToTopRightCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)GoToCenterCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)TracePerimeterCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SetPenDownPositionCommand).RaiseCanExecuteChanged();
        }

        // --- CanExecute predicates ---

        private bool CanJog() => IsConnected && !IsBusy;
        private bool CanStartCalibration() => IsConnected && !IsBusy && !IsCalibrating;
        private bool CanSetPoint1() => IsCalibrating && IsConnected && !IsBusy;
        private bool CanSetPoint2() => IsCalibrating && IsConnected && !IsBusy && _point1Captured;
        private bool CanSaveCalibration() => IsCalibrating && IsConnected && CalibrationWidth > 0 && CalibrationHeight > 0;

        // --- Jog implementation ---

        private async Task JogAxis(char axis, double distance)
        {
            if (!IsConnected || IsBusy) return;
            double jogFeedrate = (axis == 'Z') ? 600 : 5000;
            bool success = await _plotterService.JogAsync(axis, distance, jogFeedrate);
            if (success) await _plotterService.GetMachineStateAsync();
        }

        private async Task PenUp()
        {
            if (!IsConnected || IsBusy) return;
            bool success = await _plotterService.PenUpAsync();
            if (success) await _plotterService.GetMachineStateAsync();
        }

        private async Task PenDown()
        {
            if (!IsConnected || IsBusy) return;
            bool success = await _plotterService.PenDownAsync();
            if (success) await _plotterService.GetMachineStateAsync();
        }

        private async Task HomeAllAxes()
        {
            if (!IsConnected || IsBusy) return;
            StatusMessage = "Homing all axes...";
            bool success = await _plotterService.HomeAllAxesAsync();
            StatusMessage = success ? "Homing complete." : "Homing failed.";
            if (success) await _plotterService.GetMachineStateAsync();
        }

        // --- Pen Z calibration ---

        private async Task SetPenDownPosition()
        {
            if (!IsConnected || IsBusy) return;

            // Get fresh position
            var state = await _plotterService.GetMachineStateAsync();
            if (state == null) return;

            double currentZ = state.CurrentZ;
            double liftHeight = _configManager.CurrentConfig.PenLiftHeight;

            _configManager.CurrentConfig.PenDownZ = currentZ;
            _configManager.CurrentConfig.PenUpZ = currentZ + liftHeight;
            _configManager.Save();

            OnPropertyChanged(nameof(DisplayPenDownZ));
            OnPropertyChanged(nameof(DisplayPenUpZ));

            StatusMessage = $"Pen down set to Z={currentZ:F2}mm, pen up to Z={currentZ + liftHeight:F2}mm. Saved.";
        }

        // --- Calibration workflow ---

        private Task StartCalibration()
        {
            IsCalibrating = true;
            _point1Captured = false;
            Point1 = new PlotterPoint(0, 0);
            Point2 = new PlotterPoint(0, 0);
            CalibrationWidth = 0;
            CalibrationHeight = 0;
            StatusMessage = "Jog pen to bottom-left corner of drawing area, then click 'Set Point 1'.\n(Origin 0,0 is valid as Point 1.)";
            ((AsyncRelayCommand)SetPoint2Command).RaiseCanExecuteChanged();
            return Task.CompletedTask;
        }

        private async Task SetPoint1()
        {
            var state = await _plotterService.GetMachineStateAsync();
            if (state != null)
            {
                Point1 = new PlotterPoint(state.CurrentX, state.CurrentY);
                _point1Captured = true;
                ((AsyncRelayCommand)SetPoint2Command).RaiseCanExecuteChanged();
                StatusMessage = $"Point 1 set to ({Point1.X:F2}, {Point1.Y:F2}). Jog pen to top-right corner and click 'Set Point 2'.";
            }
        }

        private async Task SetPoint2()
        {
            var state = await _plotterService.GetMachineStateAsync();
            if (state != null)
            {
                Point2 = new PlotterPoint(state.CurrentX, state.CurrentY);

                double width = Point2.X - Point1.X;
                double height = Point2.Y - Point1.Y;

                if (ValidateCalibration(Point1, Point2, width, height))
                {
                    CalibrationWidth = width;
                    CalibrationHeight = height;
                    StatusMessage = $"Point 2 set to ({Point2.X:F2}, {Point2.Y:F2}). Area: {width:F2}x{height:F2}mm. Click 'Save Calibration'.";
                }
                else
                {
                    StatusMessage = "Validation failed. Point 2 must be top-right of Point 1, and within machine limits.";
                }
            }
        }

        private Task SaveCalibration()
        {
            _currentConfig.CalibrationOrigin = Point1;
            _currentConfig.CalibrationWidth = CalibrationWidth;
            _currentConfig.CalibrationHeight = CalibrationHeight;
            _configManager.Save();
            StatusMessage = $"Calibration saved! Area: {CalibrationWidth:F1}x{CalibrationHeight:F1}mm";
            IsCalibrating = false;
            _point1Captured = false;
            return Task.CompletedTask;
        }

        // --- Corner navigation ---

        private async Task MoveToAbsolute(double x, double y, string label)
        {
            StatusMessage = $"Moving to {label}...";
            await _plotterService.PenUpAsync();
            double feedrate = _currentConfig.RapidFeedrate;
            await _plotterService.SendGCodeAndAwaitOkAsync($"G90");
            await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{x:F3} Y{y:F3} F{feedrate:F0}");
            StatusMessage = $"At {label} ({x:F1}, {y:F1})";
        }

        private Task GoToOrigin() => MoveToAbsolute(0, 0, "machine origin");

        private Task GoToBottomLeft()
        {
            double x = _currentConfig.CalibrationOrigin.X;
            double y = _currentConfig.CalibrationOrigin.Y;
            return MoveToAbsolute(x, y, "bottom-left");
        }

        private Task GoToBottomRight()
        {
            double x = _currentConfig.CalibrationOrigin.X + _currentConfig.CalibrationWidth;
            double y = _currentConfig.CalibrationOrigin.Y;
            return MoveToAbsolute(x, y, "bottom-right");
        }

        private Task GoToTopLeft()
        {
            double x = _currentConfig.CalibrationOrigin.X;
            double y = _currentConfig.CalibrationOrigin.Y + _currentConfig.CalibrationHeight;
            return MoveToAbsolute(x, y, "top-left");
        }

        private Task GoToTopRight()
        {
            double x = _currentConfig.CalibrationOrigin.X + _currentConfig.CalibrationWidth;
            double y = _currentConfig.CalibrationOrigin.Y + _currentConfig.CalibrationHeight;
            return MoveToAbsolute(x, y, "top-right");
        }

        private Task GoToCenter()
        {
            double x = _currentConfig.CalibrationOrigin.X + _currentConfig.CalibrationWidth / 2;
            double y = _currentConfig.CalibrationOrigin.Y + _currentConfig.CalibrationHeight / 2;
            return MoveToAbsolute(x, y, "center");
        }

        private async Task TracePerimeter()
        {
            StatusMessage = "Tracing perimeter...";
            double ox = _currentConfig.CalibrationOrigin.X;
            double oy = _currentConfig.CalibrationOrigin.Y;
            double w = _currentConfig.CalibrationWidth;
            double h = _currentConfig.CalibrationHeight;
            double feed = _currentConfig.DrawFeedrate;

            await _plotterService.PenUpAsync();
            await _plotterService.SendGCodeAndAwaitOkAsync("G90");
            await _plotterService.SendGCodeAndAwaitOkAsync($"G0 X{ox:F3} Y{oy:F3} F{_currentConfig.RapidFeedrate:F0}");
            await _plotterService.PenDownAsync();
            await _plotterService.SendGCodeAndAwaitOkAsync($"G1 X{(ox + w):F3} Y{oy:F3} F{feed:F0}");
            await _plotterService.SendGCodeAndAwaitOkAsync($"G1 X{(ox + w):F3} Y{(oy + h):F3} F{feed:F0}");
            await _plotterService.SendGCodeAndAwaitOkAsync($"G1 X{ox:F3} Y{(oy + h):F3} F{feed:F0}");
            await _plotterService.SendGCodeAndAwaitOkAsync($"G1 X{ox:F3} Y{oy:F3} F{feed:F0}");
            await _plotterService.PenUpAsync();
            StatusMessage = "Perimeter trace complete.";
        }

        // --- Validation ---

        private bool ValidateCalibration(PlotterPoint p1, PlotterPoint p2, double width, double height)
        {
            if (p2.X <= p1.X || p2.Y <= p1.Y)
            {
                StatusMessage = "Error: Point 2 must be top-right of Point 1.";
                return false;
            }

            if (width < 50 || height < 50)
            {
                StatusMessage = "Error: Calibrated area too small (min 50x50mm).";
                return false;
            }

            if (width > _currentConfig.XMaxMM || height > _currentConfig.YMaxMM)
            {
                StatusMessage = $"Error: Area exceeds machine limits ({_currentConfig.XMaxMM}x{_currentConfig.YMaxMM}mm).";
                return false;
            }
            return true;
        }
    }
}
