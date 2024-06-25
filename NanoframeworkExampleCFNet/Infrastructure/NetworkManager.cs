using nanoFramework.Networking;
using System.Diagnostics;
using System.Threading;
using System;

namespace NanoframeworkExampleCFNet.Infrastructure
{
    public static class NetworkManager
    {
        private static readonly string Ssid = Resource.GetString(Resource.StringResources.SSID);
        private static readonly string WifiPassword = Resource.GetString(Resource.StringResources.WifiPassword);

        public static void ConnectToNetwork()
        {
            try
            {
                Logger.Log("Waiting for network up and IP address...");
                CancellationTokenSource cs = new(60000);
                var success =
                    WifiNetworkHelper.ConnectDhcp(Ssid, WifiPassword, requiresDateTime: true, token: cs.Token);

                if (!success)
                {
                    Debug.WriteLine($"Can't get a proper IP address and DateTime, error: {WifiNetworkHelper.Status}.");
                    if (WifiNetworkHelper.HelperException != null)
                        Debug.WriteLine($"Exception: {WifiNetworkHelper.HelperException}");
                }
            }


            catch (Exception e)
            {
                Logger.Log($"[Network] setup failed. {e.Message}");
            }
        }
    }
}