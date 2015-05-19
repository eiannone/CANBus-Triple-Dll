![Logo](http://res.cloudinary.com/ddbgan4vk/image/upload/v1427295808/logo_py05gc.svg)

# CANBus Triple Dll - The car hacking platform

See [CANBus Triple](http://www.canb.us) for more information, or to purchase hardware.

## About
This repository contains a dll library to connect to a CANBus Triple Device via Serial Port. 

## Using library
Instantiate the **CBTController** class, passing serial port name, and then call methods, for example:

```C#
var cbt = new CBTController("COM1");
var info = cbt.GetSystemInfo();
```

## Implemented methods
* *void* **Connect()**
* *void* **Disconnect()**
* *void* **SetComPort(**string portName**)**
* *string* **SendCommand(**byte[] cmd**)**
* *void* **SendCommandAsync(**byte[] cmd, StringReceivedHandler msgReceivedHandler**)**
* *void* **CancelCommand(**bool closePort = false**)**
* *Dictionary<string, string>* **JsonCommand(**byte[] cmd**)**
* *Dictionary<string, string>* **GetSystemInfo()**
* *void* **GetSystemInfoAsync(**JsonReceivedHandler msgReceivedHandler**)**
* *byte[]* **DumpEeprom()**
* *void* **DumpEepromAsync(**BytesReceivedHandler msgReceivedHandler**)**
* *void* **SaveEeprom(**byte[] eeprom**)**
* *void* **SaveSettings(**CBTSettings settings**)**
* *void* **RestartBootloader()**
* *Dictionary<string, string>* **ResetEeprom()**
* *void* **ResetEepromAsync(**JsonReceivedHandler msgReceivedHandler**)**
* *Dictionary<string, string>* **AutoBaudRate(**int bus**)**
* *void* **AutoBaudRateAsync(**int bus, JsonReceivedHandler msgReceivedHandler**)**
* *Dictionary<string, string>* **SetBaudRate(**int bus, int baud**)**
* *void* **SetBaudRateAsync(**int bus, int baud, JsonReceivedHandler msgReceivedHandler**)**
* *Dictionary<string, string>* **GetBusStatus(**int bus**)**
* *void* **GetBusStatusAsync(**int bus, JsonReceivedHandler msgReceivedHandler**)**
* *CBTSettings* **GetSettings()**
* *Dictionary<string, string>* **ShowSettings()**
* *void* **ShowSettingsAsync(**JsonReceivedHandler msgReceivedHandler**)**
* *CanMode* **GetCanMode(**int bus**)**
* *void* **SetCanMode(**int bus, CanMode mode**)**
* *void* **SetCanModeAsync(**int bus, CanMode mode, JsonReceivedHandler msgReceivedHandler**)**
* *void* **SendCanPacket(**int bus, byte[] msgId, byte[] data**)**
* *void* **DisableLog(**int bus**)**
* *void* **EnableLog(**int bus, int msgFilter1 = 0, int msgFilter2 = 0**)**
* *void* **EnableLogWithMask(**int bus, int msgFilter1, int mask1, int msgFilter2 = 0, int mask2 = 0**)**
* *void* **SetBluetoothMsgFilter(**int bus, bool enabled, int msgFilter1 = 0, int msgFilter2 = 0**)**
* *void* **ResetBluetooth()**
* *void* **EnableBluetoothPassthrough(**bool enabled = true**)**
* *void* **SleepTimerToggle(**bool activate**)**
