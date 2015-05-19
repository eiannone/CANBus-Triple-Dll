using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CanBusTriple
{
    public struct BusConfig
    {
        public int BitRate;
        public CanMode Mode;

        public override string ToString() 
        {
            return "\r\n\tMode: " + Enum.GetName(typeof(CanMode), Mode)
                + "\r\n\tBitRate: " + BitRate; 
        }

        public byte[] ToBytes()
        {
            var br = BitConverter.GetBytes(BitRate);
            return new byte[] { br[0], br[1], (byte)Mode, 0 };
        }
    }

    public class Pid
    {
        public int BusId;
        public byte Settings;
        public int Value;
        public byte[] TXD;
        public byte[] RXF;
        public byte[] RXD;
        public byte[] MTH;
        public string Name;

        public Pid()
        {
            BusId = 0;
            Settings = 0;
            Value = 0;
            TXD = new byte[8];
            RXF = new byte[6];
            RXD = new byte[2];
            MTH = new byte[6];
            Name = "";
        }

        public override string ToString() 
        {
            return "\r\n\tbusId: " + BusId
                + "\r\n\tsettings: 0b" + Convert.ToString(Settings, 2).PadLeft(8, '0')
                + "\r\n\tvalue: " + Value
                + "\r\n\ttxd: " + TXD.Aggregate("0x", (current, t) => current + string.Format("{0:X2}", t))
                + "\r\n\trxf: " + RXF.Aggregate("0x", (current, t) => current + string.Format("{0:X2}", t))
                + "\r\n\trxd: " + RXD.Aggregate("0x", (current, t) => current + string.Format("{0:X2}", t))
                + "\r\n\tmth: " + MTH.Aggregate("0x", (current, t) => current + string.Format("{0:X2}", t))
                + "\r\n\tname: " + Name;
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[34];
            bytes[0] = (byte)BusId;
            bytes[1] = Settings;
            Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, bytes, 2, 2);
            Buffer.BlockCopy(TXD, 0, bytes, 4, 8);
            Buffer.BlockCopy(RXF, 0, bytes, 12, 6);
            Buffer.BlockCopy(RXD, 0, bytes, 18, 2);
            Buffer.BlockCopy(MTH, 0, bytes, 20, 6);
            Buffer.BlockCopy(Encoding.Unicode.GetBytes(Name.PadRight(8)), 0, bytes, 26, 8);
            return bytes;
        }
    }

    public class CBTSettings
    {
        public const int EEPROM_SIZE = 512;
        public bool DisplayEnabled { get; set; }
        public bool FirstBoot { get; set; }
        public int DisplayIndex { get; set; }
        public bool HwSelfTest { get; set; }
        public BusConfig[] BusConfig { get; set; }
        public Pid[] Pid { get; set; }

        public CBTSettings()
        {
            DisplayEnabled = false;
            FirstBoot = true;
            DisplayIndex = 0;
            HwSelfTest = true;
            BusConfig = new BusConfig[3];
            for (int b = 0; b < BusConfig.Length; b++) {
                BusConfig[b].BitRate = 125;
                BusConfig[b].Mode = CanMode.Normal;
            }
            Pid = new Pid[8];
            for (int p = 0; p < Pid.Length; p++) Pid[p] = new Pid();
        }

        public CBTSettings(byte[] eeprom)
        {
            if (eeprom.Length != EEPROM_SIZE) 
                throw new Exception("Invalid EEPROM size (expected " + EEPROM_SIZE + "bytes)");

            DisplayEnabled = (eeprom[0] == 1);
            FirstBoot = (eeprom[1] == 1);
            DisplayIndex = eeprom[2];
            int pos = 3;

            BusConfig = new BusConfig[3];
            for (int b = 0; b < BusConfig.Length; b++) {
                BusConfig[b].BitRate = eeprom[pos] + (eeprom[pos + 1] << 8);
                BusConfig[b].Mode = (CanMode)eeprom[pos + 2];
                pos += 4;
            }

            HwSelfTest = (eeprom[pos++] == 1);
            pos += 4; // Skip unused placeholders

            Pid = new Pid[8];
            for (int p = 0; p < Pid.Length; p++) {
                Pid[p] = new Pid
                {
                    BusId = eeprom[pos],
                    Settings = eeprom[pos + 1],
                    Value = eeprom[pos + 2] + (eeprom[pos + 3] << 8)
                };
                Buffer.BlockCopy(eeprom, pos + 4, Pid[p].TXD, 0, 8);
                Buffer.BlockCopy(eeprom, pos + 12, Pid[p].RXF, 0, 6);
                Buffer.BlockCopy(eeprom, pos + 18, Pid[p].RXD, 0, 2);
                Buffer.BlockCopy(eeprom, pos + 20, Pid[p].MTH, 0, 6);
                var name = "";
                for (var c = 0; c < 8; c++) name += (char)eeprom[pos + 26 + c];
                Pid[p].Name = name.Trim();
                pos += 34;
            }
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[EEPROM_SIZE];
            bytes[0] = (byte) (DisplayEnabled ? 1 : 0);
            bytes[1] = (byte)(FirstBoot ? 1 : 0);
            bytes[2] = (byte)DisplayIndex;
            int pos = 3;

            for (int b = 0; b < 3; b++) {
                var bcBytes = BusConfig[b].ToBytes();
                Buffer.BlockCopy(bcBytes, 0, bytes, pos, bcBytes.Length);
                pos += bcBytes.Length;
            }

            bytes[pos++] = (byte)(HwSelfTest ? 1 : 0);

            // placeholders 4, 5, 6, 7
            for (int p = 0; p < 4; p++) bytes[pos++] = 0;

            for (int p = 0; p < 8; p++) {
                var pidBytes = Pid[p].ToBytes();
                Buffer.BlockCopy(pidBytes, 0, bytes, pos, pidBytes.Length);
                pos += pidBytes.Length;
            }

            // Zero padding
            while (pos < EEPROM_SIZE) bytes[pos++] = 0;

            return bytes;
        }

        public Dictionary<string, string> ToDictionary()
        {
            var msg = new Dictionary<string, string> {
                {"displayEnabled", DisplayEnabled ? "true" : "false"},
                {"firstboot", FirstBoot ? "true" : "false"},
                {"displayIndex", DisplayIndex.ToString()},
                {"hwselftest", HwSelfTest ? "true" : "false"}
            };
            for(var i = 0; i < BusConfig.Length; i++)
                msg.Add("Bus " + (i + 1) + " config", BusConfig[i].ToString());
            for (var i = 0; i < Pid.Length; i++)
                msg.Add("PID " + (i + 1), Pid[i].ToString());

            return msg;
        }
    }
}
