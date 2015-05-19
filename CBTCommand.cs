using System;

namespace CanBusTriple
{
    public enum CanMode : byte { Configuration, Normal, Sleep, Listen, Loopback, Unknown };

    internal class CBTCommand
    {
        const int MAX_BUS = 3;
        protected const byte CMD_SYSTEM = 0x01;
        protected const byte CMD_SEND_CAN = 0x02;
        protected const byte CMD_LOG = 0x03;
        protected const byte CMD_BT_FILTER = 0x04;
        protected const byte CMD_BLUETOOTH = 0x08;

        protected const byte CMD_SLEEP = 0x4E; // Naptime

        #region System commands
        public static byte[] SystemInfo {
            get { return new byte[] { CMD_SYSTEM, 0x01 }; }
        }

        public static byte[] DumpEeprom {
            get { return new byte[] { CMD_SYSTEM, 0x02 }; }
        }

        public static byte[] SaveEeprom(int chunkN, byte[] chunk) {
            if (chunk.Length != 32) throw new Exception("Invalid chunk size (32 bytes expected)");
            var cmd = new byte[36];
            cmd[0] = CMD_SYSTEM;
            cmd[1] = 0x03; // "getAndSaveEeprom" command
            cmd[2] = (byte)chunkN;
            Buffer.BlockCopy(chunk, 0, cmd, 3, chunk.Length);
            cmd[35] = 0xA1;
            return cmd;
        }

        public static byte[] ResetEeprom {
            get { return new byte[] { CMD_SYSTEM, 0x04 }; }
        }

        public static byte[] AutoBaudRate(int bus) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            return new byte[] { CMD_SYSTEM, 0x08, (byte)bus };
        }

        public static byte[] BitRate(int bus, int baud) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            if (baud < 1 || baud > 65535) throw new Exception("Invalid baud rate");
            return new byte[] { CMD_SYSTEM, 0x09, (byte)bus, (byte)(baud >> 8), (byte)(baud & 0x00FF) };
        }

        public static byte[] GetCanMode(int bus) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            return new byte[] { CMD_SYSTEM, 0x0A, (byte)bus };
        }

        public static byte[] SetCanMode(int bus, CanMode mode) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            return new byte[] { CMD_SYSTEM, 0x0A, (byte)bus, (byte)mode };
        }

        public static byte[] BusStatus(int bus) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            return new byte[] { CMD_SYSTEM, 0x10, (byte)bus };
        }

        public static byte[] RestartBootloader {
            get { return new byte[] { CMD_SYSTEM, 0x16 }; }
        }
        #endregion

        #region Can commands
        public static byte[] CanPacket(int bus, byte[] msgId, byte[] data) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            if (msgId.Length > 2 || msgId.Length == 0) throw new Exception("Invalid can identifier");
            if (data.Length > 8) throw new Exception("Invalid packed data");
            byte[] cmd = { 
                 CMD_SEND_CAN, 
                 (byte)bus, 
                 (msgId.Length > 1) ? msgId[0] : (byte)0, 
                 (msgId.Length > 1) ? msgId[1] : msgId[0],
                 0, 0, 0, 0, 0, 0, 0, 0, 
                 (byte)data.Length
            };
            for (int i = 0; i < data.Length; i++) cmd[4 + i] = data[i];
            return cmd;
        }
        #endregion

        #region Log commands
        public static byte[] BusLog(int bus, bool enabled, int msgFilter1 = 0, int msgFilter2 = 0) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            if (msgFilter1 > 65535 || msgFilter2 > 65535) throw new Exception("Invalid filters");
            byte[] cmd = new byte[(enabled && msgFilter1 > 0) ? 7 : 3];
            cmd[0] = CMD_LOG;
            cmd[1] = (byte)bus;
            cmd[2] = (byte)(enabled ? 1 : 0);
            if (enabled && msgFilter1 > 0) {
                var b1 = BitConverter.GetBytes(msgFilter1);
                cmd[3] = b1[1];
                cmd[4] = b1[0];
                var b2 = BitConverter.GetBytes(msgFilter2);
                cmd[5] = b2[1];
                cmd[6] = b2[0];
            }
            return cmd;
        }

        public static byte[] BusLogMask(int bus, int msgFilter1, int mask1, int msgFilter2 = 0, int mask2 = 0)
        {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            if (msgFilter1 > 65535 || msgFilter2 > 65535) throw new Exception("Invalid filters");
            if (mask1 > 65535 || mask2 > 65535) throw new Exception("Invalid masks");
            byte[] cmd = new byte[11];
            cmd[0] = CMD_LOG;
            cmd[1] = (byte)bus;
            cmd[2] = 2;

            var f1 = BitConverter.GetBytes(msgFilter1);
            var m1 = BitConverter.GetBytes(mask1);
            cmd[3] = f1[1];
            cmd[4] = f1[0];
            cmd[5] = m1[1];
            cmd[6] = m1[0];

            var f2 = (msgFilter1 > 0)? BitConverter.GetBytes(msgFilter1) : f1;
            var m2 = (mask2 > 0)? BitConverter.GetBytes(mask1) : m1;
            cmd[7] = f2[1];
            cmd[8] = f2[0];
            cmd[9] = m2[1];
            cmd[10] = m2[0];
            
            return cmd;
        }
        #endregion

        #region Bluetooth commands
        public static byte[] BluettothFilter(int bus, bool enabled, int msgFilter1 = 0, int msgFilter2 = 0) {
            if (bus < 1 || bus > MAX_BUS) throw new Exception("Invalid Bus");
            if (msgFilter1 > 65535 || msgFilter2 > 65535 || (enabled && msgFilter1 <= 0 && msgFilter2 <= 0))
                throw new Exception("Invalid filters");
            byte[] cmd = { CMD_BT_FILTER, (byte)bus, 0, 0, 0, 0 };
            if (enabled) {
                cmd[2] = (byte)((msgFilter1 > 0) ? msgFilter1 >> 8 : 0);
                cmd[3] = (byte)((msgFilter1 > 0) ? msgFilter1 & 0x00FF : 0);
                cmd[4] = (byte)((msgFilter2 > 0) ? msgFilter2 >> 8 : 0);
                cmd[5] = (byte)((msgFilter2 > 0) ? msgFilter2 & 0x00FF : 0);
            }
            return cmd;
        }

        public static byte[] BluetoothReset {
            get { return new byte[] { CMD_BLUETOOTH, 0x01 }; }
        }

        public static byte[] BluetoothPassthrough(bool enabled) {
            return new[] { CMD_BLUETOOTH, (byte)(enabled ? 0x02 : 0x03) };
        }
        #endregion

        #region Sleep commands
        public static byte[] SleepTimer(bool enabled) {
            return new[] { CMD_SLEEP, (byte)(enabled ? 0x01 : 0x00) };
        }
        #endregion
    }
}
