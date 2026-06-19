# RFID RC522 Reader

Windows WPF client and PlatformIO firmware for an RC522 reader connected to an Arduino over USB Serial.

## Project layout

- `RFIDReader.slnx`: open this solution in Visual Studio for the Windows client.
- `firmware`: open this folder in PlatformIO for the Arduino firmware.

The current PlatformIO profile targets an Arduino Nano with the new bootloader, because that is the configuration that was supplied and compiled. To use an Arduino Uno, change `board = nanoatmega328new` to `board = uno` in `firmware/platformio.ini`.

## Safety

- Use only MIFARE Classic cards that you own or are authorized to manage.
- Block 0 and every sector trailer block (3, 7, 11, ...) are protected in both the application and firmware.
- A block holds exactly 16 bytes. Enter data as 32 hexadecimal characters.

## Use

1. Open `firmware` in PlatformIO and upload it to the Arduino.
2. Open `RFIDReader.slnx` in Visual Studio and start the application.
3. Select the Arduino COM port and connect at 115200 baud.
4. Keep the card on the reader while reading or writing a block.
