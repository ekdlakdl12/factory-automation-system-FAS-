#include <iostream>
#include <thread>
#include <chrono>
#include <cerrno>

#include <modbus/modbus.h>

using namespace std;

static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;

// 테스트할 코일 주소(너가 쓰던 100번대 기준)
static const int TEST_COIL_ADDR = 101;   // 101로 테스트
static const int PULSE_MS = 1000;

static modbus_t* ConnectModbus(const char* ip, int port)
{
    modbus_t* ctx = modbus_new_tcp(ip, port);
    if (!ctx) return nullptr;

    // 응답 타임아웃
    modbus_set_response_timeout(ctx, 0, 500000); // 500ms

    if (modbus_connect(ctx) == -1) {
        modbus_free(ctx);
        return nullptr;
    }
    return ctx;
}

static bool WriteCoil(modbus_t* ctx, int addr, bool val)
{
    int rc = modbus_write_bit(ctx, addr, val ? 1 : 0); // FC5
    return (rc == 1);
}

static bool ReadCoil(modbus_t* ctx, int addr, bool& outVal)
{
    uint8_t bit = 0;
    int rc = modbus_read_bits(ctx, addr, 1, &bit); // FC1
    if (rc != 1) return false;
    outVal = (bit != 0);
    return true;
}

static void DumpWriteRead(modbus_t* ctx, int addr, bool v)
{
    errno = 0;
    bool okW = WriteCoil(ctx, addr, v);

    bool r = false;
    errno = 0;
    bool okR = ReadCoil(ctx, addr, r);

    cout << "[MB] addr=" << addr
        << " write=" << (okW ? "OK" : "FAIL")
        << " read=" << (okR ? (r ? "1" : "0") : "FAIL")
        << " err=" << modbus_strerror(errno)
        << "\n";
}

int main()
{
    cout << "[MODBUS] connect to " << PLC_IP << ":" << PLC_PORT << "\n";

    modbus_t* ctx = ConnectModbus(PLC_IP, PLC_PORT);
    if (!ctx) {
        cout << "[MODBUS] connect FAIL: " << modbus_strerror(errno) << "\n";
        return -1;
    }
    cout << "[MODBUS] connected OK\n";

    cout << "[TEST] coil addr = " << TEST_COIL_ADDR << "\n";
    cout << "1) write 0\n";
    DumpWriteRead(ctx, TEST_COIL_ADDR, false);

    cout << "2) write 1 (pulse)\n";
    DumpWriteRead(ctx, TEST_COIL_ADDR, true);
    this_thread::sleep_for(chrono::milliseconds(PULSE_MS));

    cout << "3) write 0\n";
    DumpWriteRead(ctx, TEST_COIL_ADDR, false);

    modbus_close(ctx);
    modbus_free(ctx);

    cout << "[DONE]\n";
    return 0;
}
