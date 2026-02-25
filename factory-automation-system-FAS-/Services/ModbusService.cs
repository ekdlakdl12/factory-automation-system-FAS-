using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Modbus.Device;

namespace WpfAppModbus
{
    public class ModbusService : IDisposable
    {
        private TcpClient _tcpClient;
        private ModbusIpMaster _master;
        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(ip, port);
                _master = ModbusIpMaster.CreateIp(_tcpClient);
                return true;
            }
            catch { return false; }
        }

        public void Disconnect()
        {
            _tcpClient?.Close();
            _tcpClient = null;
            _master = null;
        }

        public async Task<ushort[]> ReadRegistersAsync(ushort address, ushort count)
        {
            if (!IsConnected) return null;
            return await Task.Run(() => _master.ReadHoldingRegisters(1, address, count));
        }

        public async Task<bool> WriteRegisterAsync(ushort address, ushort value)
        {
            if (!IsConnected) return false;
            await Task.Run(() => _master.WriteSingleRegister(1, address, value));
            return true;
        }

        public void Dispose() => Disconnect();
    }
}