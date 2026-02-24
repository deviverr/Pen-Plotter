#include "sd_card.h"

SDCardManager sdCard;

bool SDCardManager::init() {
    if (!isPresent()) {
        _initialized = false;
        return false;
    }

    _initialized = _sd.begin(SDSS, SPI_HALF_SPEED);
    return _initialized;
}

bool SDCardManager::isPresent() {
    return (digitalRead(SD_DETECT_PIN) == LOW);
}

int SDCardManager::listGCodeFiles(char fileList[][SD_MAX_FILENAME], int maxFiles) {
    if (!_initialized) return 0;

    SdFile root;
    if (!root.open("/")) return 0;

    int count = 0;
    SdFile entry;
    while (entry.openNext(&root, O_RDONLY) && count < maxFiles) {
        if (!entry.isDir()) {
            char name[SD_MAX_FILENAME];
            entry.getName(name, SD_MAX_FILENAME);

            // Check for .gcode or .gc extension
            char* dot = strrchr(name, '.');
            if (dot && (strcasecmp(dot, ".gcode") == 0 ||
                        strcasecmp(dot, ".gc") == 0 ||
                        strcasecmp(dot, ".g") == 0)) {
                strncpy(fileList[count], name, SD_MAX_FILENAME - 1);
                fileList[count][SD_MAX_FILENAME - 1] = '\0';
                count++;
            }
        }
        entry.close();
    }
    root.close();
    return count;
}

bool SDCardManager::openFile(const char* filename) {
    if (!_initialized) return false;
    if (_fileOpen) closeFile();

    if (!_file.open(filename, O_RDONLY)) {
        return false;
    }

    _fileSize = _file.fileSize();
    _filePos = 0;
    _fileOpen = true;
    return true;
}

bool SDCardManager::readLine(char* buffer, int bufSize) {
    if (!_fileOpen) return false;

    int idx = 0;
    while (idx < bufSize - 1) {
        int c = _file.read();
        if (c < 0) {
            // EOF
            if (idx == 0) return false; // No data read
            break;
        }
        _filePos++;
        if (c == '\n') break;
        if (c == '\r') continue; // Skip CR
        buffer[idx++] = (char)c;
    }
    buffer[idx] = '\0';
    return true;
}

void SDCardManager::closeFile() {
    if (_fileOpen) {
        _file.close();
        _fileOpen = false;
        _fileSize = 0;
        _filePos = 0;
    }
}

uint8_t SDCardManager::progressPercent() const {
    if (_fileSize == 0) return 0;
    return (uint8_t)((unsigned long long)_filePos * 100ULL / _fileSize);
}
