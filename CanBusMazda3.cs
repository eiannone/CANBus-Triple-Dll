using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CanBusTriple
{
    public class CanBusMazda3 : CBTController
    {
        const byte CMD_LCD = 0x16;

        public CanBusMazda3(string comPort) : base(comPort) { }

        public void DisplayMessage(string msg)
        {
            var cmd = new byte[1 + Math.Min(msg.Length, 65)];
            cmd[0] = CMD_LCD;
            var chars = msg.ToCharArray();
            for (int i = 1; i < cmd.Length; i++) cmd[i] = (byte)chars[i - 1];
            Serial.BlindCommand(cmd);
        }
    }
}
