using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlackOut
{
    internal sealed class DisplayController
    {
        private const uint OnlyActivePaths = 2;
        private const uint UseSuppliedConfig = 0x20;
        private const uint ApplyConfig = 0x80;
        private const int InsufficientBuffer = 122;
        private readonly Configuration original;
        private readonly Configuration primaryOnly;
        private bool changed;

        public DisplayController()
        {
            original = ReadActiveConfiguration();
            // Windows places the primary desktop source at the origin.
            var primaryPath = Array.Find(original.Paths, path =>
                path.Source.ModeIndex < original.Modes.Length &&
                original.Modes[path.Source.ModeIndex].SourceX == 0 &&
                original.Modes[path.Source.ModeIndex].SourceY == 0);

            if (primaryPath.Flags == 0)
                throw new InvalidOperationException("Windows did not report an active primary display.");

            var modes = new List<DisplayMode>();
            var indexes = new Dictionary<uint, uint>();
            uint CopyMode(uint index)
            {
                if (index == uint.MaxValue)
                    return index;
                if (!indexes.TryGetValue(index, out uint copyIndex))
                {
                    copyIndex = (uint)modes.Count;
                    indexes.Add(index, copyIndex);
                    modes.Add(original.Modes[index]);
                }
                return copyIndex;
            }

            var paths = new List<DisplayPath>();
            foreach (var originalPath in original.Paths)
            {
                if (originalPath.Source.Adapter != primaryPath.Source.Adapter ||
                    originalPath.Source.Id != primaryPath.Source.Id)
                    continue;

                var path = originalPath;
                path.Source.ModeIndex = CopyMode(path.Source.ModeIndex);
                path.Target.ModeIndex = CopyMode(path.Target.ModeIndex);
                paths.Add(path);
            }
            primaryOnly = new Configuration(paths.ToArray(), modes.ToArray());
        }

        public void DisconnectExtendedDisplays(bool verify = false)
        {
            if (HasExtendedDisplays())
            {
                primaryOnly.Apply();
                changed = true;
                if (verify && HasExtendedDisplays())
                    throw new InvalidOperationException("Windows still reports an active extended display.");
            }
        }

        private bool HasExtendedDisplays() => ReadActiveConfiguration().Paths.Any(path =>
            !primaryOnly.Paths.Any(allowed =>
                path.Target.Adapter == allowed.Target.Adapter && path.Target.Id == allowed.Target.Id &&
                path.Source.Adapter == allowed.Source.Adapter && path.Source.Id == allowed.Source.Id));

        public void Restore()
        {
            if (changed)
            {
                original.Apply();
                changed = false;
            }
        }

        private static Configuration ReadActiveConfiguration()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Check(GetDisplayConfigBufferSizes(OnlyActivePaths, out uint pathCount, out uint modeCount));
                var paths = new DisplayPath[pathCount];
                var modes = new DisplayMode[modeCount];
                int result = QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths,
                    ref modeCount, modes, IntPtr.Zero);
                if (result == InsufficientBuffer)
                    continue;

                Check(result);
                Array.Resize(ref paths, (int)pathCount);
                Array.Resize(ref modes, (int)modeCount);
                return new Configuration(paths, modes);
            }
            throw new Win32Exception(InsufficientBuffer);
        }

        private static void Check(int result)
        {
            if (result != 0)
                throw new Win32Exception(result);
        }

        private sealed record Configuration(DisplayPath[] Paths, DisplayMode[] Modes)
        {
            public void Apply() => Check(SetDisplayConfig((uint)Paths.Length, Paths,
                (uint)Modes.Length, Modes, UseSuppliedConfig | ApplyConfig));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayPath
        {
            public PathEndpoint Source;
            public PathTarget Target;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PathEndpoint
        {
            public long Adapter;
            public uint Id;
            public uint ModeIndex;
            public uint StatusFlags;
        }

        // Keep the full native target record so its timing and scaling survive a round trip.
        [StructLayout(LayoutKind.Explicit, Size = 48, Pack = 4)]
        private struct PathTarget
        {
            [FieldOffset(0)] public long Adapter;
            [FieldOffset(8)] public uint Id;
            [FieldOffset(12)] public uint ModeIndex;
        }

        // The native mode union includes a 48-byte target mode. For a source
        // mode, its desktop position starts 12 bytes into the union.
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct DisplayMode
        {
            [FieldOffset(28)] public int SourceX;
            [FieldOffset(32)] public int SourceY;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayPath[] paths,
            ref uint modeCount, [Out] DisplayMode[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint pathCount, [In] DisplayPath[] paths,
            uint modeCount, [In] DisplayMode[] modes, uint flags);
    }
}
