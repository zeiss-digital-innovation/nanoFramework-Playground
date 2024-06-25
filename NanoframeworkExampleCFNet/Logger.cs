using System.Diagnostics;
using System;

namespace NanoframeworkExampleCFNet
{
    public class Logger
    {
        public static void Log(string message)
        {
            Debug.WriteLine($"{DateTime.UtcNow.ToString("u")} - {message}");
        }
    }
}