using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace BlackOut
{
    internal sealed class BluetoothController
    {
        private const uint DisabledProblem = 22;
        private const uint DisableWithoutSystemDialog = 4;
        private static readonly DevicePropertyKey ContainerId = new(
            new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
        private static readonly Regex RemoteDeviceId = new(
            @"^BTH(?:ENUM|LE)\\DEV_([0-9A-F]{12})\\", RegexOptions.IgnoreCase);
        private readonly List<(string Name, string InstanceId)> disabled = new();

        internal sealed record Device(string Key, string Address, string Name, string[] InstanceIds)
        {
            public override string ToString() => $"{Name} ({Address})";
        }

        public static Device[] GetDevices()
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE ClassGuid = '{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}'");
            using var results = searcher.Get();
            var endpoints = new List<(string Key, string Address, string Name, string InstanceId)>();
            foreach (ManagementObject entry in results)
            {
                using (entry)
                {
                    if (entry["PNPDeviceID"] is not string id)
                        continue;
                    var match = RemoteDeviceId.Match(id);
                    if (!match.Success)
                        continue;
                    string address = match.Groups[1].Value.ToUpperInvariant();
                    endpoints.Add((GetDeviceKey(id, address), address,
                        entry["Name"] as string ?? "Bluetooth device", id));
                }
            }

            return endpoints.GroupBy(endpoint => endpoint.Key)
                .Select(group => new Device(group.Key, group.First().Address, group.First().Name,
                    group.Select(endpoint => endpoint.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.Address)
                .ToArray();
        }

        private static string GetDeviceKey(string instanceId, string address)
        {
            uint size = 16;
            if (CM_Locate_DevNode(out uint node, instanceId, 0) == 0 &&
                CM_Get_DevNode_Property(node, in ContainerId, out uint type, out Guid container, ref size, 0) == 0 &&
                type == 13 && container != Guid.Empty && container != new Guid("00000000-0000-0000-ffff-ffffffffffff"))
                return container.ToString();

            return address;
        }

        public void Disconnect(IEnumerable<Device> devices)
        {
            foreach (var device in devices)
            {
                foreach (string id in device.InstanceIds)
                {
                    Check(CM_Locate_DevNode(out uint node, id, 0), device.Name);
                    Check(CM_Get_DevNode_Status(out _, out uint problem, node, 0), device.Name);
                    if (problem == DisabledProblem)
                        continue;

                    Check(CM_Disable_DevNode(node, DisableWithoutSystemDialog), device.Name);
                    disabled.Add((device.Name, id));
                }
            }
        }

        public string[] Restore()
        {
            var errors = new List<string>();
            for (int index = disabled.Count - 1; index >= 0; index--)
            {
                var (name, id) = disabled[index];
                try
                {
                    Check(CM_Locate_DevNode(out uint node, id, 0), name);
                    Check(CM_Enable_DevNode(node, 0), name);
                    disabled.RemoveAt(index);
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add(ex.Message);
                }
            }
            return errors.ToArray();
        }

        public static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void Check(uint result, string name)
        {
            if (result != 0)
                throw new InvalidOperationException($"{name}: {new Win32Exception((int)CM_MapCrToWin32Err(result, 31)).Message}");
        }

        [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint CM_Locate_DevNode(out uint node, string instanceId, uint flags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct DevicePropertyKey(Guid Format, uint Id);

        [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW", ExactSpelling = true)]
        private static extern uint CM_Get_DevNode_Property(uint node, in DevicePropertyKey key, out uint type,
            out Guid value, ref uint size, uint flags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        private static extern uint CM_Disable_DevNode(uint node, uint flags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        private static extern uint CM_Enable_DevNode(uint node, uint flags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        private static extern uint CM_MapCrToWin32Err(uint result, uint defaultError);
    }
}
