using System;
using System.Collections.Generic;
using System.Threading;

namespace CanBusTriple
{
    public class CBTController
    {
        protected CBTSerialNew Serial;

        #region Properties
        public event CanMessageReceivedHandler CanMessageReceived {
            add { Serial.CanMessageReceived += value; }
            remove { Serial.CanMessageReceived -= value; }
        }

        public event PortStatusHandler PortStatusChanged {
            add { Serial.PortStatusChanged += value; }
            remove { Serial.PortStatusChanged -= value; }
        }

        public bool Connected {
            get { return Serial.IsOpen; }
        }

        public bool Busy { 
            get {  return Serial.Busy;  } 
        }

        public int CmdTimeout { get; set; }
        #endregion

        public CBTController(string comPort)
        {
            Serial = new CBTSerialNew(comPort);
            CmdTimeout = 4000;
        }

        #region Generic methods
        public void Connect()
        {
            Serial.OpenPort();
        }

        public void Disconnect()
        {
            Serial.EndCommand(true);
        }

        public void SetComPort(string portName)
        {
            Serial.SetPort(portName);
        }

        public string SendCommand(byte[] cmd)
        {
            string result = null;
            Serial.SendCommand(cmd, res => { result = res; });
            var timeoutMs = CmdTimeout;
            while (result == null && timeoutMs > 0)  {
                Thread.Sleep(50);
                timeoutMs -= 50;
            }
            if (result == null)
                throw new Exception("No response received (timeout).");

            return result;
        }

        public void SendCommandAsync(byte[] cmd, StringReceivedHandler msgReceivedHandler)
        {
            Serial.SendCommand(cmd, msgReceivedHandler);
        }

        public void CancelCommand(bool closePort = false)
        {
            Serial.EndCommand(closePort);
        }

        protected Dictionary<string, string> JsonCommand(byte[] cmd)
        {
            Dictionary<string, string> result = null;
            Serial.JsonCommand(cmd, res => { result = res; });
            var timeoutMs = CmdTimeout;
            while (result == null && timeoutMs > 0) {
                Thread.Sleep(50);
                timeoutMs -= 50;
            }
            if (result == null)
                throw new Exception("No response received (timeout).");

            return result;
        }
        #endregion

        #region System commands
        public Dictionary<string, string> GetSystemInfo()
        {
            return JsonCommand(CBTCommand.SystemInfo);
        }

        public void GetSystemInfoAsync(JsonReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.SystemInfo, msgReceivedHandler);
        }

        public byte[] DumpEeprom()
        {
            var res = JsonCommand(CBTCommand.DumpEeprom);
            return res.ContainsKey("data") ? HexToBytes(res["data"]) : new byte[0];
        }

        public void DumpEepromAsync(BytesReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.DumpEeprom, result =>  {
                msgReceivedHandler(result.ContainsKey("data")? HexToBytes(result["data"]) : new byte[0]);
            });
        }

        public void SaveEeprom(byte[] eeprom)
        {
            var chunk = new byte[32];
            int chunks = eeprom.Length / chunk.Length;
            int offset = 0;
            bool wasOpen = false; 
            try {
                wasOpen = Serial.IsOpen;
                Serial.OpenPort();
                for (var chunkN = 0; chunkN < chunks; chunkN++) {
                    Buffer.BlockCopy(eeprom, offset, chunk, 0, chunk.Length);
                    var res = JsonCommand(CBTCommand.SaveEeprom(chunkN, chunk));
                    if (!res.ContainsKey("result") || res["result"] != "success")
                        throw new Exception("Error saving EEPROM.");
                    offset += chunk.Length;
                }
            }
            finally {
                if (Serial.IsOpen && !wasOpen) Serial.ClosePort();
            }
        }

        public void SaveSettings(CBTSettings settings)
        {
            SaveEeprom(settings.ToBytes());
        }

        public void RestartBootloader()
        {
            Serial.BlindCommand(CBTCommand.RestartBootloader);
            Serial.ClosePort();
        }

        public Dictionary<string, string> ResetEeprom()
        {
            return JsonCommand(CBTCommand.ResetEeprom);
        }

        public void ResetEepromAsync(JsonReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.ResetEeprom, msgReceivedHandler);
        }

        public Dictionary<string, string> AutoBaudRate(int bus)
        {
            return JsonCommand(CBTCommand.AutoBaudRate(bus));
        }

        public void AutoBaudRateAsync(int bus, JsonReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.AutoBaudRate(bus), msgReceivedHandler);
        }

        public Dictionary<string, string> SetBaudRate(int bus, int baud)
        {
            return JsonCommand(CBTCommand.BitRate(bus, baud));
        }

        public void SetBaudRateAsync(int bus, int baud, JsonReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.BitRate(bus, baud), msgReceivedHandler);
        }

        public Dictionary<string, string> GetBusStatus(int bus)
        {
            return JsonCommand(CBTCommand.BusStatus(bus));
        }

        public void GetBusStatusAsync(int bus, JsonReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.BusStatus(bus), msgReceivedHandler);
        }

        public CBTSettings GetSettings()
        {
            return new CBTSettings(DumpEeprom());
        }

        public Dictionary<string, string> ShowSettings()
        {
            return GetSettings().ToDictionary();
        }

        public void ShowSettingsAsync(JsonReceivedHandler msgReceivedHandler)
        {
            DumpEepromAsync(eeprom => {
                var settings = new CBTSettings(eeprom);
                msgReceivedHandler(settings.ToDictionary()); 
            });
        }

        public CanMode GetCanMode(int bus)
        {
            var res = JsonCommand(CBTCommand.GetCanMode(bus));
            return res.ContainsKey("mode")? (CanMode)Enum.Parse(typeof(CanMode), res["mode"], true) : CanMode.Unknown;
        }

        public void SetCanMode(int bus, CanMode mode)
        {
            var res = JsonCommand(CBTCommand.SetCanMode(bus, mode));
            if (!res.ContainsKey("mode") || res["mode"] != Enum.GetName(typeof(CanMode), mode))
                throw new Exception("Error changing CAN mode.");
        }

        public void SetCanModeAsync(int bus, CanMode mode, JsonReceivedHandler msgReceivedHandler)
        {
            Serial.JsonCommand(CBTCommand.SetCanMode(bus, mode), msgReceivedHandler);
        }
        #endregion

        #region Can commands
        public void SendCanPacket(int bus, byte[] msgId, byte[] data)
        {
            Serial.BlindCommand(CBTCommand.CanPacket(bus, msgId, data));
        }
        #endregion

        #region Log/filter commands
        private void OkCommand(byte[] cmd)
        {
            var finished = false;
            Serial.OkCommand(cmd, res => { finished = res; });
            // Wait for response
            var timeoutMs = CmdTimeout;
            while (!finished && timeoutMs > 0) {
                Thread.Sleep(50);
                timeoutMs -= 50;
            }
            if (!finished)
                throw new Exception("Command failed" + ((timeoutMs <= 0) ? " (command timeout)" : ""));
        }

        public void DisableLog(int bus)
        {
            OkCommand(CBTCommand.BusLog(bus, false));
        }

        public void EnableLog(int bus, int msgFilter1 = 0, int msgFilter2 = 0)
        {
            OkCommand(CBTCommand.BusLog(bus, true, msgFilter1, msgFilter2));
        }

        public void EnableLogWithMask(int bus, int msgFilter1, int mask1, int msgFilter2 = 0, int mask2 = 0)
        {
            OkCommand(CBTCommand.BusLogMask(bus, msgFilter1, mask1, msgFilter2, mask2));
        }
        #endregion

        #region Bluetooth commands
        public void SetBluetoothMsgFilter(int bus, bool enabled, int msgFilter1 = 0, int msgFilter2 = 0)
        {
            Serial.BlindCommand(CBTCommand.BluettothFilter(bus, enabled, msgFilter1, msgFilter2));
        }

        public void ResetBluetooth()
        {
            Serial.BlindCommand(CBTCommand.BluetoothReset);
        }

        public void EnableBluetoothPassthrough(bool enabled = true)
        {
            Serial.BlindCommand(CBTCommand.BluetoothPassthrough(enabled));
        }
        #endregion

        #region Sleep commands
        public void SleepTimerToggle(bool activate)
        {
            Serial.BlindCommand(CBTCommand.SleepTimer(activate));
        }
        #endregion

        private static byte[] HexToBytes(string hexData)
        {
            var bytesOut = new byte[hexData.Length / 2];
            var i = 0;
            for (var b = 0; b < hexData.Length; b += 2) {
                var hexStr = "" + hexData[b] + hexData[b + 1];
                bytesOut[i++] = Convert.ToByte(hexStr, 16);
            }
            return bytesOut;
        }
    }
}
