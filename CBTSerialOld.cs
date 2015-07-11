using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace CanBusTriple
{
    enum CommandType { Json, Ok, Line, None, All }
    
    public delegate void StringReceivedHandler(string line);
    public delegate void JsonReceivedHandler(Dictionary<string, string> json);
    public delegate void OkReceivedHandler(bool result);
    public delegate void BytesReceivedHandler(byte[] bytes);

    public class CBTSerialOld
    {
        public const int BAUD_RATE = 115200;
        public event CanMessageReceivedHandler CanMessageReceived;
        public event PortStatusHandler PortStatusChanged;
        private event StringReceivedHandler LineReceived;
        private event JsonReceivedHandler JsonReceived;
        private event OkReceivedHandler OkReceived;

        private readonly SerialPort _port;

        private readonly byte[] _canBuf = new byte[15];
        private bool _receivingCanMsg;        
        private int _canRead;
        private DateTime _startTime;
        private Stopwatch _stopwatch;

        private JsonReceivedHandler _jsonHandler;
        private OkReceivedHandler _okHandler;
        private StringReceivedHandler _stringHandler;
        private CommandType _currentCmd = CommandType.None;

        public bool Busy => _currentCmd != CommandType.None;

        public bool IsOpen => _port.IsOpen;

        internal CBTSerialOld(string portName)
        {
            _port = new SerialPort(portName, BAUD_RATE, Parity.None, 8, StopBits.One) 
                        { DtrEnable = true, RtsEnable = true, NewLine = "\r\n" };
            _port.DataReceived += OnDataReceived;
        }

        public void ClosePort()
        {
            if (!_port.IsOpen) return;
            _stopwatch.Stop();
            _port.Close();
            PortStatusChanged?.Invoke(false);
        }

        public void OpenPort()
        {
            if (_port.IsOpen) return;
            _port.Open();
            _startTime = DateTime.Now;
            _stopwatch = Stopwatch.StartNew();
            PortStatusChanged?.Invoke(true);
        }

        public void SetPort(string portName)
        {
            if (Busy) throw new Exception("Port busy. Cannot set new name.");
            if (_port.IsOpen) ClosePort();
            _port.PortName = portName;
        }

        public void BlindCommand(byte[] cmd)
        {
            var wasOpen = _port.IsOpen;
            try {
                if (!wasOpen) OpenPort();
                _port.Write(cmd, 0, cmd.Length);
                // Waits for writing completed
                while (_port.BytesToWrite > 0) Thread.Sleep(50);
            }
            finally {
                if (!wasOpen) ClosePort();
            }
        }

        public void JsonCommand(byte[] cmd, JsonReceivedHandler msgReceivedHandler)
        {
            if (Busy) throw new Exception("Port busy. Cannot send command.");
            _currentCmd = CommandType.Json;
            var wasOpen = _port.IsOpen;
            _jsonHandler = json => {
                EndCommand(!wasOpen);
                msgReceivedHandler(json);
            };
            try {
                if (!wasOpen) OpenPort();
                JsonReceived += _jsonHandler;
                _port.Write(cmd, 0, cmd.Length);
            }
            catch (Exception) {
                EndCommand(!wasOpen);
                throw;
            }
        }

        public void OkCommand(byte[] cmd, OkReceivedHandler msgReceivedHandler)
        {
            if (Busy) throw new Exception("Port busy. Cannot send command.");
            _currentCmd = CommandType.Ok;
            var wasOpen = _port.IsOpen;
            _okHandler = result => {
                msgReceivedHandler(result);
                EndCommand(!wasOpen);
            };
            try {
                if (!wasOpen) OpenPort();
                OkReceived += _okHandler;
                _port.Write(cmd, 0, cmd.Length);
            }
            catch (Exception) {
                EndCommand(!wasOpen);
                throw;
            }
        }

        public void SendCommand(byte[] cmd, StringReceivedHandler msgReceivedHandler)
        {
            if (Busy) throw new Exception("Port busy. Cannot send command.");
            _currentCmd = CommandType.All;
            var wasOpen = _port.IsOpen;
            _jsonHandler = json => {
                var str = json.Aggregate("", (c, el) => c + ("[" + el.Key + "] => " + el.Value + "\r\n"));
                msgReceivedHandler(str);
            };
            _okHandler = result => { msgReceivedHandler("OK"); };
            _stringHandler = msgReceivedHandler;
            try {
                if (!wasOpen) OpenPort();
                JsonReceived += _jsonHandler;
                OkReceived += _okHandler;
                LineReceived += _stringHandler;
                _port.Write(cmd, 0, cmd.Length);
            }
            catch (Exception) {
                EndCommand(!wasOpen);
                throw;
            }
        }

        public void EndCommand(bool closePort)
        {
            switch (_currentCmd) {
                case CommandType.Json:
                    JsonReceived -= _jsonHandler;
                    break;

                case CommandType.Ok:
                    OkReceived -= _okHandler;
                    break;

                case CommandType.All:
                    JsonReceived -= _jsonHandler;
                    OkReceived -= _okHandler;
                    LineReceived -= _stringHandler;
                    break;
            }
            if (closePort) ClosePort();
            _currentCmd = CommandType.None;
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            while (_port.BytesToRead > 0) {
                if (_receivingCanMsg) {
                    ReadCanMessage();
                }
                else {
                    // Reads a byte and detect packet type
                    var b = (byte)_port.ReadByte();
                    switch (b) {
                        case 0x03: // CAN message
                            ReadCanMessage();
                            break;

                        case 0x7B: // Json message ('{')
                            ReadJsonString();
                            break;

                        case 0xFF: // OK message (COMMAND_OK)
                            // Discard two next bytes (line terminator)
                            _port.ReadByte();
                            _port.ReadByte();
                            OkReceived?.Invoke(true);
                            break;

                        case 0x80: // Error message (COMMAND_ERROR)
                            OkReceived?.Invoke(false);
                            break;

                        default: // Reads a line
                            var line = ((char)b) + _port.ReadLine();
                            LineReceived?.Invoke(line);
                            break;
                    }
                }
            }
        }

        private void ReadCanMessage()
        {
            var bytesToRead = _canBuf.Length - _canRead;
            var bytesRead = _port.Read(_canBuf, _canRead, Math.Min(bytesToRead, _port.BytesToRead));
            if (bytesRead == bytesToRead) {
                // Read completed
                _canRead = 0;
                _receivingCanMsg = false;
                if (CanMessageReceived != null) {
                    var msg = new CanMessage {
                        Bus = _canBuf[0],
                        Id = (_canBuf[1] << 8) + _canBuf[2],
                        Status = _canBuf[12],
                        Data = new byte[_canBuf[11]],
                        DateTime = _startTime + _stopwatch.Elapsed
                    };
                    Buffer.BlockCopy(_canBuf, 3, msg.Data, 0, msg.Data.Length);
                    CanMessageReceived(msg);
                }
            }
            else {
                // Partial read
                _canRead += bytesRead;
                _receivingCanMsg = true;
            }
            if (_port.IsOpen && _port.BytesToRead > 0) OnDataReceived(this, null);
        }

        private void ReadJsonString()
        {
            var str = _port.ReadTo("}\r\n");
            if (JsonReceived != null)  {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>("{" + str + "}");
                JsonReceived(json);
            }
        }
    }
}
