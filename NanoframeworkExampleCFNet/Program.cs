using System;
using System.Device.I2c;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web;
using Iot.Device.Common;
using Iot.Device.Shtc3;
using nanoFramework.M2Mqtt;
using nanoFramework.M2Mqtt.Messages;
using nanoFramework.Networking;
using nanoFramework.Hardware.Esp32;
using UnitsNet;

namespace NanoframeworkExampleCFNet
{
    // README!
    // First adjust the strings in the resourse.resx file according to your data
    // For testing you can use the IoTHub I have written and just change the wifi SSID and password and it should work.
    // Change or add to the parts of the code with @Karoly to get your sensor data in this program
    public class Program
    {
        private static readonly string DeviceId = Resource.GetString(Resource.StringResources.DeviceID);
        private static readonly string HostName = Resource.GetString(Resource.StringResources.HostName);

        private static readonly string SasKey = Resource.GetString(Resource.StringResources.SasKey);


        private static readonly string Ssid = Resource.GetString(Resource.StringResources.SSID);
        private static readonly string WifiPassword = Resource.GetString(Resource.StringResources.WifiPassword);

        // @Karoly You can change the topic names as needed according to the sensor use cases
        private static string telemetryTopic = "";
        private const string twinReportedPropertiesTopic = "$iothub/twin/PATCH/properties/reported/";
        private const string twinDesiredPropertiesTopic = "$iothub/twin/GET/";


        // This method is run when the mainboard is powered up or reset.   
        public static void Main()
        {
            // Use Trace to show messages in Visual Studio's "Output" window during debugging.
            Trace("Program Started");

            telemetryTopic = string.Format("devices/{0}/messages/events/", DeviceId);

            Debug.WriteLine("Waiting for network up and IP address...");
            bool success;
            CancellationTokenSource cs = new(60000);
            success = WifiNetworkHelper.ConnectDhcp(Ssid, WifiPassword, requiresDateTime: true, token: cs.Token);

            if (!success)
            {
                Debug.WriteLine($"Can't get a proper IP address and DateTime, error: {WifiNetworkHelper.Status}.");
                if (WifiNetworkHelper.HelperException != null)
                    Debug.WriteLine($"Exception: {WifiNetworkHelper.HelperException}");
                return;
            }

            Thread.Sleep(3000); //used to reliably allow redeployment in VS2019

            Start();

            Thread.Sleep(Timeout.Infinite);
        }

        private static void Start()
        {
            while (true)
            {
                try
                {
                    using var mqttc = SetupMqttClient();
                    try
                    {

                        using var sensor = SetupSensor();
                        while (mqttc.IsConnected)
                        {
                            if (sensor.TryGetTemperatureAndHumidity(out var temperature, out var relativeHumidity))
                            {
                                Trace(string.Format(
                                    "{0} [STC3 sensor]" + " read succeeded Temperature:{1} RelativeHumidity{2}",
                                    DateTime.UtcNow.ToString("u"), temperature, relativeHumidity));

                                PublishTelemetry(mqttc, InstrumentNames.Temperature, temperature.DegreesCelsius.ToString());
                                PublishTelemetry(mqttc, InstrumentNames.RelativeHumidity,
                                    relativeHumidity.Percent.ToString());
                                PublishTelemetry(mqttc, InstrumentNames.DewPoint,
                                    WeatherHelper.CalculateDewPoint(temperature, relativeHumidity).DegreesCelsius
                                        .ToString());
                                PublishTelemetry(mqttc, InstrumentNames.HeatIndex,
                                    WeatherHelper.CalculateHeatIndex(temperature, relativeHumidity).DegreesCelsius
                                        .ToString());
                                PublishTelemetry(mqttc, InstrumentNames.VaporPressure,
                                    WeatherHelper.CalculateActualVaporPressure(temperature, relativeHumidity).Kilopascals
                                        .ToString());
                            }
                            else
                            {
                                Trace(string.Format("{0} [SHTC3 sensor]" + " read failed", DateTime.UtcNow.ToString("u")));
                            }

                            Thread.Sleep(1000 * 60);
                        }
                    }
                    catch (Exception e)
                    {
                        Trace(string.Format("{0} [SHTC3 sensor]" + " setup failed. {1}", DateTime.UtcNow.ToString("u"), e.Message));
                    }
                }
                catch (Exception e)
                {
                    Trace(string.Format("{0} [MQTT Client]" + " setup failed. {1}", DateTime.UtcNow.ToString("u"), e.Message));
                }
                Trace(string.Format("{0} [MQTT Client]" + " is Disconnected", DateTime.UtcNow.ToString("u")));
                Thread.Sleep(1000);
                Trace(string.Format("{0} [MQTT Client]" + " retry", DateTime.UtcNow.ToString("u")));
            }
        }

        private static void PublishTelemetry(MqttClient mqttc, string instrumentName, string value)
        {
            //Publish telemetry data using AT LEAST ONCE QOS Level
            mqttc.Publish(telemetryTopic,
                Encoding.UTF8.GetBytes($"{{ {instrumentName}: {value}}}"),
                null,
                null,
                MqttQoSLevel.AtLeastOnce,
                false);
            Trace(string.Format("{0} [MQTT Client] Sent telemetry {{ {1}: {2} }}", DateTime.UtcNow.ToString("u"), instrumentName, value));

        }

        private static MqttClient SetupMqttClient()
        {
            // nanoFramework socket implementation requires a valid root CA to authenticate with a
            // this can be supplied to the caller (as it's doing on the code bellow) or the Root CA has to be stored in the certificate store
            // Root CA for Azure from here: https://github.com/Azure/azure-iot-sdk-c/blob/master/certs/certs.c

            X509Certificate azureRootCACert =
                new X509Certificate(Resource.GetString(Resource.StringResources.AzureRootCerts));

            //Create MQTT Client with default port 8883 using TLS protocol
            var mqttc = new MqttClient(
                HostName,
                8883,
                true,
                azureRootCACert,
                null,
                MqttSslProtocols.TLSv1_2);

            // event when connection has been dropped
            mqttc.ConnectionClosed += Client_ConnectionClosed;

            // handler for received messages on the subscribed topics
            mqttc.MqttMsgPublishReceived += Client_MqttMsgReceived;

            // handler for publisher
            mqttc.MqttMsgPublished += Client_MqttMsgPublished;

            // handler for subscriber 
            mqttc.MqttMsgSubscribed += Client_MqttMsgSubscribed;

            // handler for unsubscriber
            mqttc.MqttMsgUnsubscribed += client_MqttMsgUnsubscribed;

            var code = mqttc.Connect(
                DeviceId,
                string.Format("{0}/{1}/api-version=2018-06-30", HostName, DeviceId),
                GetSharedAccessSignature(null, SasKey, string.Format("{0}/devices/{1}", HostName, DeviceId),
                    new TimeSpan(24, 0, 0)),
                false,
                MqttQoSLevel.AtLeastOnce,
                false, "$iothub/twin/GET/?$rid=999",
                "Disconnected",
                false,
                60
            );

            if (mqttc.IsConnected)
            {
                Trace("subscribing to topics");
                mqttc.Subscribe(
                    new[]
                    {
                            "$iothub/methods/POST/#",
                            string.Format("devices/{0}/messages/devicebound/#", DeviceId),
                            "$iothub/twin/PATCH/properties/desired/#",
                            "$iothub/twin/res/#"
                    },
                    new[]
                    {
                            MqttQoSLevel.AtLeastOnce,
                            MqttQoSLevel.AtLeastOnce,
                            MqttQoSLevel.AtLeastOnce,
                            MqttQoSLevel.AtLeastOnce
                    }
                );


                Trace("Sending twin properties");
                mqttc.Publish(string.Format("{0}?$rid={1}", twinReportedPropertiesTopic, Guid.NewGuid()),
                    Encoding.UTF8.GetBytes("{ \"Firmware\": \"nanoFramework\"}"), null, null,
                    MqttQoSLevel.AtLeastOnce, false);


                Trace("Getting twin properties");
                mqttc.Publish(string.Format("{0}?$rid={1}", twinDesiredPropertiesTopic, Guid.NewGuid()),
                    Encoding.UTF8.GetBytes(""), null, null, MqttQoSLevel.AtLeastOnce, false);


                Trace("[MQTT Client] Start to send telemetry");
            }

            return mqttc;
        }

        private static void Client_MqttMsgReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Trace(string.Format("Message received on topic: {0}", e.Topic));
            Trace(string.Format("The message was: {0}", new string(Encoding.UTF8.GetChars(e.Message))));

            if (e.Topic.StartsWith("$iothub/twin/PATCH/properties/desired/"))
            {
                Trace("and received desired properties.");
            }
            else if (e.Topic.StartsWith("$iothub/twin/"))
            {
                if (e.Topic.IndexOf("res/400/") > 0 || e.Topic.IndexOf("res/404/") > 0 ||
                    e.Topic.IndexOf("res/500/") > 0)
                    Trace("and was in the error queue.");
                else
                    Trace("and was in the success queue.");
            }
            else if (e.Topic.StartsWith("$iothub/methods/POST/"))
            {
                Trace("and was a method.");
            }
            else if (e.Topic.StartsWith(string.Format("devices/{0}/messages/devicebound/", DeviceId)))
            {
                Trace("and was a message for the device.");
            }
            else if (e.Topic.StartsWith("$iothub/clientproxy/"))
            {
                Trace("and the device has been disconnected.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/Info"))
            {
                Trace("and was in the log message queue.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/HighlightInfo"))
            {
                Trace("and was in the Highlight info queue.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/Error"))
            {
                Trace("and was in the logmessage error queue.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/Warning"))
            {
                Trace("and was in the logmessage warning queue.");
            }
        }

        private static void Client_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            Trace(string.Format("Response from publish with message id: {0}", e.MessageId.ToString()));
        }

        private static void Client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            Trace(string.Format("Response from subscribe with message id: {0}", e.MessageId.ToString()));
        }

        private static void client_MqttMsgUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e)
        {
            Trace(string.Format("Response from unsubscribe with message id: {0}", e.MessageId.ToString()));
        }

        private static void Client_ConnectionClosed(object sender, EventArgs e)
        {
            Trace("Connection closed");
        }

        private static string GetSharedAccessSignature(string keyName, string sharedAccessKey, string resource,
            TimeSpan tokenTimeToLive)
        {
            // http://msdn.microsoft.com/en-us/library/azure/dn170477.aspx
            // the canonical Uri scheme is http because the token is not amqp specific
            // signature is computed from joined encoded request Uri string and expiry string

            var exp = DateTime.UtcNow.ToUnixTimeSeconds() + (long)tokenTimeToLive.TotalSeconds;

            var expiry = exp.ToString();
            var encodedUri = HttpUtility.UrlEncode(resource);


            var hmac = HMACSHA256.HashData(Convert.FromBase64String(sharedAccessKey),
                Encoding.UTF8.GetBytes(encodedUri + "\n" + expiry));
            var sig = Convert.ToBase64String(hmac);

            var result = "";

            if (keyName != null)
                result = string.Format(
                    "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
                    encodedUri,
                    HttpUtility.UrlEncode(sig),
                    HttpUtility.UrlEncode(expiry),
                    HttpUtility.UrlEncode(keyName));
            result = string.Format(
                "SharedAccessSignature sr={0}&sig={1}&se={2}",
                encodedUri,
                HttpUtility.UrlEncode(sig),
                HttpUtility.UrlEncode(expiry));

            return result;
        }


        private static Shtc3 SetupSensor()
        {
            Configuration.SetPinFunction(21, DeviceFunction.I2C1_DATA);
            Configuration.SetPinFunction(22, DeviceFunction.I2C1_CLOCK);

            I2cConnectionSettings settings = new I2cConnectionSettings(1, Shtc3.DefaultI2cAddress);
            I2cDevice device = I2cDevice.Create(settings);

            Shtc3 sensor = new Shtc3(device);
            return sensor;
        }

        //[Conditional("DEBUG")]
        static void Trace(string message)
        {
            Debug.WriteLine(message);
        }
    }
}
