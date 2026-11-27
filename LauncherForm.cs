using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BlackOut
{
    public class LauncherForm : Form
    {
        internal const string ElevatedBlackoutArgument = "--start-blackout";
        private readonly Button initiateButton;
        private readonly Button bluetoothButton;
        private readonly CheckedListBox bluetoothDevices;
        private readonly ToolStripDropDown bluetoothMenu;
        private readonly BluetoothController bluetooth = new();

        public LauncherForm()
        {
            Text = "Black Out";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 450);

            BackColor = Color.FromArgb(20, 20, 20);
            ForeColor = Color.White;

            initiateButton = new Button
            {
                Text = "Initiate Blackout",
                Size = new Size(380, 82),
                Location = new Point(130, 218),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };

            initiateButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            initiateButton.FlatAppearance.BorderSize = 10;

            initiateButton.Click += InitiateButton_Click;

            bluetoothButton = new Button
            {
                Text = "Bluetooth devices ▾",
                Size = new Size(380, 42),
                Location = new Point(130, 160),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            bluetoothDevices = new CheckedListBox
            {
                CheckOnClick = true,
                HorizontalScrollbar = true,
                IntegralHeight = false,
                Size = new Size(378, 144),
                BackColor = BackColor,
                ForeColor = ForeColor,
                AccessibleName = "Bluetooth devices to disconnect during blackout"
            };
            bluetoothMenu = new ToolStripDropDown { Padding = Padding.Empty };
            bluetoothMenu.Items.Add(new ToolStripControlHost(bluetoothDevices)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty
            });
            bluetoothButton.Click += OpenBluetoothMenu;
            bluetoothMenu.Closed += (_, _) => UpdateBluetoothButton();

            Controls.Add(bluetoothButton);
            Controls.Add(initiateButton);
        }

        private void OpenBluetoothMenu(object? sender, EventArgs e)
        {
            var selected = bluetoothDevices.CheckedItems.Cast<BluetoothController.Device>()
                .Select(device => device.Key).ToHashSet();
            var devices = RefreshBluetoothDevices(selected);
            if (devices == null)
                return;

            if (devices.Length == 0)
            {
                MessageBox.Show(this, "Windows reported no configured Bluetooth devices.",
                    "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bluetoothMenu.Show(bluetoothButton, new Point(0, bluetoothButton.Height));
            bluetoothDevices.Focus();
        }

        private BluetoothController.Device[]? RefreshBluetoothDevices(HashSet<string> selected)
        {
            BluetoothController.Device[] devices;
            try
            {
                devices = BluetoothController.GetDevices();
            }
            catch (System.Management.ManagementException ex)
            {
                MessageBox.Show(this, $"Windows could not list Bluetooth devices. {ex.Message}",
                    "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            bluetoothDevices.Items.Clear();
            foreach (var device in devices)
                bluetoothDevices.Items.Add(device, selected.Contains(device.Key));
            UpdateBluetoothButton();

            return devices;
        }

        private void UpdateBluetoothButton()
        {
            int count = bluetoothDevices.CheckedItems.Count;
            bluetoothButton.Text = count == 0 ? "Bluetooth devices ▾" : $"Bluetooth devices ({count} selected) ▾";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                bluetoothMenu.Dispose();
            base.Dispose(disposing);
        }

        private void InitiateButton_Click(object? sender, EventArgs e)
        {
            var selected = bluetoothDevices.CheckedItems.Cast<BluetoothController.Device>().ToArray();
            if (selected.Length > 0 && !BluetoothController.IsElevated())
            {
                var startInfo = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                startInfo.ArgumentList.Add(ElevatedBlackoutArgument);
                foreach (var device in selected)
                    startInfo.ArgumentList.Add(device.Key);
                try
                {
                    using var process = Process.Start(startInfo);
                    Close();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // The launcher and its selections remain available after UAC is canceled.
                }
                catch (Win32Exception ex)
                {
                    MessageBox.Show(this, $"Windows could not start BlackOut as administrator. {ex.Message}",
                        "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            StartBlackout(selected);
        }

        internal void ResumeElevatedBlackout(string[] deviceKeys)
        {
            if (!BluetoothController.IsElevated() || RefreshBluetoothDevices(deviceKeys.ToHashSet()) == null)
                return;

            var selected = bluetoothDevices.CheckedItems.Cast<BluetoothController.Device>().ToArray();
            if (selected.Length != deviceKeys.Length)
            {
                MessageBox.Show(this, "The Bluetooth device list changed. Review your selections and start again.",
                    "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            StartBlackout(selected);
        }

        private void StartBlackout(BluetoothController.Device[] selected)
        {
            DisplayController? displays = null;
            BlackoutForm? blackout = null;
            string stage = "disconnect extended displays";
            try
            {
                displays = new DisplayController();
                displays.DisconnectExtendedDisplays(verify: true);

                stage = "show the blackout screen";
                blackout = new BlackoutForm(displays, bluetooth)
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = Screen.PrimaryScreen?.Bounds ?? Bounds
                };
                blackout.Show();
                blackout.Update();

                stage = "disconnect selected Bluetooth devices";
                bluetooth.Disconnect(selected);
                stage = "verify the display layout";
                displays.DisconnectExtendedDisplays(verify: true);

                Hide();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                blackout?.Dispose();
                var errors = new List<string> { ex.Message };
                errors.AddRange(bluetooth.Restore());
                try
                {
                    displays?.Restore();
                }
                catch (Win32Exception restoreError)
                {
                    errors.Add($"Display restoration failed. {restoreError.Message}");
                }
                MessageBox.Show(this, $"BlackOut could not {stage}. {string.Join(Environment.NewLine, errors)}",
                    "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            blackout.DimBrightness();
        }
    }
}
