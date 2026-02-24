# Pen Plotter

A DIY pen plotter project with custom firmware and Windows control application.

## Components

### SimplePlotter Firmware
Custom firmware for the **MKS Gen v1.4** board (ATmega2560). Written in C++ with Arduino framework and built with PlatformIO.

**Features:**
- G-code interpreter (G0/G1, G28, G90/G91, G92, M-codes)
- 3-axis stepper control (X, Y, Z pen lift) via AccelStepper
- ST7920 128x64 LCD with rotary encoder menu system
- SD card G-code execution
- Endstop homing with fast/slow approach
- Real-time speed override via potentiometer and M220
- Pause/resume support (M24/M25)
- Serial protocol at 115200 baud

### PlotterControl (Windows App)
WPF desktop application (.NET 9) for controlling the plotter.

**Features:**
- Serial connection with auto-discovery
- Manual jog controls and homing
- Calibration workflow with corner navigation
- Text rendering (Hershey fonts + system fonts)
- Image to G-code conversion (scan lines, contours, dithering)
- Plot preview with machine/paper visualization
- G-code generation with path optimization
- Auto-home before plotting with area preview
- Plot confirmation dialog
- Pause/resume/stop during plotting
- Speed override slider
- Light/dark theme toggle
- Human writing simulation (jitter, wobble, pressure)
- Serial log viewer with save/export

## Hardware

- **Board:** MKS Gen v1.4 (ATmega2560)
- **X/Y:** GT2 belt + 20-tooth pulleys, 1/16 microstepping (80 steps/mm)
- **Z:** T8 leadscrew, 1/16 microstepping (400 steps/mm)
- **Endstops:** X/Z optical (HIGH=triggered), Y mechanical (active-LOW)
- **Display:** RepRap Discount Full Graphic Smart Controller (ST7920)
- **Speed knob:** Analog potentiometer on A0

## Build Instructions

### Firmware
```bash
cd SimplePlotter_Firmware
pio run                    # Build
pio run --target upload    # Flash
pio device monitor         # Serial monitor (115200 baud)
```

### Control App
```bash
cd PlotterControl
dotnet build
dotnet run --project PlotterControl
```

Requires .NET 9 SDK on Windows.

## License
MIT License
