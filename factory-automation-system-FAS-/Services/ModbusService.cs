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

        // 연결 (비동기)
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

        // Holding Register 읽기 (주소, 개수)
        public async Task<ushort[]> ReadRegistersAsync(ushort address, ushort count)
        {
            if (!IsConnected) return null;
            return await Task.Run(() => _master.ReadHoldingRegisters(1, address, count));
        }

        // 단일 Register 쓰기
        public async Task<bool> WriteRegisterAsync(ushort address, ushort value)
        {
            if (!IsConnected) return false;
            await Task.Run(() => _master.WriteSingleRegister(1, address, value));
            return true;
        }

        public void Dispose() => Disconnect();
    }
}