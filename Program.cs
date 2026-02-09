using Iot.Device.Hcsr04; // NuGet 패키지 필요
using Iot.Device.Imu;
using Iot.Device.Mfrc522; // RC522 전용 클래스
using Iot.Device.Rfid;    // RFID 공통 라이브러리
using Iot.Device.ServoMotor; // 서보 전용 클래스
using Iot.Device.Vcnl4040.Definitions;
using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Pwm;
using System.Device.Pwm.Drivers;
using System.Device.Spi;
using System.Diagnostics;
using System.Numerics; // Vector3 사용을 위해 필요
using System.Threading;
using System.Text.Json;


int[] hyperCenter = { 23, 24 };
int[] hyperLeft = { 5, 6 };
int[] hyperRight = { 12, 16 };

String motion_rd = "QR1";
String motion_location = "";
int motion_int = 0;
int motion_dist = 0;
string motion_direction = "";

//움직임 제어 중
//     1 : 5E 8C CC 1
//     2 : 29 17 BC 2 
//     3 : 4E 8E B2 1
//     4 : 87 2E C2 1 
//     5 : 37 34 B9 2 
//     6 : 19 46 4F 5
//     7 : 9C 2C 22 3
//     8 : 21 20 1A 2 


// --- 주기적 실행 루프 ---
// 마지막으로 읽은 QR 코드 값 저장
string lastMotionRd = "";

while (true)
{
    // 사용자 입력 확인 (exit 입력 시 종료)
    if (Console.KeyAvailable)
    {
        string input = Console.ReadLine();
        if (input?.ToLower() == "exit")
        {
            Console.WriteLine("프로그램 종료");
            break;
        }
    }

    try
    {
        string currentMotionRd = ReadQRCodeFromJSON("camera_output.json");

        // JSON 값이 갱신되었을 때만 동작
        if (!string.IsNullOrEmpty(currentMotionRd) && currentMotionRd != lastMotionRd)
        {
            motion();  // 모션 제어 실행
            lastMotionRd = currentMotionRd; // 최신 값 갱신
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"오류 발생: {ex.Message}");
    }

    Thread.Sleep(500); // 0.5초 간격
}


void motion()
{
    //motion_rd = readDataFromPC();
    string motion_rd = ReadQRCodeFromJSON("camera_output.json");
    motion_rd = "";
    double front_Distance = HyperSensor(hyperCenter);
    double left_Distance = HyperSensor(hyperLeft);
    double right_Distance = HyperSensor(hyperRight);

    String rfid_data = "";
    if (motion_rd == "QR1") { motion_int = 1; motion_dist = 0; rfid_data = "5E 8C CC 1"; }
    else if (motion_rd == "QR2") { motion_int = 2; motion_dist = 100; rfid_data = "29 17 BC 2"; }
    else if (motion_rd == "QR3") { motion_int = 3; motion_dist = 200; rfid_data = "4E 8E B2 1"; }
    else if (motion_rd == "QR4") { motion_int = 4; motion_dist = 300; rfid_data = "37 34 B9 2"; }
    else if (motion_rd == "QR5") { motion_int = 5; motion_dist = 400; rfid_data = "19 46 4F 5"; }
    else if (motion_rd == "QR6") { motion_int = 6; motion_dist = 500; rfid_data = "9C 2C 22 3"; }

    //움직이는 방향 결정
    if (motorDistance() < motion_dist)
    {
        motion_direction = "forward";
    }
    else if (motorDistance() > motion_dist)
    {
        motion_direction = "backward";
    }
    else
    {
        driveMotor("stop", "");
    }

    //전방 가까우거나 현재 값과 움직이는 위치가 같으면 정지
    if (front_Distance <= 30 && motion_rd == motion_location)
    {
        driveMotor("stop", "");
    }
    else
    {
        if(motion_direction == "forward")
        {
            driveMotor("go", "forward");
        }
        if(motion_direction == "backward")
        {
            driveMotor("go", "backward");
        }

    }

    //벽에 가까운면 정역에 따른 서보모터 움직임 변경
    //왼쪽
    if (left_Distance < 30)
    {
        if (motion_direction == "forward")
        {
            steerServo(90 + GyroSensor());
        }
        if(motion_direction == "backward")
        {
            steerServo(90 - GyroSensor());
        }
    }
    //오른쪽
    if(right_Distance < 30)
    {
        if(motion_direction == "forward")
        {
            steerServo(90 - GyroSensor());
        }
        if(motion_direction == "backward")
        {
            steerServo(90 + GyroSensor());
        }
    }

    //현재 위치 값 업데이트
    if (motion_rd == motion_location || RfidReader() == rfid_data)
    {
        motion_location = motion_rd;
    }
}




//초음파 센서 함수
double HyperSensor(int[] HyperSensor)
{
    //초음파 센서 확인
    // 1. 핀 설정 (BCM 번호 기준)

    using Hcsr04 sonar = new Hcsr04(HyperSensor[0], HyperSensor[1]);

    //Console.WriteLine("초음파 거리 측정 시작...");

    // 거리 읽기 시도
    if (sonar.TryGetDistance(out var distance))
    {
        // 센티미터 단위로 출력
        double cm = distance.Centimeters;
        return cm;
    }
    return 0;
}

//자이로 센서 
int GyroSensor()
{
    var settings = new I2cConnectionSettings(1, Mpu6050.DefaultI2cAddress);
    using var i2cDevice = I2cDevice.Create(settings);
    using var sensor = new Mpu6050(i2cDevice);

    double yaw = 0; // 우리가 구하고자 하는 Z축 각도
    Stopwatch sw = new Stopwatch();

    //Console.WriteLine("Z축 각도 측정 시작... (정지 상태에서 시작하세요)");
    sw.Start();
    // 지난 루프부터 지금까지 걸린 시간(dt) 계산
    double dt = sw.Elapsed.TotalSeconds;
    sw.Restart();

    // 자이로 데이터 읽기 (Z축 회전 속도)
    Vector3 gyro = sensor.GetGyroscopeReading();

    // Z축 회전 속도가 미세하게 변할 때 발생하는 '노이즈(드리프트)' 제거
    // 0.5도/s 미만의 움직임은 정지 상태로 간주
    double gyroZ = gyro.Z;
    if (Math.Abs(gyroZ) < 0.5) gyroZ = 0;

    // 적분: 각도 = 각도 + (각속도 * 시간)
    yaw += gyroZ * dt;

    // 결과 출력
    Console.Clear();
    Thread.Sleep(50); // 20Hz 주기로 측정
    return (int)yaw;
}

//태그
string RfidReader()
{
    String rfid_tmp = "";
    var connectionSettings = new SpiConnectionSettings(0, 0)
    {
        ClockFrequency = 10_000_000,
        Mode = SpiMode.Mode0
    };

    using SpiDevice spi = SpiDevice.Create(connectionSettings);
    using MfRc522 mfrc522 = new MfRc522(spi, 25); // 25는 리셋 핀(GPIO 25)
    //Console.WriteLine("RFID 리더기 준비 완료. 카드를 태그해주세요...");
    // 카드 감지 시도
    if (mfrc522.ListenToCardIso14443TypeA(out var card, TimeSpan.FromSeconds(20)))
    {
        // 카드 UID 추출 및 출력
        string uid = BitConverter.ToString(card.NfcId);
        //Console.WriteLine($"[카드 감지!] UID: {uid}");
        // 태그 후 중복 인식을 방지하기 위한 짧은 대기
        Thread.Sleep(1000);
        return uid;
    }
    return null;
}

//서보모터
void steerServo(double angle)
{
    //서보 모터 확인
    // 1. PWM 설정 (핀 18, 주파수 50Hz - SG90 표준)
    using PwmChannel pwmChannel = PwmChannel.Create(0, 0, 50); // 하드웨어 PWM 버스 0 사용
    using ServoMotor servo = new ServoMotor(pwmChannel, 180, 500, 2500); // 180도, 최소 500us, 최대 2500us
    servo.Start();
    //Console.WriteLine("SG90 서보 모터 제어 시작...");
    servo.WriteAngle(angle);
    Thread.Sleep(1000);
}

//모터 정역
void driveMotor(String moving, String direction)
{
    //모터 확인
    // 1. GPIO 컨트롤러 생성
    using GpioController controller = new GpioController();

    // 2. 핀 번호 설정 (BCM 기준)
    int[] motorPins = { 17, 27, 22, 23 }; // PC817을 거쳐 L298N IN1에 연결된 핀
                                          // 3. 핀 모드 설정
    controller.OpenPin(motorPins[0], PinMode.Output);
    controller.OpenPin(motorPins[0], PinMode.Output);
    controller.OpenPin(motorPins[0], PinMode.Output);
    controller.OpenPin(motorPins[0], PinMode.Output);

    //Console.WriteLine("모터 테스트 시작 (PC817 제어)");

    if (moving == "go")
    {
        if (direction == "forward")
        {
            Console.WriteLine("모터 회전 시작 (HIGH)");
            controller.Write(motorPins[0], PinValue.High); // 800옴 저항을 통해 PC817 LED 점등
            controller.Write(motorPins[1], PinValue.Low);
            controller.Write(motorPins[2], PinValue.High);
            controller.Write(motorPins[3], PinValue.Low);
            Thread.Sleep(500);
        }
        else if (direction == "backward")
        {
            Console.WriteLine("모터 정지 (LOW)");
            controller.Write(motorPins[0], PinValue.Low);  // 1.3k옴 풀다운이 GND로 잡아줌
            controller.Write(motorPins[1], PinValue.High);
            controller.Write(motorPins[2], PinValue.Low);
            controller.Write(motorPins[3], PinValue.High);
            Thread.Sleep(500);
        }
    }
    else if (moving == "stop")
    {
        Console.WriteLine("모터 정지 (LOW)");
        controller.Write(motorPins[0], PinValue.Low);  // 1.3k옴 풀다운이 GND로 잡아줌
        controller.Write(motorPins[1], PinValue.Low);
        controller.Write(motorPins[2], PinValue.Low);
        controller.Write(motorPins[3], PinValue.Low);
        Thread.Sleep(500);
    }
    // 4. 핀 정리
    //foreach (var pin in motorPins)
    //{
    //    controller.Write(pin, PinValue.Low);
    //    controller.ClosePin(pin);
    //}
    //Console.WriteLine("모터 테스트 종료 및 핀 정리 완료.");
}

//대략적인 위치 측정
double motorDistance()
{
    int rps = 200 / 60;
    double wheel = 2 * 3.14 * 10;
    double distance = rps * wheel;
    return distance;
}


string ReadQRCodeFromJSON(string filename)
{
    try
    {
        string jsonText = File.ReadAllText(filename);
        using (JsonDocument doc = JsonDocument.Parse(jsonText))
        {
            if (doc.RootElement.TryGetProperty("qr_code", out JsonElement qrCodeElement))
            {
                return qrCodeElement.GetString();
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("JSON 읽기 오류: " + ex.Message);
    }
    return "";
}





//ServoMotor servoMotor;
//List<Hcsr04> ultrasonicSensors = new List<Hcsr04>();
//Stopwatch imuStopwatch = new Stopwatch();

//using GpioController controller = new GpioController();
//var connectionSettings = new SpiConnectionSettings(0, 0)
//{
//    ClockFrequency = 10_000_000,
//    Mode = SpiMode.Mode0
//};
//using PwmChannel pwmChannel = PwmChannel.Create(0, 0, 50); // 하드웨어 PWM 버스 0 사용
//using ServoMotor servo = new ServoMotor(pwmChannel, 180, 500, 2500); // 180도, 최소 500us, 최대 2500us

//int[] EchoPins = { 24, 6, 16 };
//int[] TrigPins = { 23, 5, 12 };
//int[] MotorPin = { 17, 27, 22, 13 }; // IN1, IN2, IN3, IN4
//const int RfidResetPin = 25;
////const int ServoPin = 18;

//void InitializeHardware()
//{

//    // GPIO 설정 (L298N 제어용)
//    foreach (var Pin in MotorPin)
//    {
//        controller.OpenPin(Pin, PinMode.Output);
//        controller.Write(Pin, PinValue.Low);
//    }
//    // I2C 설정 (MPU6050)
//    var i2cSettings = new I2cConnectionSettings(1, Mpu6050.DefaultI2cAddress);
//    var i2cDevice = I2cDevice.Create(i2cSettings);
//    Mpu6050 imuSensor = new Mpu6050(i2cDevice);

//    // SPI 설정 (RC522)
//    var spiSettings = new SpiConnectionSettings(0, 0) { ClockFrequency = 10_000_000, Mode = SpiMode.Mode0 };
//    var spiDevice = SpiDevice.Create(spiSettings);
//    MfRc522 rfidReader = new MfRc522(spiDevice, RfidResetPin);

//    // PWM 설정 (SG90) - config.txt 설정 필수
//    var pwmChannel = PwmChannel.Create(0, 0, 50); // Chip 0, Channel 0 (GPIO 18)
//    servoMotor = new ServoMotor(pwmChannel, 180, 500, 2500);
//    servoMotor.Start();

//    // 초음파 센서 설정
//    for (int i = 0; i < TrigPins.Length; i++)
//    {
//        ultrasonicSensors.Add(new Hcsr04(TrigPins[i], EchoPins[i]));
//    }
//    Console.WriteLine("모든 하드웨어 초기화 완료.");
//}

//// --- 정리 함수 ---
////void CleanupHardware()
////{
////    controller.Write(MotorPin, PinValue.Low);
////    controller.Dispose();
////    rfidReader?.Dispose();
////    imuSensor?.Dispose();
////    servoMotor?.Stop();
////    servoMotor?.Dispose();
////    foreach (var s in ultrasonicSensors) s.Dispose();
////    Console.WriteLine("시스템 종료 및 자원 해제 완료.");
////}


//String motion_rd = "QR1";
//String motion_location = "";
//int motion_int = 0;
//int motion_dist = 0;
//void motion()
//{
//    //motion_rd = readDataFromPC();
//    int distance = HyperSensor(Stream front);
//    String rfid_data = "";
//    if (motion_rd == "QR1") { motion_int = 1; motion_dist = 0; rfid_data = "5E 8C CC 1"; }
//    else if (motion_rd == "QR2") { motion_int = 2; motion_dist = 100; rfid_data = "29 17 BC 2"; }
//    else if (motion_rd == "QR3") { motion_int = 3; motion_dist = 200; rfid_data = "4E 8E B2 1"; }
//    else if (motion_rd == "QR4") { motion_int = 4; motion_dist = 300; rfid_data = "37 34 B9 2"; }
//    else if (motion_rd == "QR5") { motion_int = 5; motion_dist = 400; rfid_data = "19 46 4F 5"; }
//    else if (motion_rd == "QR6") { motion_int = 6; motion_dist = 500; rfid_data = "9C 2C 22 3"; }

//    if (distance <= 30 && motion_rd == motion_location)
//    {
//        driveMotor("stop", "");
//    }

//    if (motion_rd == motion_location || RfidReader() == rfid_data)
//    {
//        motion_location = motion_rd;
//    }
//    servoMotor.WriteAngle(GyroSensor());
//}




//void HyperSensor()
//{
//    Console.WriteLine("초음파 거리 측정 시작...");
//    foreach (var item in ultrasonicSensors)
//    {
//        // item.Sensor를 통해 거리를 측정
//        if (item.TryGetDistance(out var distance) && distance.Centimeters < 15)
//        {
//            // item.Name을 사용하여 어느 방향인지 구분
//            Console.WriteLine($"[{item}] 장애물 감지! 거리: {distance.Centimeters:F1}cm");

//            // 예: 방향에 따른 분기 처리
//            if (item.Equals == "중앙")
//            {

//            }
//        }
//    }
//}
//double GyroSensor()
//{
//    double yaw = 0; // 우리가 구하고자 하는 Z축 각도
//    Stopwatch sw = new Stopwatch();

//    Console.WriteLine("Z축 각도 측정 시작... (정지 상태에서 시작하세요)");
//    sw.Start();

//    // 지난 루프부터 지금까지 걸린 시간(dt) 계산
//    double dt = sw.Elapsed.TotalSeconds;
//    sw.Restart();

//    // 자이로 데이터 읽기 (Z축 회전 속도)
//    Vector3 gyro = sensor.GetGyroscopeReading();

//    // Z축 회전 속도가 미세하게 변할 때 발생하는 '노이즈(드리프트)' 제거
//    // 0.5도/s 미만의 움직임은 정지 상태로 간주
//    double gyroZ = gyro.Z;
//    if (Math.Abs(gyroZ) < 0.5) gyroZ = 0;

//    // 적분: 각도 = 각도 + (각속도 * 시간)
//    yaw += gyroZ * dt;

//    // 결과 출력
//    Console.Clear();
//    return yaw;

//    Thread.Sleep(50); // 20Hz 주기로 측정
//}
//string RfidReader()
//{
//    using SpiDevice spi = SpiDevice.Create(connectionSettings);
//    using MfRc522 mfrc522 = new MfRc522(spi, 25); // 25는 리셋 핀(GPIO 25)
//    Console.WriteLine("RFID 리더기 준비 완료. 카드를 태그해주세요...");
//    // 카드 감지 시도
//    if (mfrc522.ListenToCardIso14443TypeA(out var card, TimeSpan.FromSeconds(100)))
//    {
//        var rfid_data = new[] {
//            (Name: "5E-8C-CC-1", Device: "QR1"),
//            (Name: "29-17-BC-2", Device: "QR2"),
//            (Name: "4E-8E-B2-1", Device: "QR3"),
//            (Name: "87-2E-C2-1", Device: "QR4"),
//            (Name: "37-34-B9-2", Device: "QR5"),
//            (Name: "19-46-4F-5", Device: "QR6"),
//            (Name: "9C-2C-22-3", Device: "QR7"),
//            (Name: "21-20-1A-2", Device: "QR8")
//        };
//        // 카드 UID 추출 및 출력
//        string uid = BitConverter.ToString(card.NfcId);
//        Console.WriteLine($"[카드 감지!] UID: {uid}");
//        // 특정 UID 카드 확인 예시  
//        foreach (var rfid in rfid_data)
//        {
//            if (uid.Replace("-", " ") == rfid.Name)
//            {
//                Console.WriteLine($">>> 등록된 카드 확인: {rfid.Device}");
//                Thread.Sleep(1000);
//                return rfid.Device;
//            }
//        }
//    }return null;
//}
//void ServoMotor(int angle)
//{
//    //서보 모터 확인
//    // 1. PWM 설정 (핀 18, 주파수 50Hz - SG90 표준)

//    servo.Start();
//    servo.WriteAngle(angle);
//}
//void driveMotor(String moving, String direction)
//{
//    Console.WriteLine("모터 테스트 시작 (PC817 제어)");
//    if (moving == "go")
//    {
//        if (direction == "forward")
//        {
//            controller.Write(MotorPin[0], PinValue.High);
//            controller.Write(MotorPin[1], PinValue.Low);
//            controller.Write(MotorPin[2], PinValue.High);
//            controller.Write(MotorPin[3], PinValue.Low);
//            Thread.Sleep(2000);
//        }
//        else if (direction == "backward")
//        {
//            controller.Write(MotorPin[0], PinValue.Low);
//            controller.Write(MotorPin[1], PinValue.High);
//            controller.Write(MotorPin[2], PinValue.Low);
//            controller.Write(MotorPin[3], PinValue.High);
//            Thread.Sleep(2000);
//        }
//        else if (moving == "stop")
//        {
//            controller.Write(MotorPin[1], PinValue.Low);
//            controller.Write(MotorPin[2], PinValue.High);
//            controller.Write(MotorPin[3], PinValue.Low);
//            controller.Write(MotorPin[4], PinValue.High);
//            Thread.Sleep(2000);
//        }
//    }
//}
//double motorDistance()
//{
//    int rps = 200 / 60;
//    double wheel = 2 * 3.14 * 10;
//    double distance = rps * wheel;
//    return distance;
//}


