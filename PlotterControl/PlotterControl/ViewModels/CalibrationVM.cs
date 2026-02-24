// PlotterControl/PlotterControl/ViewModels/CalibrationVM.cs

using PlotterControl.Models;
using PlotterControl.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlotterControl.ViewModels
{
    public class CalibrationVM : BaseViewModel
    {
        private readonly IPlotterService _plotterService;
        private readonly ConfigManager _configManager;
        private PlotterConfig _currentConfig;

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
                    ((AsyncRelayCommand)StartCalibrationCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)SetPoint1Command).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)SetPoint2Command).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)SaveCalibrationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        // Commands
        public ICommand StartCalibrationCommand { get; }
        public ICommand SetPoint1Command { get; }
        public ICommand SetPoint2Command { get; }
        public ICommand SaveCalibrationCommand { get; }

        // Corner navigation commands
        public ICommand GoToOriginCommand { get; }
        public ICommand GoToBottomLeftCommand { get; }
        public ICommand GoToBottomRightCommand { get; }
        public ICommand GoToTopLeftCommand { get; }
        public ICommand GoToTopRightCommand { get; }
        public ICommand GoToCenterCommand { get; }
        public ICommand TracePerimeterCommand { get; }

        public CalibrationVM(IPlotterService plotterService, ConfigManager configManager)
        {
            _plotterService = plotterService;
            _configManager = configManager;

            _currentConfig = _configManager.CurrentConfig;

            // Initialize properties from config
            Point1 = _currentConfig.CalibrationOrigin;
            Point2 = new PlotterPoint(_currentConfig.CalibrationOrigin.X + _currentConfig.CalibrationWidth,
                                      _currentConfig.CalibrationOrigin.Y + _currentConfig.CalibrationHeight);
            CalibrationWidth = _currentConfig.CalibrationWidth;
            CalibrationHeight = _currentConfig.CalibrationHeight;

            // Calibration workflow commands
            StartCalibrationCommand = new AsyncRelayCommand(StartCalibration, CanStartCalibration);
            SetPoint1Command = new AsyncRelayCommand(SetPoint1, CanSetPoint1);
            SetPoint2Command = new AsyncRelayCommand(SetPoint2, CanSetPoint2);
            SaveCalibrationCommand = new AsyncRelayCommand(SaveCalibration, CanSaveCalibration);

            // Corner navigation commands
            GoToOriginCommand = new AsyncRelayCommand(GoToOrigin, CanNavigate);
            GoToBottomLeftCommand = new AsyncRelayCommand(GoToBottomLeft, CanNavigate);
            GoToBottomRightCommand = new AsyncRelayCommand(GoToBottomRight, CanNavigate);
            GoToTopLeftCommand = new AsyncRelayCommand(GoToTopLeft, CanNavigate);
            GoToTopRightCommand = new AsyncRelayCommand(GoToTopRight, CanNavigate);
            GoToCenterCommand = new AsyncRelayCommand(GoToCenter, CanNavigate);
            TracePerimeterCommand = new AsyncRelayCommand(TracePerimeter, CanNavigate);

            StatusMessage = "Load existing calibration or start new.";
        }

        // --- Calibration workflow ---

        private bool CanStartCalibration() => _plotterService.MachineState.IsConnected && !_plotterService.MachineState.IsBusy && !IsCalibrating;
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

        private bool CanSetPoint1() => IsCalibrating && _plotterService.MachineState.IsConnected && !_plotterService.MachineState.IsBusy;
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

        private bool CanSetPoint2() => IsCalibrating && _plotterService.MachineState.IsConnected && !_plotterService.MachineState.IsBusy && _point1Captured;
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

        private bool CanSaveCalibration() => IsCalibrating && _plotterService.MachineState.IsConnected && CalibrationWidth > 0 && CalibrationHeight > 0;
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

        private bool CanNavigate() => _plotterService.MachineState.IsConnected && !_plotterService.MachineState.IsBusy;

        private async Task MoveToAbsolute(double x, double y, string label)
        {
            StatusMessage = $"Moving to {label}...";
            // Pen up first, then move XY, all absolute
            await _plotterService.PenUpAsync();
            double feedrate = _currentConfig.RapidFeedrate;
            await _plotterService.SendGCodeAsync($"G90");
            await _plotterService.SendGCodeAsync($"G0 X{x:F3} Y{y:F3} F{feedrate:F0}");
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
            await _plotterService.SendGCodeAsync("G90");
            // Move to bottom-left
            await _plotterService.SendGCodeAsync($"G0 X{ox:F3} Y{oy:F3} F{_currentConfig.RapidFeedrate:F0}");
            // Pen down
            await _plotterService.PenDownAsync();
            // Trace rectangle: BL -> BR -> TR -> TL -> BL
            await _plotterService.SendGCodeAsync($"G1 X{(ox + w):F3} Y{oy:F3} F{feed:F0}");
            await _plotterService.SendGCodeAsync($"G1 X{(ox + w):F3} Y{(oy + h):F3} F{feed:F0}");
            await _plotterService.SendGCodeAsync($"G1 X{ox:F3} Y{(oy + h):F3} F{feed:F0}");
            await _plotterService.SendGCodeAsync($"G1 X{ox:F3} Y{oy:F3} F{feed:F0}");
            // Pen up
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
