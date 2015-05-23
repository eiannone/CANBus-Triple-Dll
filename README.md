![Logo](http://res.cloudinary.com/ddbgan4vk/image/upload/v1427295808/logo_py05gc.svg)

# CANBus Triple Dll - The car hacking platform

See [CANBus Triple](http://www.canb.us) for more information, or to purchase hardware.

## About
This repository contains a dll library to connect to a CANBus Triple Device via Serial Port. 

## Using library
Instantiate the **CBTController** class, passing serial port name, and then call methods, for example:

```C#
var cbt = new CBTController("COM1");
var info = await cbt.GetSystemInfo();
Console.Writeln("Device: " + info["name"]);
```

## Implemented methods
* *void* **Connect()**
* *async Task* **Disconnect()**
* *void* **SetComPort(**string portName**)**
* *async Task&lt;string&gt;* **SendCommand(**byte[] cmd**)**
* *async Task* **CancelCommand(**bool closePort**)**
* *async Task&lt;Dictionary&lt;string, string&gt;&gt;* **GetSystemInfo()**
* *async Task&lt;byte[]&gt;* **DumpEeprom()**
* *async Task* **SaveEeprom(**byte[] eeprom**)**
* *async Task* **SaveSettings(**CBTSettings settings**)**
* *async Task* **RestartBootloader()**
* *async Task&lt;Dictionary&lt;string, string&gt;&gt;* **ResetEeprom()**
* *async Task&lt;Dictionary&lt;string, string&gt;&gt;* **AutoBaudRate(**int bus**)**
* *async Task&lt;Dictionary&lt;string, string&gt;&gt;* **SetBaudRate(**int bus, int baud**)**
* *async Task&lt;Dictionary&lt;string, string&gt;&gt;* **GetBusStatus(**int bus**)**
* *async Task&lt;CBTSettings&gt;* **GetSettings()**
* *async Task&lt;Dictionary&lt;string, string&gt;&gt;* **ShowSettings()**
* *async Task&lt;CanMode&gt;* **GetCanMode(**int bus**)**
* *async Task* **SetCanMode(**int bus, CanMode mode**)**
* *async Task* **SendCanPacket(**int bus, byte[] msgId, byte[] data**)**
* *async Task&lt;bool&gt;* **DisableLog(**int bus**)**
* *async Task&lt;bool&gt;* **EnableLog(**int bus, int msgFilter1, int msgFilter2**)**
* *async Task&lt;bool&gt;* **EnableLogWithMask(**int bus, int msgFilter1, int mask1, int msgFilter2, int mask2**)**
* *async Task* **SetBluetoothMsgFilter(**int bus, bool enabled, int msgFilter1, int msgFilter2**)**
* *async Task* **ResetBluetooth()**
* *async Task* **EnableBluetoothPassthrough(**bool enabled**)**
* *async Task* **SleepTimerToggle(**bool activate**)**

## Firmware compatibility
**SetCanMode()**  method require at least firmware version 0.5.0, which you can find here: https://github.com/eiannone/CANBus-Triple/tree/main

**EnableLogWithMask()** method require at least firmware version 0.5.1, which you can find here: https://github.com/eiannone/CANBus-Triple/tree/can-mask
