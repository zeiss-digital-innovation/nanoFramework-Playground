using System;
using System.Device.I2c;
using System.Threading;
using Iot.Device.Common;
using Iot.Device.Shtc3;
using nanoFramework.Hardware.Esp32;
using nanoFramework.Networking;
using NanoframeworkExampleCFNet.Infrastructure;

namespace NanoframeworkExampleCFNet
{
    public class Device
    {
        private readonly MqttManager _mqttManager = new();
        private Shtc3 _ioCDevice;
        private DateTime _lastMessageSent = DateTime.MinValue;
        public DeviceStatus GetStatus()
        {
            if (WifiNetworkHelper.Status != NetworkHelperStatus.NetworkIsReady) return DeviceStatus.Disconnected;

            var mqttIsReady = _mqttManager != null && _mqttManager.IsConnected();
            if (!mqttIsReady)
            {
                return DeviceStatus.ConnectedToNetwork;
            }

            var iocDeviceIsReady = _ioCDevice != null;
            if (iocDeviceIsReady)
            {
                return DeviceStatus.ConfiguredIoCDevice;
            }
            return DeviceStatus.ConnectedToServices;
        }

        public void ConnectToNetwork() => NetworkManager.ConnectToNetwork();

        public void ConnectToServices()
        {
            try
            {
                _mqttManager.Connect();
            }
            catch (Exception e)
            {
                Logger.Log($"[MQTT MqttClient] setup failed. {e.Message}");
                Logger.Log("[MQTT MqttClient] is Disconnected");
                Thread.Sleep(1000);
                Logger.Log("[MQTT MqttClient] retry");
            }
        }

        public void SetupIoCDevice()
        {
            try
            {
                Configuration.SetPinFunction(21, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(22, DeviceFunction.I2C1_CLOCK);

                I2cConnectionSettings settings = new I2cConnectionSettings(1, Shtc3.DefaultI2cAddress);
                I2cDevice device = I2cDevice.Create(settings);

                _ioCDevice = new Shtc3(device);
            }
            catch (Exception e)
            {
                Logger.Log($"[SHTC3 sensor] setup failed. {e.Message}");
            }
        }

        public void SendSensorData()
        {
            if (DateTime.UtcNow - _lastMessageSent < TimeSpan.FromMinutes(1))
            {
                Thread.SpinWait(1);
                return;
            }

            if (_ioCDevice.TryGetTemperatureAndHumidity(out var temperature, out var relativeHumidity))
            {
                Logger.Log(
                    $"[STC3 sensor] read succeeded Temperature:{temperature} RelativeHumidity{relativeHumidity}");
                
                _lastMessageSent = DateTime.UtcNow;
                
                _mqttManager.PublishTelemetry(InstrumentNames.Temperature,
                    temperature.DegreesCelsius.ToString());
                _mqttManager.PublishTelemetry(InstrumentNames.RelativeHumidity,
                    relativeHumidity.Percent.ToString());
                _mqttManager.PublishTelemetry(InstrumentNames.DewPoint,
                    WeatherHelper.CalculateDewPoint(temperature, relativeHumidity).DegreesCelsius
                        .ToString());
                _mqttManager.PublishTelemetry(InstrumentNames.HeatIndex,
                    WeatherHelper.CalculateHeatIndex(temperature, relativeHumidity).DegreesCelsius
                        .ToString());
                _mqttManager.PublishTelemetry(InstrumentNames.VaporPressure,
                    WeatherHelper.CalculateActualVaporPressure(temperature, relativeHumidity)
                        .Kilopascals
                        .ToString());
            }
            else
            {
                Logger.Log("[SHTC3 sensor] read failed");
            }
        }

        public enum DeviceStatus
        {
            Disconnected = 0,
            ConnectedToNetwork,
            ConnectedToServices,
            ConfiguredIoCDevice
        }
    }
}