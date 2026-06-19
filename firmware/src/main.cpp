#include <Arduino.h>
#include <SPI.h>
#include <MFRC522.h>
#include <string.h>

#define SS_PIN 10
#define RST_PIN 9

MFRC522 rfid(SS_PIN, RST_PIN);

void printHex(const byte* data, byte length) {
    for (byte i = 0; i < length; i++) {
        if (data[i] < 0x10) {
            Serial.print('0');
        }
        Serial.print(data[i], HEX);
    }
}

bool parseHex(const char* text, byte* destination, byte length) {
    if (strlen(text) != length * 2) {
        return false;
    }

    for (byte i = 0; i < length; i++) {
        char pair[3] = { text[i * 2], text[i * 2 + 1], '\0' };
        char* end;
        long value = strtol(pair, &end, 16);
        if (*end != '\0' || value < 0 || value > 255) {
            return false;
        }
        destination[i] = static_cast<byte>(value);
    }
    return true;
}

bool isProtectedBlock(int block) {
    return block == 0 || block % 4 == 3;
}

bool selectCard() {
    return rfid.PICC_IsNewCardPresent() && rfid.PICC_ReadCardSerial();
}

void finishCardSession() {
    rfid.PICC_HaltA();
    rfid.PCD_StopCrypto1();
}

bool authenticate(int block, const byte* keyBytes) {
    MFRC522::MIFARE_Key key;
    memcpy(key.keyByte, keyBytes, sizeof(key.keyByte));

    MFRC522::StatusCode status = rfid.PCD_Authenticate(
        MFRC522::PICC_CMD_MF_AUTH_KEY_A, block, &key, &(rfid.uid));

    if (status == MFRC522::STATUS_OK) {
        return true;
    }

    Serial.print(F("ERR AUTH "));
    Serial.println(rfid.GetStatusCodeName(status));
    finishCardSession();
    return false;
}

void readBlock(int block, const byte* key) {
    if (!selectCard()) {
        Serial.println(F("ERR NO_CARD"));
        return;
    }
    if (!authenticate(block, key)) {
        return;
    }

    byte data[18];
    byte length = sizeof(data);
    MFRC522::StatusCode status = rfid.MIFARE_Read(block, data, &length);
    if (status != MFRC522::STATUS_OK) {
        Serial.print(F("ERR READ "));
        Serial.println(rfid.GetStatusCodeName(status));
        finishCardSession();
        return;
    }

    Serial.print(F("OK READ "));
    printHex(rfid.uid.uidByte, rfid.uid.size);
    Serial.print(' ');
    Serial.print(block);
    Serial.print(' ');
    printHex(data, 16);
    Serial.println();
    finishCardSession();
}

void writeBlock(int block, const byte* key, byte* data) {
    if (!selectCard()) {
        Serial.println(F("ERR NO_CARD"));
        return;
    }
    if (!authenticate(block, key)) {
        return;
    }

    MFRC522::StatusCode status = rfid.MIFARE_Write(block, data, 16);
    if (status != MFRC522::STATUS_OK) {
        Serial.print(F("ERR WRITE "));
        Serial.println(rfid.GetStatusCodeName(status));
        finishCardSession();
        return;
    }

    Serial.print(F("OK WRITE "));
    printHex(rfid.uid.uidByte, rfid.uid.size);
    Serial.print(' ');
    Serial.println(block);
    finishCardSession();
}

void handleCommand(char* command) {
    char* action = strtok(command, " ");
    char* blockText = strtok(nullptr, " ");
    char* keyText = strtok(nullptr, " ");
    char* dataText = strtok(nullptr, " ");

    if (action == nullptr || blockText == nullptr || keyText == nullptr) {
        Serial.println(F("ERR FORMAT"));
        return;
    }

    char* end;
    long block = strtol(blockText, &end, 10);
    if (*end != '\0' || block < 0 || block > 63 || isProtectedBlock(block)) {
        Serial.println(F("ERR BLOCK"));
        return;
    }

    byte key[6];
    if (!parseHex(keyText, key, sizeof(key))) {
        Serial.println(F("ERR KEY"));
        return;
    }

    if (strcmp(action, "READ") == 0 && dataText == nullptr) {
        readBlock(static_cast<int>(block), key);
        return;
    }

    byte data[16];
    if (strcmp(action, "WRITE") == 0 && dataText != nullptr && parseHex(dataText, data, sizeof(data))) {
        writeBlock(static_cast<int>(block), key, data);
        return;
    }

    Serial.println(F("ERR FORMAT"));
}

void setup() {
    Serial.begin(115200);
    SPI.begin();
    rfid.PCD_Init();
    rfid.PCD_AntennaOn();
    rfid.PCD_SetAntennaGain(rfid.RxGain_max);
    Serial.println(F("READY"));
}

void loop() {
    if (!Serial.available()) {
        return;
    }

    char command[96];
    size_t length = Serial.readBytesUntil('\n', command, sizeof(command) - 1);
    command[length] = '\0';
    if (length > 0 && command[length - 1] == '\r') {
        command[length - 1] = '\0';
    }

    if (length > 0) {
        handleCommand(command);
    }
}
