using System;

namespace NanoframeworkExampleCFNet
{
    // README!
    // First adjust the strings in the resource.resx file according to your data
    // For testing you can use the IoTHub I have written and just change the Wi-Fi SSID and password and it should work.
    // Change or add to the parts of the code with @Karoly to get your sensor data in this program
    public class Program
    {
        // This method is run when the main board is powered up or reset.   
        public static void Main()
        {
            // Use Trace to show messages in Visual Studio's "Output" window during debugging.
            Logger.Log("Program Started");
            var device = new Device();

            while (true)
            {
                switch (device.GetStatus())
                {
                    case Device.DeviceStatus.Disconnected:
                        device.ConnectToNetwork();
                        break;
                    case Device.DeviceStatus.ConnectedToNetwork:
                        device.ConnectToServices();
                        break;
                    case Device.DeviceStatus.ConnectedToServices:
                        device.SetupIoCDevice();
                        break;
                    case Device.DeviceStatus.ConfiguredIoCDevice:
                        device.SendSensorData();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}