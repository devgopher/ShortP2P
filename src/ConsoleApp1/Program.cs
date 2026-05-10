using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

class Program
{
    static async Task Main()
    {
        // var watcher = new BluetoothLEAdvertisementWatcher
        // {
        //     ScanningMode = BluetoothLEScanningMode.Active
        // };
        // watcher.Received += async (s, e) =>
        // {
        //     var name = e.Advertisement.LocalName;
        //     Console.WriteLine($"Addr: {e.BluetoothAddress:X}, RSSI:{e.RawSignalStrengthInDBm}, Name:'{name}'");
        //     await Demo(e.BluetoothAddress);
        // };
        // watcher.Stopped += (s,e) => Console.WriteLine("Watcher stopped: " + e.Error);
        // watcher.Start();
        // Console.WriteLine("Scanning... press Enter to stop");
        // Console.ReadLine();
        // watcher.Stop();
        //string selector = $"System.Devices.Aep.DeviceAddress:=\"B960D3FE3ADB\"";
        
        
        var devices = await DeviceInformation.FindAllAsync();

        // var devices = await DeviceInformation.FindAllAsync(DeviceClass.All);
        var dd = devices.Where(d => d.IsEnabled && d.Name.Contains("DACHA"));
        var all = dd.Select(d => JsonSerializer.Serialize(d)).ToArray();
        var canPair = dd.Where(d => d.Pairing.CanPair).ToArray();
        var paired = dd.Where(d => d.Pairing.IsPaired).ToArray();

        foreach (var d in devices) Console.WriteLine($"Found: {d.Name} ({d.Id})");
    }
    
    static async Task Demo(ulong address)
    {
        // Device address hex uppercase without 0x, e.g., "AABBCCDDEEFF"
         string addrHex = address.ToString("X");
         
        // string selector = $"System.Devices.Aep.DeviceAddress:=\"{addrHex}\" AND System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#False";
        //
        // var t = new Thread(async () =>
        // {
        //     try
        //     {
        //
        //     }
        //     catch (Exception ex) { Console.WriteLine("Error: " + ex); }
        // });
        // t.SetApartmentState(ApartmentState.STA);
        // t.Start();
        // t.Join();

        

    }
}