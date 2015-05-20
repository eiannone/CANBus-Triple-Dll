using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace CanBusTriple
{
    public class CBTController
    {
        protected CBTSerial Serial;

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
        #endregion

        public CBTController(string comPort)
        {
            Serial = new CBTSerial(comPort);
        }

        #region Generic methods
        public void Connect()
        {
            Serial.OpenPort();
        }

        public async Task Disconnect()
        {
            await Serial.EndCommand(true);
        }

        public void SetComPort(string portName)
        {
            Serial.SetPort(portName);
        }

        public async Task<string> SendCommand(byte[] cmd)
        {
            return await Serial.SendCommand(cmd);
        }

        public async Task CancelCommand(bool closePort = false)
        {
            await Serial.EndCommand(closePort);
        }
        #endregion

        #region System commands
        public async Task<Dictionary<string, string>> GetSystemInfo()
        {
            return await Serial.JsonCommand(CBTCommand.SystemInfo);
        }

        public async Task<byte[]> DumpEeprom()
        {
            var res = await Serial.JsonCommand(CBTCommand.DumpEeprom);
            return res.ContainsKey("data") ? HexToBytes(res["data"]) : new byte[0];
        }

        public async Task SaveEeprom(byte[] eeprom)
        {
            var chunk = new byte[32];
            var chunks = eeprom.Length / chunk.Length;
            var offset = 0;
            var wasOpen = false;
            ExceptionDispatchInfo capturedException = null;
            try {
                wasOpen = Serial.IsOpen;
                Serial.OpenPort();
                for (var chunkN = 0; chunkN < chunks; chunkN++) {
                    Buffer.BlockCopy(eeprom, offset, chunk, 0, chunk.Length);
                    var res = await Serial.JsonCommand(CBTCommand.SaveEeprom(chunkN, chunk));
                    if (!res.ContainsKey("result") || res["result"] != "success")
                        throw new Exception("Error saving EEPROM.");
                    offset += chunk.Length;
                }
            }
            catch (Exception ex) {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            if (Serial.IsOpen && !wasOpen) await Serial.ClosePort();
            if (capturedException != null) capturedException.Throw();
        }

        public async Task SaveSettings(CBTSettings settings)
        {
            await SaveEeprom(settings.ToBytes());
        }

        public async Task RestartBootloader()
        {
            await Serial.BlindCommand(CBTCommand.RestartBootloader);
            await Serial.ClosePort();
        }

        public async Task<Dictionary<string, string>> ResetEeprom()
        {
            return await Serial.JsonCommand(CBTCommand.ResetEeprom);
        }

        public async Task<Dictionary<string, string>> AutoBaudRate(int bus)
        {
            return await Serial.JsonCommand(CBTCommand.AutoBaudRate(bus));
        }

        public async Task<Dictionary<string, string>> SetBaudRate(int bus, int baud)
        {
            return await Serial.JsonCommand(CBTCommand.BitRate(bus, baud));
        }

        public async Task<Dictionary<string, string>> GetBusStatus(int bus)
        {
            return await Serial.JsonCommand(CBTCommand.BusStatus(bus));
        }

        public async Task<CBTSettings> GetSettings()
        {
            var bytes = await DumpEeprom();
            return new CBTSettings(bytes);
        }

        public async Task<Dictionary<string, string>> ShowSettings()
        {
            var settings = await GetSettings();
            return settings.ToDictionary();
        }

        public async Task<CanMode> GetCanMode(int bus)
        {
            var res = await Serial.JsonCommand(CBTCommand.GetCanMode(bus));
            return res.ContainsKey("mode")? (CanMode)Enum.Parse(typeof(CanMode), res["mode"], true) : CanMode.Unknown;
        }

        public async Task SetCanMode(int bus, CanMode mode)
        {
            var res = await Serial.JsonCommand(CBTCommand.SetCanMode(bus, mode));
            if (!res.ContainsKey("mode") || res["mode"] != Enum.GetName(typeof(CanMode), mode))
                throw new Exception("Error changing CAN mode.");
        }
        #endregion

        #region Can commands
        public async Task SendCanPacket(int bus, byte[] msgId, byte[] data)
        {
            await Serial.BlindCommand(CBTCommand.CanPacket(bus, msgId, data));
        }
        #endregion

        #region Log/filter commands
        public async Task<bool> DisableLog(int bus)
        {
            return await Serial.OkCommand(CBTCommand.BusLog(bus, false));
        }

        public async Task<bool> EnableLog(int bus, int msgFilter1 = 0, int msgFilter2 = 0)
        {
            return await Serial.OkCommand(CBTCommand.BusLog(bus, true, msgFilter1, msgFilter2));
        }

        public async Task<bool> EnableLogWithMask(int bus, int msgFilter1, int mask1, int msgFilter2 = 0, int mask2 = 0)
        {
            return await Serial.OkCommand(CBTCommand.BusLogMask(bus, msgFilter1, mask1, msgFilter2, mask2));
        }
        #endregion

        #region Bluetooth commands
        public async Task SetBluetoothMsgFilter(int bus, bool enabled, int msgFilter1 = 0, int msgFilter2 = 0)
        {
            await Serial.BlindCommand(CBTCommand.BluettothFilter(bus, enabled, msgFilter1, msgFilter2));
        }

        public async Task ResetBluetooth()
        {
            await Serial.BlindCommand(CBTCommand.BluetoothReset);
        }

        public async Task EnableBluetoothPassthrough(bool enabled = true)
        {
            await Serial.BlindCommand(CBTCommand.BluetoothPassthrough(enabled));
        }
        #endregion

        #region Sleep commands
        public async Task SleepTimerToggle(bool activate)
        {
            await Serial.BlindCommand(CBTCommand.SleepTimer(activate));
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
