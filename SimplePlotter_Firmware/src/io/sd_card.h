#ifndef SD_CARD_H
#define SD_CARD_H

#include <Arduino.h>
#include <SdFat.h>
#include "../config.h"

#define SD_MAX_FILES 20
#define SD_MAX_FILENAME 13 // 8.3 format + null

class SDCardManager {
public:
    bool init();
    bool isPresent();
    bool isInitialized() const { return _initialized; }

    // File listing
    int listGCodeFiles(char fileList[][SD_MAX_FILENAME], int maxFiles);

    // File execution
    bool openFile(const char* filename);
    bool readLine(char* buffer, int bufSize);
    void closeFile();
    bool isFileOpen() const { return _fileOpen; }

    // Progress tracking
    unsigned long fileSize() const { return _fileSize; }
    unsigned long filePosition() const { return _filePos; }
    uint8_t progressPercent() const;

private:
    SdFat _sd;
    SdFile _file;
    bool _initialized = false;
    bool _fileOpen = false;
    unsigned long _fileSize = 0;
    unsigned long _filePos = 0;
};

extern SDCardManager sdCard;

#endif // SD_CARD_H
