using System;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using PlotterControl.Utils;

namespace PlotterControl.Services
{
    public class SerialConnection : IDisposable
    {
        private SerialPort _serialPort;
        private StringBuilder _lineBuffer = new StringBuilder();
        private readonly object _bufferLock = new object();

        public event Action<string> DataReceived;
        public event Action<string> ErrorOccurred;
        public event Action ConnectionClosed;

        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;
        public string PortName => _serialPort?.PortName;

        public void Open(string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            if (IsOpen)
            {
                Close();
            }

            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = false
            };

            try
            {
                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                lock (_bufferLock)
                {
                    _lineBuffer.Clear();
                }

                _serialPort.DataReceived += SerialPort_DataReceived;

                Logger.Info($"Connected to {portName} at {baudRate} baud.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to open serial port {portName}: {ex.Message}", ex);
                _serialPort?.Dispose();
                _serialPort = null;
            }
        }

        public void Close()
        {
            if (_serialPort == null) return;

            try
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;

                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error closing serial port: {ex.Message}", ex);
            }
            finally
            {
                _serialPort?.Dispose();
                _serialPort = null;
            }

            ConnectionClosed?.Invoke();
            Logger.Info("Disconnected.");
        }

        public async Task WriteLineAsync(string data)
        {
            if (!IsOpen)
            {
                Logger.Warning("Attempted to write to a closed serial port.");
                return;
            }

            try
            {
                await Task.Run(() => _serialPort.WriteLine(data));
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to write to serial port: {ex.Message}", ex);
            }
        }

        public string ReadLine()
        {
            if (!IsOpen) return null;
            try
            {
                return _serialPort.ReadLine();
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read from serial port: {ex.Message}", ex);
                return null;
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort sp = (SerialPort)sender;
                if (!sp.IsOpen) return;

                string indata = sp.ReadExisting();
                if (string.IsNullOrEmpty(indata)) return;

                lock (_bufferLock)
                {
                    _lineBuffer.Append(indata);

                    string buffered = _lineBuffer.ToString();
                    int lastNewline = buffered.LastIndexOf('\n');

                    if (lastNewline >= 0)
                    {
                        string completePart = buffered.Substring(0, lastNewline);
                        _lineBuffer = new StringBuilder(buffered.Substring(lastNewline + 1));

                        var lines = completePart.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            string trimmed = line.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                DataReceived?.Invoke(trimmed);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in DataReceived handler: {ex.Message}", ex);
            }
        }

        public static string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }

        public void Dispose()
        {
            Close();
        }
    }
}
