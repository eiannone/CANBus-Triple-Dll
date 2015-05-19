using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CanBusTriple
{
    public class CBTSerialNew
    {
        public const int BAUD_RATE = 115200;
        public const int READ_TIMEOUT = 3000;
        private readonly SerialPort _port;
        private BackgroundWorker _bgWorker;
        private CancellationTokenSource _cancelRead;
        private CommandType _currentCmd = CommandType.None;

        private readonly byte[] _canBuf = new byte[15];
        private int _canRead;
        private DateTime _startTime;
        private Stopwatch _stopwatch;

        public event PortStatusHandler PortStatusChanged;
        public event CanMessageReceivedHandler CanMessageReceived;

        private event JsonReceivedHandler JsonReceived;
        private event OkReceivedHandler OkReceived;
        private event StringReceivedHandler LineReceived;

        private JsonReceivedHandler _jsonHandler;
        private OkReceivedHandler _okHandler;
        private StringReceivedHandler _stringHandler;

        public bool Busy
        {
            get { return _currentCmd != CommandType.None; }
        }

        public bool IsOpen
        {
            get { return _port.IsOpen; }
        }

        internal CBTSerialNew(string portName)
        {
            _port = new SerialPort(portName, BAUD_RATE, Parity.None, 8, StopBits.One) {
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n",
                ReadTimeout = READ_TIMEOUT
            };
        }

        public void OpenPort()
        {
            if (_port.IsOpen) return;
            _port.Open();
            _cancelRead = new CancellationTokenSource();
            _bgWorker = new BackgroundWorker {WorkerSupportsCancellation = true};
            _bgWorker.DoWork += async (sender, e) => { 
                while(!_bgWorker.CancellationPending) await DetectPacket();
                e.Cancel = true;
            };
            _startTime = DateTime.Now;
            _stopwatch = Stopwatch.StartNew();
            _bgWorker.RunWorkerAsync();
            if (PortStatusChanged != null) PortStatusChanged(true);
        }

        public void ClosePort()
        {
            if (!_port.IsOpen) return;
            _bgWorker.CancelAsync();
            if (!_cancelRead.IsCancellationRequested) _cancelRead.Cancel();
            var timeout = READ_TIMEOUT;
            while (_bgWorker.IsBusy && timeout > 0) {
                Thread.Sleep(100);
                timeout -= 100;
            }            
            _stopwatch.Stop();
            _port.Close();
            if (PortStatusChanged != null) PortStatusChanged(false);
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
                var str = json.Aggregate("", (cur, el) => cur + ("[" + el.Key + "] => " + el.Value + "\r\n"));
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
            if (!_cancelRead.IsCancellationRequested) _cancelRead.Cancel();
            if (closePort) ClosePort();
            _currentCmd = CommandType.None;
        }

        async private Task DetectPacket()
        {
            if (_cancelRead.IsCancellationRequested) {
                _cancelRead = new CancellationTokenSource();
                return;
            }
            var buf = new byte[10];
            try {
                var bytesRead = await _port.BaseStream.ReadAsync(buf, 0, 1, _cancelRead.Token);                
                if (bytesRead == 0) return;
            }
            catch (TaskCanceledException) {
                _cancelRead = new CancellationTokenSource();
                return;
            }
            catch (Exception ex) {
                if (ex is IOException || ex is TimeoutException) return;
                throw;
            }

            switch (buf[0]) {
                case 0x03: // CAN message
                    await ReadCanMessage(_startTime + _stopwatch.Elapsed);
                    break;

                case 0x7B: // Json message ('{')
                    var str = _port.ReadTo("}\r\n");
                    if (JsonReceived != null) {
                        var json = JsonConvert.DeserializeObject<Dictionary<string, string>>("{" + str + "}");
                        JsonReceived(json);
                    }
                    break;

                case 0xFF: // OK message (COMMAND_OK)
                    // Discard two next bytes (line terminator)
                    var buff = new byte[2];
                    await _port.BaseStream.ReadAsync(buff, 0, 2, _cancelRead.Token);
                    if (OkReceived != null) OkReceived(true);
                    break;

                case 0x80: // Error message (COMMAND_ERROR)
                    if (OkReceived != null) OkReceived(false);
                    break;

                default: // Reads a line
                    var line = ((char)buf[0]) + _port.ReadLine();
                    if (LineReceived != null) LineReceived(line);
                    break;
            }            
        }

        private async Task ReadCanMessage(DateTime timestamp)
        {
            int bytesToRead, bytesRead;
            do {
                bytesToRead = _canBuf.Length - _canRead;
                bytesRead = await _port.BaseStream.ReadAsync(_canBuf, _canRead, bytesToRead, _cancelRead.Token);
                if (bytesRead == bytesToRead) {
                    // Read completed
                    _canRead = 0;
                    if (CanMessageReceived != null) {
                        var msg = new CanMessage {
                            Bus = _canBuf[0],
                            Id = (_canBuf[1] << 8) + _canBuf[2],
                            Status = _canBuf[12],
                            Data = new byte[_canBuf[11]],
                            DateTime = timestamp
                        };
                        Buffer.BlockCopy(_canBuf, 3, msg.Data, 0, msg.Data.Length);
                        CanMessageReceived(msg);
                    }
                }
                else {
                    // Partial read
                    _canRead += bytesRead;
                }
            }
            while (bytesRead < bytesToRead);
        }
    }
}
