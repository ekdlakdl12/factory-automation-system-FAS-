using System;
using System.IO.Ports;

class SerialRead {
    static void Main() {
        SerialPort serialPort = new SerialPort("/dev/ttyAMA0", 9600);
        SerialPort barcodeSerial = new SerialPort("/dev/ttyACM0", 9600);
        barcodeSerial.DtrEnable = true;
        barcodeSerial.RtsEnable = true;

try {
    barcodeSerial.Open();
    serialPort.Open();

    Console.WriteLine("Commnication...");

    while (true) {
        if (barcodeSerial.BytesToRead > 0) {
            string barcodedata = barcodeSerial.ReadExisting(); 
            
            Console.WriteLine($"[Barcode In]: {barcodedata}");
        
            serialPort.Write(barcodedata);
        }
        if (serialPort.BytesToRead > 0) {
            string serialdata = serialPort.ReadExisting();
            Console.WriteLine($"[Serial Out]: {serialdata}");
        }
    }
}
catch (Exception ex) {
    Console.WriteLine("error: " + ex.Message);
}
finally {
    if (barcodeSerial.IsOpen) barcodeSerial.Close();
    if (serialPort.IsOpen) serialPort.Close(); 
}
    }
}