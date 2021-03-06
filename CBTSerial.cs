﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CanBusTriple
{
    public delegate void PortStatusHandler(bool isOpen);
    public delegate void CanMessageReceivedHandler(CanMessage msg);
    public delegate void DebugHandler(string info);

    public struct CanMessage
    {
        public int Bus;
        public int Id;
        public byte[] Data;
        public DateTime DateTime;
        /**
         * Status contains information about the two receive buffers of MCP2515 chip
         * 01 -> Message received on buffer 1
         * 02 -> Message received on buffer 2
         * 03 -> Messages received on both buffers
         * */
        public int Status { get; set; }

        public string HexId => $"0x{Id:X3}";

        public string HexData {
            get {
                return Data.Aggregate("0x", (current, b) => current + $"{b:X2}");
            }
        }

        public string Time => DateTime.ToString("HH:mm:ss.fff");
    }

    public class CBTSerial
    {
        public const int BAUD_RATE = 115200;
        public const int READ_TIMEOUT = 3000;
        private readonly SerialPort _port;
        private BackgroundWorker _bgWorker;
        private CancellationTokenSource _cancelRead;

        private readonly byte[] _canBuf = new byte[15];
        private int _canRead;
        private DateTime _startTime;
        private Stopwatch _stopwatch;
        private Dictionary<string, string> _jsonReceived;
        private bool? _resultReceived;
        private string _lineReceived;

        public event PortStatusHandler PortStatusChanged;
        public event CanMessageReceivedHandler CanMessageReceived;
        public event DebugHandler Debug;

        public bool Busy { get; private set; }

        public bool IsOpen => _port.IsOpen;

        internal CBTSerial(string portName)
        {
            _port = new SerialPort(portName, BAUD_RATE, Parity.None, 8, StopBits.One) {
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n",
                ReadTimeout = READ_TIMEOUT
            };
            Busy = false;
        }

        public void OpenPort()
        {
            if (_port.IsOpen) return;
            _port.Open();
            ResetCancelToken();
            _bgWorker = new BackgroundWorker {WorkerSupportsCancellation = true};
            _bgWorker.DoWork += async (sender, e) => { 
                while(!_bgWorker.CancellationPending) await DetectPacket();
                e.Cancel = true;
            };
            _startTime = DateTime.Now;
            _stopwatch = Stopwatch.StartNew();
            _bgWorker.RunWorkerAsync();
            PortStatusChanged?.Invoke(true);
        }

        public async Task ClosePort()
        {
            if (!_port.IsOpen) return;
            _bgWorker.CancelAsync();
            if (!_cancelRead.IsCancellationRequested) _cancelRead.Cancel();
            var timeout = READ_TIMEOUT;
            while (_bgWorker.IsBusy && timeout > 0) {
                await Task.Delay(100);
                timeout -= 100;
            }
            _stopwatch.Stop();
            _port.Close();
            Busy = false;
            PortStatusChanged?.Invoke(false);
        }

        public void SetPort(string portName)
        {
            if (Busy) throw new Exception("Port busy. Cannot set new name.");
            if (_port.IsOpen) throw new Exception("Port is open. Cannot set new name.");
            _port.PortName = portName;
        }

        public async Task EndCommand(bool closePort) {
            if (!_cancelRead.IsCancellationRequested) _cancelRead.Cancel();
            if (closePort) await ClosePort();
            Busy = false;
        }

        public async Task BlindCommand(byte[] cmd)
        {
            ExceptionDispatchInfo capturedException = null;
            var wasOpen = _port.IsOpen;
            try {
                if (!wasOpen) OpenPort();                
                await WriteBytes(cmd);
                // Waits for writing completed
                while (_port.BytesToWrite > 0) await Task.Delay(50);       
            }
            catch (Exception ex) {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            if (!wasOpen) await ClosePort();
            capturedException?.Throw();
        }

        public async Task<Dictionary<string, string>> JsonCommand(byte[] cmd)
        {
            if (Busy) throw new Exception("Port busy. Cannot send command.");
            Busy = true;
            var wasOpen = _port.IsOpen;
            ExceptionDispatchInfo capturedException = null;
            try {
                if (!wasOpen) OpenPort();
                _jsonReceived = null;
                await WriteBytes(cmd);
                var timeout = READ_TIMEOUT;
                while (_jsonReceived == null && timeout > 0) {
                    await Task.Delay(20);
                    timeout -= 20;
                }
                if (_jsonReceived == null) throw new Exception("Command timeout");
            }
            catch (Exception ex) {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            
            await EndCommand(!wasOpen);
            capturedException?.Throw();
            return _jsonReceived;
        }

        public async Task<bool> OkCommand(byte[] cmd)
        {
            if (Busy) throw new Exception("Port busy. Cannot send command.");
            Busy = true;
            var wasOpen = _port.IsOpen;
            ExceptionDispatchInfo capturedException = null;
            try {
                if (!wasOpen) OpenPort();
                _resultReceived = null;
                await WriteBytes(cmd);
                var timeout = READ_TIMEOUT;
                while (_resultReceived == null && timeout > 0) {
                    await Task.Delay(20);
                    timeout -= 20;
                }
                if (_resultReceived == null) throw new Exception("Command timeout");
            }
            catch (Exception ex) {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }

            await EndCommand(!wasOpen);
            capturedException?.Throw();
            return (_resultReceived == true);
        }

        public async Task<string> SendCommand(byte[] cmd)
        {
            if (Busy) throw new Exception("Port busy. Cannot send command.");
            Busy = true;
            var wasOpen = _port.IsOpen;
            ExceptionDispatchInfo capturedException = null;
            try {
                if (!wasOpen) OpenPort();
                _lineReceived = null;
                _jsonReceived = null;
                _resultReceived = null;
                await WriteBytes(cmd);
                var timeout = READ_TIMEOUT;
                while (_lineReceived == null && _jsonReceived == null && _resultReceived == null && timeout > 0) {
                    await Task.Delay(20);
                    timeout -= 20;
                }
                if (timeout < 1) throw new Exception("Command timeout");
            }
            catch (Exception ex) {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }

            await EndCommand(!wasOpen);
            capturedException?.Throw();
            if (_jsonReceived != null)
                return _jsonReceived.Aggregate("", (cur, el) => cur + ("[" + el.Key + "] => " + el.Value + "\r\n"));
            if (_resultReceived != null)
                return (_resultReceived == true) ? "OK" : "ERROR";
            return _lineReceived;
        }

        private void ResetCancelToken()
        {
            _cancelRead = new CancellationTokenSource();
            _cancelRead.Token.Register(ResetCancelToken);
        }

        private async Task DetectPacket()
        {
            var buf = new byte[10];
            try {
                if (!_port.IsOpen) return;                
                var bytesRead = await _port.BaseStream.ReadAsync(buf, 0, 1, _cancelRead.Token);                
                if (bytesRead == 0) return;
            }
            catch (Exception ex) {
                if (ex is TaskCanceledException || ex is IOException || ex is TimeoutException) return;
                throw;
            }

            switch (buf[0]) {
                case 0x03: // CAN message
                    await ReadCanMessage(_startTime + _stopwatch.Elapsed);
                    break;

                case 0x7B: // Json message ('{')
                    var str = _port.ReadTo("}\r\n");
                    Debug?.Invoke("<- {" + str + "}");
                    _jsonReceived = JsonConvert.DeserializeObject<Dictionary<string, string>>("{" + str + "}");
                    break;

                case 0xFF: // OK message (COMMAND_OK)
                    Debug?.Invoke("<- COMMAND_OK");
                    try {
                        // Discard two next bytes (line terminator)
                        var buff = new byte[2];
                        await _port.BaseStream.ReadAsync(buff, 0, 2, _cancelRead.Token);                       
                    }
                    catch (TaskCanceledException) {}
                    _resultReceived = true;
                    break;

                case 0x80: // Error message (COMMAND_ERROR)
                    Debug?.Invoke("<- COMMAND_ERROR");
                    _resultReceived = false;
                    break;

                default: // Read a line
                    _lineReceived = ((char)buf[0]) + _port.ReadLine();
                    Debug?.Invoke(Encoding.ASCII.GetBytes(_lineReceived).Aggregate("<-", (st, el) => st + $" {el:X2}"));
                    break;
            }            
        }

        private async Task ReadCanMessage(DateTime timestamp)
        {
            int bytesToRead, bytesRead;
            do {                
                try {
                    bytesToRead = _canBuf.Length - _canRead;
                    bytesRead = await _port.BaseStream.ReadAsync(_canBuf, _canRead, bytesToRead, _cancelRead.Token);
                }
                catch (TaskCanceledException) {
                    return;
                }
                if (bytesRead == bytesToRead) {
                    // Read completed
                    _canRead = 0;
                    Debug?.Invoke(_canBuf.Aggregate("<- CAN", (str, el) => str + $" {el:X2}"));
                    if (CanMessageReceived == null) continue;
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
                else {
                    // Partial read
                    _canRead += bytesRead;
                }
            }
            while (bytesRead < bytesToRead);
        }

        private async Task WriteBytes(byte[] bytes)
        {
            await _port.BaseStream.WriteAsync(bytes, 0, bytes.Length);
            Debug?.Invoke(bytes.Aggregate("->", (str, el) => str + $" {el:X2}"));
        }
    }
}
