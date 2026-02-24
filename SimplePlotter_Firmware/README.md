# SimplePlotter Firmware

## Build & Flash Instructions

### Requirements
- PlatformIO (VS Code extension or CLI)
- MKS Gen v1.4 board (ATmega2560)
- USB cable (CH340 driver installed)

### Build
1. Open this folder in VS Code
2. Install PlatformIO extension
3. Click "Build" (checkmark icon) or run:
   ```
   pio run
   ```

### Flash
1. Connect MKS Gen v1.4 via USB
2. Click "Upload" (right arrow icon) or run:
   ```
   pio run --target upload
   ```

### Serial Monitor
- Use PlatformIO serial monitor or any terminal at 115200 baud

### Troubleshooting
- If upload fails, check CH340 driver and board selection
- If firmware does not boot, verify pin mappings in config.h

### Directory Structure
- `src/` - Firmware source code
- `platformio.ini` - Build config

### Updating Machine Constants
- Edit `src/config.h` for pin mappings, steps/mm, travel limits

---

## License
MIT License
