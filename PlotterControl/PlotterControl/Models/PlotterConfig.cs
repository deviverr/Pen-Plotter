// PlotterControl/PlotterControl/Models/PlotterConfig.cs

namespace PlotterControl.Models
{
    public class PlotterConfig
    {
        // Machine Dimensions & Calibration
        public double XMaxMM { get; set; } = 234.0; // Max X travel in mm (measured pen travel)
        public double YMaxMM { get; set; } = 191.0; // Max Y travel in mm (measured bed travel)
        public double ZMaxMM { get; set; } = 203.0; // Max Z travel in mm

        public PlotterPoint CalibrationOrigin { get; set; } = new PlotterPoint(0, 0); // User-defined (0,0) point
        public double CalibrationWidth { get; set; } = 180.0; // User-defined printable width
        public double CalibrationHeight { get; set; } = 180.0; // User-defined printable height

        // Pen/Z-Axis Settings (Z=0 is at endstop/paper level, Z+ moves up)
        public double PenUpZ { get; set; } = 3.0;    // Z-height for pen up (raised above paper)
        public double PenDownZ { get; set; } = 0.5;  // Z-height for pen down (paper contact)
        public double PenLiftHeight { get; set; } = 2.5; // Distance pen lifts above PenDownZ

        // Pen Tip & Pressure Settings
        public double PenTipSizeMM { get; set; } = 0.5;   // Pen tip diameter (0.3, 0.5, 0.7, 1.0 common)
        public double PenPressureMM { get; set; } = 0.1;  // Base Z offset below PenDownZ (0.0=light, 0.5=heavy)

        // Movement Speeds (mm/min)
        public double RapidFeedrate { get; set; } = 5000.0; // G0/rapid movement
        public double DrawFeedrate { get; set; } = 2000.0;  // G1/drawing movement
        public double ZFeedrate { get; set; } = 600.0;     // Z-axis movement

        // Communication Settings
        public string LastComPort { get; set; } = string.Empty;
        public int BaudRate { get; set; } = 115200;
        public int CommandTimeout { get; set; } = 5000; // Milliseconds to wait for an 'ok' response

        // UI/Application Settings
        public string DefaultFont { get; set; } = "hershey_simplex";
        public string DefaultTemplate { get; set; } = "custom_blank";
        public bool ShowGrid { get; set; } = true;
        public bool AutoConnect { get; set; } = false;
        public bool DarkTheme { get; set; } = true;
    }
}
