// PlotterControl/PlotterControl/Services/PlotterService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Ports;
using PlotterControl.Utils;

namespace PlotterControl.Services
{
    public class PlotterService : IPlotterService
    {
        private readonly SerialConnection _serialConnection;
        private MachineState _machineState; // Backing field for MachineState property
        private CancellationTokenSource _readLoopCancellationTokenSource;
        private Task _readLoopTask;
        private ConcurrentQueue<string> _responseQueue; // Thread-safe queue for incoming lines
        private SemaphoreSlim _serialWriteLock = new SemaphoreSlim(1, 1); // Ensure only one write at a time
        private SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1); // Serialize full command-response cycles

        // Plot execution state
        private CancellationTokenSource _plotCancellationTokenSource;
        private Task _plotExecutionTask;
        private volatile bool _isPlottingPaused;

        public event Action<MachineState> MachineStateChanged;
        public event Action<string> LogReceived;
        public event Action<PlotProgress> PlotProgressChanged; // New event for plot progress

        public MachineState MachineState => _machineState; // Public property implementation

        private const int DEFAULT_BAUD_RATE = 115200;
        private const int RESPONSE_TIMEOUT_MS = 30000;  // Timeout for normal moves
        private const int HOMING_TIMEOUT_MS = 120000;   // 2 minutes for full G28 (all 3 axes)
        private const double DEFAULT_Z_FEEDRATE_MM_MIN = 600; // From firmware config.h: MAX_VELOCITY_Z * 60

        public PlotterService(SerialConnection serialConnection)
        {
            _serialConnection = serialConnection ?? throw new ArgumentNullException(nameof(serialConnection));
            _serialConnection.DataReceived += HandleSerialDataReceived;
            _serialConnection.ErrorOccurred += HandleSerialErrorOccurred;
            _serialConnection.ConnectionClosed += HandleConnectionClosed;

            _machineState = new MachineState();
            UpdateMachineState(ms => ms.StatusMessage = "Disconnected");
            _responseQueue = new ConcurrentQueue<string>();
        }

        private void UpdateMachineState(Action<MachineState> updateAction)
        {
            updateAction(_machineState);
            MachineStateChanged?.Invoke(_machineState);
        }

        private void HandleSerialDataReceived(string line)
        {
            Logger.Info($"< {line}");
            LogReceived?.Invoke(line);

            // Enqueue for consumption by awaiting methods
            _responseQueue.Enqueue(line);

            // Passive updates for machine state
            // Only parse as position if line matches strict M114 format (avoid matching debug lines)
            if (Regex.IsMatch(line, @"^X:[\d.-]+ Y:[\d.-]+ Z:[\d.-]+")) ParseM114Response(line);
            else if (line.StartsWith("FIRMWARE_NAME:", StringComparison.OrdinalIgnoreCase)) ParseM115Response(line);
        }
        
        private void HandleSerialErrorOccurred(string errorMessage)
        {
            Logger.Error($"Serial Error: {errorMessage}"); // Changed from LogReceived?.Invoke to Logger.Error
            UpdateMachineState(ms => ms.StatusMessage = $"Error: {errorMessage}");
        }

        private void HandleConnectionClosed()
        {
            StopPlot(); // Ensure any running plot is stopped
            _readLoopCancellationTokenSource?.Cancel(); // Stop polling if active
            _readLoopTask?.Wait(100);

            UpdateMachineState(ms =>
            {
                ms.IsConnected = false;
                ms.PortName = string.Empty;
                ms.StatusMessage = "Disconnected";
                ms.FirmwareName = string.Empty;
                ms.FirmwareVersion = string.Empty;
                ms.IsHomed = false;
                ms.IsBusy = false;
                ms.IsPlotting = false;
            });
            Logger.Info("Connection closed."); // Changed from LogReceived?.Invoke to Logger.Info
        }

        public async Task<List<string>> DiscoverPortsAsync()
        {
            var availablePorts = SerialConnection.GetPortNames().ToList();
            var plotterPorts = new List<string>();

            foreach (var port in availablePorts)
            {
                SerialConnection tempConnection = null;
                try
                {
                    tempConnection = new SerialConnection();
                    tempConnection.Open(port, DEFAULT_BAUD_RATE);
                    
                    if (tempConnection.IsOpen)
                    {
                        await Task.Delay(1500); // Wait for device to boot
                        await tempConnection.WriteLineAsync("M115");
                        string line;
                        var cts = new CancellationTokenSource(3000);

                        while (!cts.IsCancellationRequested)
                        {
                            line = tempConnection.ReadLine();
                            if (line == null) {
                                await Task.Delay(50, cts.Token);
                                continue;
                            }
                            Logger.Info($"> (Discovery) {line}");

                            if (line.StartsWith("FIRMWARE_NAME:", StringComparison.OrdinalIgnoreCase))
                            {
                                plotterPorts.Add(port);
                                break;
                            }
                            else if (line.Equals("ok", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { /* Timeout, expected for non-plotter ports */ }
                catch (TimeoutException) { /* Port not responsive, expected for non-plotter ports */ }
                catch (Exception ex)
                {
                    Logger.Error($"Discovery error on {port}: {ex.Message}", ex); // Changed to Logger.Error
                }
                finally
                {
                    tempConnection?.Close();
                    tempConnection?.Dispose();
                }
            }
            return plotterPorts;
        }


        public async Task<bool> ConnectAsync(string portName, int baudRate)
        {
            if (_serialConnection.IsOpen) await DisconnectAsync();

            _serialConnection.Open(portName, baudRate, Parity.None, 8, StopBits.One);

            if (!_serialConnection.IsOpen)
            {
                UpdateMachineState(ms => ms.StatusMessage = "Failed to open port.");
                return false;
            }

            UpdateMachineState(ms =>
            {
                ms.IsConnected = true;
                ms.PortName = portName;
                ms.BaudRate = baudRate;
                ms.StatusMessage = "Connected. Waiting for firmware boot...";
            });

            // Wait for firmware to boot after DTR reset (Arduino Mega can take 2-3s)
            Logger.Info("Waiting for firmware boot (3s)...");
            await Task.Delay(3000);

            // Check if we already got FIRMWARE_NAME from boot messages (passive parsing)
            bool alreadyIdentified = !string.IsNullOrEmpty(_machineState.FirmwareName);

            // Drain boot messages from queue (already passively parsed by HandleSerialDataReceived)
            int drainedCount = 0;
            while (_responseQueue.TryDequeue(out _)) { drainedCount++; }
            if (drainedCount > 0)
            {
                Logger.Info($"Drained {drainedCount} boot messages from queue.");
            }

            if (!alreadyIdentified)
            {
                // Send empty line first to flush any garbage in firmware buffer
                try
                {
                    await _serialConnection.WriteLineAsync("");
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to send flush newline: {ex.Message}");
                }

                // Query M115 for firmware info with retry
                UpdateMachineState(ms => ms.StatusMessage = "Querying firmware...");
                string m115Response = null;

                for (int attempt = 0; attempt < 3 && m115Response == null; attempt++)
                {
                    if (attempt > 0)
                    {
                        Logger.Info($"M115 retry attempt {attempt + 1}...");
                        await Task.Delay(1000);
                        while (_responseQueue.TryDequeue(out _)) { }
                    }

                    Logger.Info($"Sending M115 (attempt {attempt + 1}/3)...");
                    m115Response = await SendGCodeAndAwaitResponseInternalAsync("M115", "FIRMWARE_NAME:", 5000);

                    if (m115Response != null)
                    {
                        Logger.Info($"M115 response received: {m115Response}");
                    }
                    else
                    {
                        Logger.Warning($"M115 attempt {attempt + 1} - no response.");
                    }
                }

                if (!string.IsNullOrEmpty(m115Response))
                {
                    Logger.Info($"Firmware identified: {m115Response}");
                }
                else
                {
                    Logger.Warning("No firmware response to M115 after 3 retries. Connection may fail.");
                    UpdateMachineState(ms => ms.StatusMessage = "Firmware not responding. Check connection.");
                    return false;
                }
            }
            else
            {
                Logger.Info($"Firmware already identified from boot: {_machineState.FirmwareName}");
            }

            UpdateMachineState(ms => ms.StatusMessage = "Ready.");
            StartPollingMachineState();
            return true;
        }

        public async Task DisconnectAsync()
        {
            StopPlot(); // Stop any running plot
            _readLoopCancellationTokenSource?.Cancel(); // Stop polling
            _readLoopTask?.Wait(100);

            _serialConnection.Close();
            UpdateMachineState(ms => ms.StatusMessage = "Disconnected");
            await Task.Delay(100); // Give time for events to propagate
        }

        public async Task<bool> HomeAllAxesAsync()
        {
            UpdateMachineState(ms => ms.IsBusy = true);
            try
            {
                // G28 can take up to 2 minutes (3 axes × up to 40s each)
                bool success = await SendGCodeAndAwaitOkAsync("G28", HOMING_TIMEOUT_MS);
                if (success)
                {
                    UpdateMachineState(ms =>
                    {
                        ms.IsHomed = true;
                        ms.CurrentX = 0;
                        ms.CurrentY = 0;
                        ms.CurrentZ = 0;
                    });
                }
                return success;
            }
            finally
            {
                UpdateMachineState(ms => ms.IsBusy = false);
            }
        }

        public async Task<bool> JogAsync(char axis, double distance, double feedrate)
        {
            UpdateMachineState(ms => ms.IsBusy = true);
            try
            {
                // Use relative positioning so jogging works without homing
                if (!await SendGCodeAndAwaitOkAsync("G91")) return false;
                try
                {
                    string gcode = $"G0 {axis}{distance:F3} F{feedrate:F0}";
                    bool success = await SendGCodeAndAwaitOkAsync(gcode);
                    return success;
                }
                finally
                {
                    // ALWAYS restore absolute mode, even if G0 fails
                    await SendGCodeAndAwaitOkAsync("G90");
                }
            }
            finally
            {
                UpdateMachineState(ms => ms.IsBusy = false);
            }
        }

        public async Task<bool> PenUpAsync()
        {
            UpdateMachineState(ms => ms.IsBusy = true);
            try
            {
                // GCodeGenerator config in Work_Plan expects PEN_UP_Z to be 0.0
                bool success = await SendGCodeAndAwaitOkAsync($"G0 Z{0.0:F3} F{DEFAULT_Z_FEEDRATE_MM_MIN}");
                return success;
            }
            finally
            {
                UpdateMachineState(ms => ms.IsBusy = false);
            }
        }

        public async Task<bool> PenDownAsync()
        {
            UpdateMachineState(ms => ms.IsBusy = true);
            try
            {
                // GCodeGenerator config in Work_Plan expects PEN_DOWN_Z to be 15.0
                bool success = await SendGCodeAndAwaitOkAsync($"G0 Z{15.0:F3} F{DEFAULT_Z_FEEDRATE_MM_MIN}");
                return success;
            }
            finally
            {
                UpdateMachineState(ms => ms.IsBusy = false);
            }
        }

        public async Task<bool> SetSpeedOverrideAsync(int percent)
        {
            percent = Math.Clamp(percent, 10, 200);
            return await SendGCodeAndAwaitOkAsync($"M220 S{percent}");
        }

        public async Task<MachineState> GetMachineStateAsync()
        {
            if (!_serialConnection.IsOpen) return _machineState;

            // Non-blocking lock acquisition for polling — skip this cycle if lock is held
            // (e.g., by a user jog command). Prevents jog from blocking for 30s.
            if (!await _commandLock.WaitAsync(100)) return _machineState;
            try
            {
                // M114 - get position
                string m114_response = await SendGCodeAndAwaitResponseInternalAsync("M114", "X:", RESPONSE_TIMEOUT_MS);
                if (m114_response == null) Logger.Warning("M114 response timed out.");

                // M119 - get endstop status (multi-line: x_min, y_min, z_min, then ok)
                await SendGCodeAsync("M119");
                string x_min_response = await AwaitResponseStartingWithAsync("x_min:", RESPONSE_TIMEOUT_MS);
                string y_min_response = await AwaitResponseStartingWithAsync("y_min:", RESPONSE_TIMEOUT_MS);
                string z_min_response = await AwaitResponseStartingWithAsync("z_min:", RESPONSE_TIMEOUT_MS);
                bool m119_ok = await AwaitResponseAsync("ok", RESPONSE_TIMEOUT_MS);

                if (x_min_response == null || y_min_response == null || z_min_response == null || !m119_ok)
                {
                    Logger.Warning("M119 response timed out or incomplete.");
                }
                else
                {
                    ParseM119Response(x_min_response, y_min_response, z_min_response);
                }
            }
            finally
            {
                _commandLock.Release();
            }

            MachineStateChanged?.Invoke(_machineState);
            return _machineState;
        }

        public async Task<bool> SendGCodeAsync(string gcodeCommand)
        {
            if (!_serialConnection.IsOpen)
            {
                Logger.Warning($"NOT CONNECTED: {gcodeCommand}"); // Changed to Logger.Warning
                return false;
            }
            Logger.Info($"> {gcodeCommand}"); // Changed from LogReceived?.Invoke to Logger.Info

            await _serialWriteLock.WaitAsync(); // Acquire lock before writing
            try
            {
                await _serialConnection.WriteLineAsync(gcodeCommand);
            }
            finally
            {
                _serialWriteLock.Release(); // Release lock
            }
            return true;
        }

        public Task<bool> SendGCodeAndAwaitOkAsync(string gcodeCommand)
            => SendGCodeAndAwaitOkAsync(gcodeCommand, RESPONSE_TIMEOUT_MS);

        public async Task<bool> SendGCodeAndAwaitOkAsync(string gcodeCommand, int timeoutMs)
        {
            // 5s timeout acquiring lock prevents indefinite blocking if polling holds it
            if (!await _commandLock.WaitAsync(5000))
            {
                Logger.Warning($"Command lock timeout for: {gcodeCommand}");
                return false;
            }
            try
            {
                if (!await SendGCodeAsync(gcodeCommand)) return false;
                return await AwaitResponseAsync("ok", timeoutMs);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        // Awaits a specific response line starting with a prefix, then consumes the 'ok'
        public async Task<string> SendGCodeAndAwaitResponseAsync(string gcodeCommand, string responsePrefix, int timeoutMs = 5000)
        {
            await _commandLock.WaitAsync();
            try
            {
                return await SendGCodeAndAwaitResponseInternalAsync(gcodeCommand, responsePrefix, timeoutMs);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        private async Task<string> SendGCodeAndAwaitResponseInternalAsync(string gcodeCommand, string responsePrefix, int timeoutMs)
        {
            if (!string.IsNullOrEmpty(gcodeCommand))
            {
                if (!await SendGCodeAsync(gcodeCommand)) return null;
            }

            var timeoutCts = new CancellationTokenSource(timeoutMs);
            try
            {
                string responseLine = null;
                while (true)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    
                    if (_responseQueue.TryDequeue(out var line))
                    {
                        if (line.StartsWith(responsePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            responseLine = line;
                            // The final 'ok' should be consumed by AwaitResponseAsync, if needed by the caller.
                            // For M115, the ParseM115Response will update the state,
                            // and the final 'ok' will be dequeued by AwaitResponseAsync("ok").
                            break;
                        }
                        else if (line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Error($"Command '{gcodeCommand}' failed with: {line}"); // Changed to Logger.Error
                            return null;
                        }
                        // Discard unsolicited "ok" if it appears before the expected response
                        else if (line.Equals("ok", StringComparison.OrdinalIgnoreCase)) {
                             Logger.Info($"Discarding unsolicited 'ok' while awaiting '{responsePrefix}'"); // Changed to Logger.Info
                        }
                        else // Discard other unsolicited messages
                        {
                            Logger.Info($"Discarding unexpected response: {line}"); // Changed to Logger.Info
                        }
                    }
                    await Task.Delay(50, timeoutCts.Token); // Small delay to prevent busy-waiting
                }
                return responseLine;
            }
            catch (OperationCanceledException)
            {
                Logger.Warning($"Timeout awaiting response for '{gcodeCommand}' starting with '{responsePrefix}'"); // Changed to Logger.Warning
                return null;
            }
            finally
            {
                timeoutCts.Dispose();
            }
        }
        
        // Awaits a specific single response line (like "ok")
        private async Task<bool> AwaitResponseAsync(string expectedResponse, int timeoutMs = RESPONSE_TIMEOUT_MS)
        {
            var timeoutCts = new CancellationTokenSource(timeoutMs);
            try
            {
                while (true)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();

                    if (_responseQueue.TryDequeue(out var responseLine))
                    {
                        if (responseLine.Equals(expectedResponse, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                        else if (responseLine.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Error($"Command failed with: {responseLine}"); // Changed to Logger.Error
                            return false; // Error received instead of expected
                        }
                        else
                        {
                            Logger.Info($"Discarding unexpected response while awaiting '{expectedResponse}': {responseLine}"); // Changed to Logger.Info
                        }
                    }
                    await Task.Delay(50, timeoutCts.Token); // Small delay to prevent busy-waiting
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Warning($"Timeout awaiting '{expectedResponse}'"); // Changed to Logger.Warning
                return false;
            }
            finally
            {
                timeoutCts.Dispose();
            }
        }
        
        // Awaits a single response line starting with a prefix, without sending a command first
        private async Task<string> AwaitResponseStartingWithAsync(string responsePrefix, int timeoutMs = RESPONSE_TIMEOUT_MS)
        {
            var timeoutCts = new CancellationTokenSource(timeoutMs);
            try
            {
                while (true)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    
                    if (_responseQueue.TryDequeue(out var line))
                    {
                        if (line.StartsWith(responsePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return line;
                        }
                        else if (line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Error($"Error while awaiting '{responsePrefix}': {line}"); // Changed to Logger.Error
                            return null;
                        }
                        else // Discard other unsolicited messages
                        {
                            Logger.Info($"Discarding unexpected response: {line}"); // Changed to Logger.Info
                        }
                    }
                    await Task.Delay(50, timeoutCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Warning($"Timeout awaiting response starting with '{responsePrefix}'"); // Changed to Logger.Warning
                return null;
            }
            finally
            {
                timeoutCts.Dispose();
            }
        }

        private DateTime _lastBusySetTime = DateTime.MinValue;

        private void StartPollingMachineState()
        {
            _readLoopCancellationTokenSource = new CancellationTokenSource();
            _readLoopTask = Task.Run(async () =>
            {
                while (!_readLoopCancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(2000);

                    // IsBusy watchdog: if IsBusy has been true for >60s without an active plot,
                    // reset it to prevent permanent stuck state from command timeouts.
                    if (_machineState.IsBusy && !_machineState.IsPlotting)
                    {
                        if (_lastBusySetTime == DateTime.MinValue)
                        {
                            _lastBusySetTime = DateTime.Now;
                        }
                        else if ((DateTime.Now - _lastBusySetTime).TotalSeconds > 60)
                        {
                            Logger.Warning("IsBusy watchdog: resetting stuck IsBusy flag after 60s timeout.");
                            UpdateMachineState(ms => ms.IsBusy = false);
                            _lastBusySetTime = DateTime.MinValue;
                        }
                    }
                    else
                    {
                        _lastBusySetTime = DateTime.MinValue;
                    }

                    // Only poll when connected AND NOT busy to avoid command lock deadlocks
                    // during homing, plotting, or other long operations.
                    if (_serialConnection.IsOpen && !_machineState.IsBusy && !_machineState.IsPlotting)
                    {
                        await GetMachineStateAsync();
                    }
                }
            }, _readLoopCancellationTokenSource.Token);
        }

        // Response Parsing
        private void ParseM114Response(string response)
        {
            // Example: X:0.00 Y:0.00 Z:0.00 E:0.00 Count A:0 B:0 C:0
            // The M114 in firmware now only outputs X: Y: Z:
            var match = Regex.Match(response, @"X:([\d.-]+)\s+Y:([\d.-]+)\s+Z:([\d.-]+)");
            if (match.Success)
            {
                UpdateMachineState(ms =>
                {
                    ms.CurrentX = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    ms.CurrentY = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    ms.CurrentZ = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                });
            }
        }

        private void ParseM115Response(string response)
        {
            // Example: FIRMWARE_NAME:SimplePlotter FIRMWARE_VERSION:1.0 PROTOCOL_VERSION:1.0 ...
            var nameMatch = Regex.Match(response, @"FIRMWARE_NAME:([^\s]+)");
            var versionMatch = Regex.Match(response, @"FIRMWARE_VERSION:([^\s]+)");

            UpdateMachineState(ms =>
            {
                if (nameMatch.Success) ms.FirmwareName = nameMatch.Groups[1].Value;
                if (versionMatch.Success) ms.FirmwareVersion = versionMatch.Groups[1].Value;
            });
        }

        private void ParseM119Response(string x_min_response, string y_min_response, string z_min_response)
        {
            // Example lines:
            // x_min: open
            // y_min: TRIGGERED
            // z_min: open
            var xMatch = Regex.Match(x_min_response, @"x_min:\s*(\w+)");
            var yMatch = Regex.Match(y_min_response, @"y_min:\s*(\w+)");
            var zMatch = Regex.Match(z_min_response, @"z_min:\s*(\w+)");

            UpdateMachineState(ms =>
            {
                if (xMatch.Success) ms.XMinTriggered = xMatch.Groups[1].Value.Equals("TRIGGERED", StringComparison.OrdinalIgnoreCase);
                if (yMatch.Success) ms.YMinTriggered = yMatch.Groups[1].Value.Equals("TRIGGERED", StringComparison.OrdinalIgnoreCase);
                if (zMatch.Success) ms.ZMinTriggered = zMatch.Groups[1].Value.Equals("TRIGGERED", StringComparison.OrdinalIgnoreCase);
            });
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
            _serialConnection.Dispose();
        }

        public async Task<bool> ExecutePlotAsync(string gcode, CancellationToken cancellationToken, IProgress<PlotProgress> progress = null)
        {
            if (!_serialConnection.IsOpen) return false;
            
            UpdateMachineState(ms =>
            {
                ms.IsPlotting = true;
                ms.IsBusy = true;
            });
            _isPlottingPaused = false;
            
            _plotCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            var lines = gcode.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith(";")).ToArray();
            var startTime = DateTime.Now;

            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    _plotCancellationTokenSource.Token.ThrowIfCancellationRequested();

                    while (_isPlottingPaused)
                    {
                        await Task.Delay(100, _plotCancellationTokenSource.Token);
                    }

                    var line = lines[i].Trim();
                    bool success = await SendGCodeAndAwaitOkAsync(line);
                    
                    if (!success)
                    {
                        Logger.Error($"Plot failed at line {i+1}: {line}");
                        return false;
                    }

                    if (progress != null)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var percentComplete = (i + 1) * 100.0 / lines.Length;
                        var estimatedTotalSeconds = (percentComplete > 1) ? elapsed.TotalSeconds / percentComplete * 100 : 0;
                        var remaining = (estimatedTotalSeconds > 0) ? TimeSpan.FromSeconds(estimatedTotalSeconds - elapsed.TotalSeconds) : TimeSpan.Zero;
                        
                        var progressReport = new PlotProgress
                        {
                            LinesCompleted = i + 1,
                            TotalLines = lines.Length,
                            PercentComplete = percentComplete,
                            EstimatedTimeRemaining = remaining,
                            CurrentCommand = line,
                            IsPaused = _isPlottingPaused
                        };
                        progress.Report(progressReport);
                        PlotProgressChanged?.Invoke(progressReport);
                    }
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Plot canceled.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Plotting error: {ex.Message}", ex);
                return false;
            }
            finally
            {
                UpdateMachineState(ms =>
                {
                    ms.IsPlotting = false;
                    ms.IsBusy = false;
                });
                _plotCancellationTokenSource?.Dispose();
                _plotCancellationTokenSource = null;
            }
        }

        public void PausePlot()
        {
            if (_machineState.IsPlotting)
            {
                _isPlottingPaused = true;
                UpdateMachineState(ms => ms.StatusMessage = "Plotting paused.");
            }
        }

        public void ResumePlot()
        {
            if (_machineState.IsPlotting)
            {
                _isPlottingPaused = false;
                UpdateMachineState(ms => ms.StatusMessage = "Plotting resumed.");
            }
        }

        public void StopPlot()
        {
            if (_plotCancellationTokenSource != null && !_plotCancellationTokenSource.IsCancellationRequested)
            {
                _plotCancellationTokenSource.Cancel();
            }
            
            // Send quickstop command and lift pen as a safety measure
            Task.Run(async () =>
            {
                await SendGCodeAsync("M410"); // Quickstop
                await SendGCodeAsync($"G0 Z{0.0:F3} F{DEFAULT_Z_FEEDRATE_MM_MIN}"); // Pen up
            });
            
            _isPlottingPaused = false;
            UpdateMachineState(ms =>
            {
                ms.IsPlotting = false;
                ms.IsBusy = false;
                ms.StatusMessage = "Plot stopped by user.";
            });
        }
    }
}