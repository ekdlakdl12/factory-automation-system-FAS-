#include <LiquidCrystal_I2C.h>
#include <SoftwareSerial.h>
#include <DHT.h>
#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BME280.h>

#define DHTPIN 4
#define DHTTYPE DHT22
#define FX22_PIN A1   //가스센서
#define ESP_RX 2
#define ESP_TX 3

SoftwareSerial wifi(2, 3); // RX, TX
// 대부분의 주소는 0x27입니다. 안되면 0x3F로 변경해 보세요.
LiquidCrystal_I2C lcd(0x27, 16, 2); // 주소, 가로 16칸, 세로 2줄

DHT dht(DHTPIN, DHTTYPE);
Adafruit_BME280 bme;

void setup() {
  Serial.begin(115200);
  wifi.begin(9600); // ESP-01 기본 속도 (안되면 9600으로 시도)
  wifiInit();
  dht.begin();
  if (!bme.begin(0x76)) {
    Serial.println("BME280 센서 오류!");
  }
  lcdInit();
}

void loop() {
  // 1. ESP-01(PC로부터 온 데이터) -> 시리얼 모니터로 출력
  if (wifi.available()) {
    while (wifi.available()) {
      Serial.write(wifi.read());
    }
  }

  float hum = dht.readHumidity();
  float temp = dht.readTemperature();
  int fxValue = analogRead(FX22_PIN);
  float bmePres = bme.readPressure() / 100.0F;

  String data = "FX:" + String(fxValue) + ",T:" + String(temp) + ",H:" + String(hum) + ",P:" + String(bmePres);
  Serial.println("수집 데이터: " + data);
  sendDataToPC(data);
  delay(1000);
  lcdPrint("Humidity", hum);
  lcdPrint("Temperature", temp);
  lcdPrint("GasSensor", fxValue);
  lcdPrint("BMP", bmePres);
}

// PC로 데이터를 보내는 함수 (ID 0번 클라이언트 기준)
void sendDataToPC(String data) {
  wifi.print("AT+CIPSEND=0,"); // 0번 클라이언트에게 전송
  wifi.println(data.length());
  delay(100);
  wifi.print(data);
}

void sendAT(String command, const int timeout) {
  wifi.println(command);
  long startTime = millis();
  while (millis() - startTime < timeout) {
    if (wifi.available()) {
      Serial.write(wifi.read());
    }
  }
}

void wifiInit()
{
  
  Serial.println("ESP-01 PC Communication Setup...");

  // 1. WiFi 모드 설정 (Station)
  sendAT("AT+CWMODE=1", 2000);

  wifi.println("AT+CWHOSTNAME=\"Gas_Sensor_1\""); // 이름 지정
  delay(100);
  sendAT("AT+CIPSTA=\"192.168.0.220\",\"192.168.0.1\",\"255.255.255.0\"", 5000);

  sendAT("AT+CWJAP=\"PLC\",\"spreatics*\"", 5000);
  // 2. 공유기 접속 (SSID, PW 수정)
  // 3. 할당된 IP 주소 확인 (PC에서 이 주소로 접속해야 함)
  sendAT("AT+CIFSR", 2000);
  
  // 4. 다중 접속 허용 (서버 구축 시 필수)
  sendAT("AT+CIPMUX=1", 1000);
  
  // 5. 서버 가동 (포트 번호 8080)
  sendAT("AT+CIPSERVER=1,8080", 1000);
  
  Serial.println("Server is Ready!");
}

void lcdInit()
{
  lcd.init();      // LCD 초기화
  lcd.backlight(); // 백라이트 켜기
  lcd.setCursor(0, 0); // 첫 번째 줄 첫 번째 칸으로 커서 이동 (0, 0)
}

void lcdPrint(String tmp1, const float tmp2)
{
  lcd.setCursor(0, 0);
  lcd.print(tmp1);
  lcd.setCursor(0, 1);
  lcd.print(tmp2);
  delay(1000);
}
