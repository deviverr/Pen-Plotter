// PlotterControl/PlotterControl/Services/IPlotterService.cs

using System;
using System.Collections.Generic;
using System.Threading; // For CancellationToken
using System.Threading.Tasks;

namespace PlotterControl.Services
{
    // Represents the current machine state
    public class MachineState
    {
        public bool IsConnected { get; set; } = false;
        public bool IsHomed { get; set; } = false;
        public double CurrentX { get; set; } = 0.0;
        public double CurrentY { get; set; } = 0.0;
        public double CurrentZ { get; set; } = 0.0;
        public bool IsBusy { get; set; } = false; // If a command is being processed
        public bool IsPlotting { get; set; } = false; // If a long plot job is active
        public string StatusMessage { get; set; } = "Disconnected";
        public int BaudRate { get; set; } = 115200;
        public string PortName { get; set; } = string.Empty;
        public string FirmwareName { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public bool XMinTriggered { get; set; } = false;
        public bool YMinTriggered { get; set; } = false;
        public bool ZMinTriggered { get; set; } = false;
    }

    // Represents the progress of a plotting job
    public class PlotProgress
    {
        public int LinesCompleted { get; set; }
        public int TotalLines { get; set; }
        public double PercentComplete { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public string CurrentCommand { get; set; }
        public bool IsPaused { get; set; }
    }

    public interface IPlotterService : IDisposable
    {
        // Events
        event Action<MachineState> MachineStateChanged;
        event Action<string> LogReceived; // For messages from firmware (e.g. "ok", "error", "// Info")
        event Action<PlotProgress> PlotProgressChanged; // For plot progress updates

        // Properties
        MachineState MachineState { get; } // For current machine state

        // Connection Management
        Task<List<string>> DiscoverPortsAsync();
        Task<bool> ConnectAsync(string portName, int baudRate);
        Task DisconnectAsync();

        // Machine Control
        Task<bool> HomeAllAxesAsync();
        Task<bool> JogAsync(char axis, double distance, double feedrate);
        Task<bool> PenUpAsync();
        Task<bool> PenDownAsync();
        Task<bool> SetSpeedOverrideAsync(int percent);
        Task<MachineState> GetMachineStateAsync(); // Polls current state (e.g., M114, M119, M115)

        // G-code Sending
        Task<bool> SendGCodeAsync(string gcodeCommand);
        Task<bool> SendGCodeAndAwaitOkAsync(string gcodeCommand); // Added missing declaration
        Task<string> SendGCodeAndAwaitResponseAsync(string gcodeCommand, string responsePrefix, int timeoutMs = 5000);

        // Plot Execution
        Task<bool> ExecutePlotAsync(string gcode, CancellationToken cancellationToken, IProgress<PlotProgress> progress = null);
        void PausePlot();
        void ResumePlot();
        void StopPlot();
    }
}
