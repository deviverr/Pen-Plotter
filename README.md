# SimplePlotter

A complete pen plotter system — custom firmware for an MKS Gen v1.4 board paired with a Windows desktop app for generating and sending G-code.

Built from scratch as a personal project. The plotter draws text, images, and arbitrary paths on paper using a pen mounted on an XY gantry with a Z-axis lift mechanism.

---

## What's in This Repo

```
SimplePlotter_Firmware/     ATmega2560 firmware (C++ / Arduino / PlatformIO)
PlotterControl/             Windows control app (C# / WPF / .NET 9)
```

---

## Firmware

The firmware runs on an **MKS Gen v1.4** board and interprets G-code commands received over USB serial or read from an SD card.

### Capabilities

- **G-code motion** — Linear moves (G0/G1), absolute/relative positioning (G90/G91), coordinate reset (G92)
- **Homing** — Per-axis homing with fast approach, backoff, and slow precision pass. Configurable acceleration ramp-down for smooth endstop engagement
- **SD card execution** — Browse and run `.gcode` files from an SD card, with pause (M25) and resume (M24)
- **Speed override** — Physical potentiometer knob (10–200%) and M220 command for real-time feed rate adjustment
- **LCD menu** — Full menu system on a 128x64 ST7920 display with rotary encoder: manual jog, homing, pen settings, motion settings, SD file browser, plot preview
- **Safety** — 8-second hardware watchdog, soft limits, stepper idle timeout, endstop debouncing

### Supported G-code

| Command | Description |
|---------|-------------|
| `G0/G1` | Linear move (with optional F feed rate) |
| `G28`   | Home one or all axes |
| `G90`   | Absolute positioning |
| `G91`   | Relative positioning |
| `G92`   | Set current position |
| `M0`    | Stop execution |
| `M24`   | Resume SD execution |
| `M25`   | Pause SD execution |
| `M84`   | Disable steppers |
| `M114`  | Report position |
| `M115`  | Firmware info |
| `M119`  | Endstop status |
| `M220`  | Set speed factor (%) |
| `M410`  | Quick stop |
| `M503`  | Report settings |

### Build & Flash

Requires [PlatformIO](https://platformio.org/).

```bash
cd SimplePlotter_Firmware
pio run                     # compile
pio run --target upload     # flash via USB
pio device monitor          # serial monitor (115200 baud)
```

---

## Control App

A WPF desktop application for Windows that connects to the plotter over serial and provides a full workflow — from content creation to plotting.

### Features

- **Connect** — Auto-detect serial ports, one-click connect at 115200 baud
- **Manual control** — Jog X/Y/Z with configurable step sizes, home individual axes or all at once
- **Text to G-code** — Type text and render it using built-in Hershey vector fonts (Simplex, Script, Gothic) or system TrueType fonts
- **Image to G-code** — Import images and convert them to plottable paths using scan lines, edge detection, or dithering algorithms
- **Plot preview** — Live visualization of generated G-code overlaid on the machine's work area with paper boundaries
- **Calibration** — Step-by-step calibration wizard for verifying axis travel and pen alignment
- **Plotting workflow** — Confirmation dialog, automatic homing if needed, corner preview (pen traces the bounding box with pen raised), then plots
- **Pause / Resume / Stop** — Full job control during plotting
- **Speed override** — Slider to adjust feed rate in real-time (sends M220)
- **Humanization** — Optional jitter, wobble, and pressure variation to simulate handwriting
- **Theme** — Light and dark mode toggle (saved in config)
- **Notifications** — Snackbar warnings for connection loss, firmware errors, and other events
- **Serial log** — Scrollable log of all serial traffic with save/export

### Build & Run

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
cd PlotterControl
dotnet build
dotnet run --project PlotterControl
```

### Serial Protocol

The firmware communicates over USB serial at **115200 baud** using a line-based protocol. Every command receives a response.

**Response format:**
- `ok` — Command accepted and executed
- `error:<code> <description>` — Command failed
- `// <message>` — Informational message (diagnostics, status updates)

**Error codes:**

| Code | Name | Description |
|------|------|-------------|
| 1 | Unknown Command | Unrecognized G-code or M-code |
| 2 | Invalid Syntax | Malformed G-code (missing parameters, bad format) |
| 3 | Out of Range | Target position exceeds soft limits or maximum allowed jump |
| 4 | Endstop Hit | Endstop triggered during a move (safety stop) |
| 5 | Homing Failed | Homing sequence failed — endstop not found, backoff failed, or timeout |
| 6 | Not Homed | Attempted absolute move (G90) without homing first — send G28 |
| 7 | Buffer Overflow | G-code command buffer full or line exceeds 64 characters |
| 8 | Timeout | General operation timeout |
| 9 | Empty Command | Empty or whitespace-only line received |

**Position report format** (M114 response):
```
X:123.45 Y:67.89 Z:2.00
```

---

## Hardware

| Component | Specification |
|-----------|---------------|
| **Controller** | MKS Gen v1.4 (ATmega2560) |
| **X/Y drive** | GT2 belt + 20-tooth pulleys, 1/16 microstepping (80 steps/mm) |
| **Z drive** | T8 leadscrew, 1/16 microstepping (400 steps/mm) |
| **X endstop** | Optical sensor (active HIGH) |
| **Y endstop** | Mechanical switch (active LOW) |
| **Z endstop** | Optical sensor (active HIGH) |
| **Display** | RepRap Discount Full Graphic Smart Controller (ST7920, 128x64) |
| **Speed knob** | Analog potentiometer on A0 |
| **Communication** | USB serial via CH340, 115200 baud |

---

## Project Structure

```
SimplePlotter_Firmware/
├── src/
│   ├── main.cpp              # Main loop — polling, G-code dispatch
│   ├── config.h              # All pin mappings & machine constants
│   ├── globals.h             # Shared state & extern declarations
│   ├── gcode/                # G-code parser, command types, ring buffer
│   ├── motion/               # Stepper control, kinematics, homing
│   ├── io/                   # Serial, endstops, SD card, potentiometer, buzzer
│   ├── ui/                   # LCD screens, encoder, menu navigation
│   └── utils/                # Math helpers, ring buffer, timing
└── platformio.ini

PlotterControl/
└── PlotterControl/
    ├── App.xaml               # Application entry + theme init
    ├── MainWindow.xaml        # Shell layout with tab navigation
    ├── Models/                # Data models (config, fonts, paths, points)
    ├── ViewModels/            # MVVM view models for each tab
    ├── Views/                 # XAML views (editor, control panel, settings, calibration)
    ├── Services/              # Serial, G-code generation, image processing, text rendering
    ├── Controls/              # Custom WPF controls (plot preview canvas)
    └── Resources/             # Hershey font JSON files, app icon
```

---

## License

MIT
