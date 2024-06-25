using nanoFramework.M2Mqtt;
using nanoFramework.M2Mqtt.Messages;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System;
using System.Security.Cryptography;
using System.Web;

namespace NanoframeworkExampleCFNet.Infrastructure
{
    public class MqttManager
    {
        private static readonly string HostName = Resource.GetString(Resource.StringResources.HostName);
        private static readonly string DeviceId = Resource.GetString(Resource.StringResources.DeviceID);
        private static readonly string TelemetryTopic = $"devices/{DeviceId}/messages/events/";

        private const string TwinReportedPropertiesTopic = "$iothub/twin/PATCH/properties/reported/";
        private const string TwinDesiredPropertiesTopic = "$iothub/twin/GET/";
        private static readonly string SasKey = Resource.GetString(Resource.StringResources.SasKey);

        private MqttClient _mqttClient;

        public bool IsConnected()
        {
            return _mqttClient is { IsConnected: true };
        }

        public void Connect()
        {
            // nanoFramework socket implementation requires a valid root CA to authenticate with a
            // this can be supplied to the caller (as it's doing on the code bellow) or the Root CA has to be stored in the certificate store
            // Root CA for Azure from here: https://github.com/Azure/azure-iot-sdk-c/blob/master/certs/certs.c

            //Connect MQTT MqttClient with default port 8883 using TLS protocol
            _mqttClient = new MqttClient(
                HostName,
                8883,
                true,
                new X509Certificate(Resource.GetString(Resource.StringResources.AzureRootCerts)),
                null,
                MqttSslProtocols.TLSv1_2);

            // handler for received messages on the subscribed topics
            _mqttClient.MqttMsgPublishReceived += Client_MqttMsgReceived;

            // event when connection has been dropped
            _mqttClient.ConnectionClosed += (_, _) => Logger.Log("Connection closed");

            // handler for publisher
            _mqttClient.MqttMsgPublished += (_, eventArgs) =>
                Logger.Log($"Response from publish with message id: {eventArgs.MessageId.ToString()}");

            // handler for subscriber 
            _mqttClient.MqttMsgSubscribed += (_, eventArgs) =>
                Logger.Log($"Response from subscribe with message id: {eventArgs.MessageId.ToString()}");

            // handler for unsubscriber
            _mqttClient.MqttMsgUnsubscribed += (_, eventArgs) =>
                Logger.Log($"Response from unsubscribe with message id: {eventArgs.MessageId.ToString()}");

            _mqttClient.Connect(
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

            if (_mqttClient.IsConnected)
            {
                Logger.Log("subscribing to topics");
                _mqttClient.Subscribe(
                    new[]
                    {
                        "$iothub/methods/POST/#",
                        $"devices/{DeviceId}/messages/devicebound/#",
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

                Logger.Log("[MQTT MqttClient] Start to send telemetry");
            }
        }

        public void PublishTelemetry(string instrumentName, string instrumentValue)
        {
            //Publish telemetry data using AT LEAST ONCE QOS Level
            var message = $"{{ {instrumentName}: {instrumentValue} }}";
            _mqttClient.Publish(TelemetryTopic,
                Encoding.UTF8.GetBytes(message),
                null,
                null,
                MqttQoSLevel.AtLeastOnce,
                false);

            Logger.Log($"[MQTT MqttClient] Sent telemetry {message}");
        }

        private static void Client_MqttMsgReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Logger.Log($"Message received on topic: {e.Topic}");
            Logger.Log($"The message was: {new string(Encoding.UTF8.GetChars(e.Message))}");

            if (e.Topic.StartsWith("$iothub/twin/PATCH/properties/desired/"))
            {
                Logger.Log("and received desired properties.");
            }
            else if (e.Topic.StartsWith("$iothub/twin/"))
            {
                if (e.Topic.IndexOf("res/400/") > 0 || e.Topic.IndexOf("res/404/") > 0 ||
                    e.Topic.IndexOf("res/500/") > 0)
                    Logger.Log("and was in the error queue.");
                else
                    Logger.Log("and was in the success queue.");
            }
            else if (e.Topic.StartsWith("$iothub/methods/POST/"))
            {
                Logger.Log("and was a method.");
            }
            else if (e.Topic.StartsWith($"devices/{DeviceId}/messages/devicebound/"))
            {
                Logger.Log("and was a message for the device.");
            }
            else if (e.Topic.StartsWith("$iothub/clientproxy/"))
            {
                Logger.Log("and the device has been disconnected.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/Info"))
            {
                Logger.Log("and was in the log message queue.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/HighlightInfo"))
            {
                Logger.Log("and was in the Highlight info queue.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/Error"))
            {
                Logger.Log("and was in the logmessage error queue.");
            }
            else if (e.Topic.StartsWith("$iothub/logmessage/Warning"))
            {
                Logger.Log("and was in the logmessage warning queue.");
            }
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

    }
}