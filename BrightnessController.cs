using System;
using System.Management;

namespace BlackOut
{
    public static class BrightnessController
    {
        public static void SetLaptopBrightness(byte brightness)
        {
            try
            {
                brightness = Math.Max((byte)1, brightness);

                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT * FROM WmiMonitorBrightnessMethods"
                );

                foreach (ManagementObject obj in searcher.Get())
                {
                    obj.InvokeMethod("WmiSetBrightness", new object[] { 1, brightness });
                }
            }
            catch
            {
                // Some desktops/external monitors will not support this path.
            }
        }
    }
} 