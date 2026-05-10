using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace QuickBlueToothLE;

internal class Program
{
    private static DeviceInformation device = null;

    public static string HEART_RATE_SERVICE_ID = "180d";


    private static async Task Main(string[] args)
    {
        void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Bluetooth address в формате шестнадцатеричного
            string address = args.BluetoothAddress.ToString("X");
            short rssi = args.RawSignalStrengthInDBm;
            Console.WriteLine($"Device: 0x{address}, RSSI: {rssi} dBm, Timestamp: {args.Timestamp}");

            // Пример чтения данных из рекламного пакета (local name, manufacturer data)
            var localName = args.Advertisement.LocalName;
            if (!string.IsNullOrEmpty(localName))
                Console.WriteLine($"  LocalName: {localName}");

            foreach (var md in args.Advertisement.ManufacturerData)
            {
                var companyId = md.CompanyId;
                var data = md.Data; // IBuffer
                byte[] bytes = new byte[data.Length];
                Windows.Storage.Streams.DataReader.FromBuffer(data).ReadBytes(bytes);
                Console.WriteLine($"  Manufacturer: 0x{companyId:X}, Data: {BitConverter.ToString(bytes)}");
            }

            foreach (var section in args.Advertisement.DataSections)
            {
                Console.WriteLine($"  DataSection Type: 0x{section.DataType:X}, Length: {section.Data.Length}");
            }
        }

        // Query for extra properties you want returned
        string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

        var deviceWatcher =
            new BluetoothLEAdvertisementWatcher
            {
                SignalStrengthFilter =
                {
                    SamplingInterval = TimeSpan.FromMilliseconds(500)
                },
                ScanningMode = BluetoothLEScanningMode.Active
            };
        deviceWatcher.Received += OnAdvertisementReceived;
        deviceWatcher.Start();
        
        Console.WriteLine("Watcher started. Нажмите Enter для остановки...");
        Console.ReadLine();
ач

        deviceWatcher.Stop();
    }
}