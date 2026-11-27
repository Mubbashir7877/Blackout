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
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT * FROM WmiMonitorBrightnessMethods"
                );

                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using (obj)
                        obj.InvokeMethod("WmiSetBrightness", new object[] { 1u, brightness });
                }
            }
            catch
            {
                // Some desktops/external monitors will not support this path.
            }
        }
    }
}
