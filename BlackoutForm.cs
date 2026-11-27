using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlackOut
{
    public class BlackoutForm : Form
    {
        private readonly DisplayController displays;
        private readonly BluetoothController bluetooth;
        private readonly System.Windows.Forms.Timer displayTimer;

        private TextBox? exitTextBox;
        private Button? exitButton;
        private Panel? exitPanel;
        private CheckBox? blockToggle;
        private KeyboardBlocker? keyboardBlocker;
        private Task? dimBrightnessTask;
        private bool exiting;

        internal void DimBrightness()
        {
            dimBrightnessTask = Task.Run(() => BrightnessController.SetLaptopBrightness(1));
        }

        internal BlackoutForm(DisplayController displays, BluetoothController bluetooth)
        {
            this.displays = displays;
            this.bluetooth = bluetooth;
            displayTimer = new System.Windows.Forms.Timer { Interval = 500 };
            displayTimer.Tick += MaintainDisplayConfiguration;

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            BackColor = Color.Black;
            ShowInTaskbar = false;
            ControlBox = false;
            KeyPreview = true;

            BuildExitUi();
            Shown += (_, _) => displayTimer.Start();
        }

        private void MaintainDisplayConfiguration(object? sender, EventArgs e)
        {
            try
            {
                displays.DisconnectExtendedDisplays();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows may be reconfiguring its display paths. Retry on the next tick.
            }

            if (Screen.PrimaryScreen is { } primary && Bounds != primary.Bounds)
                Bounds = primary.Bounds;
        }

        private void BuildExitUi()
        {
            exitPanel = new Panel
            {
                Size = new Size(420, 210),
                BackColor = Color.FromArgb(18, 18, 18)
            };

            exitTextBox = new TextBox
            {
                Size = new Size(380, 30),
                Location = new Point(20, 20),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            exitButton = new Button
            {
                Text = "Exit",
                Size = new Size(380, 70),
                Location = new Point(20, 60),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };

            exitButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            exitButton.FlatAppearance.BorderSize = 1;

            exitButton.Click += ExitButton_Click;
            exitTextBox.KeyDown += ExitTextBox_KeyDown;

            blockToggle = new CheckBox
            {
                Text = "Block",
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 44),
                Location = new Point(20, 145),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                AccessibleName = "Block all keyboard input"
            };
            blockToggle.CheckedChanged += BlockToggle_CheckedChanged;

            exitPanel.Controls.Add(exitTextBox);
            exitPanel.Controls.Add(exitButton);
            exitPanel.Controls.Add(blockToggle);

            Controls.Add(exitPanel);

            void CenterExitPanel()
            {
                exitPanel.Location = new Point(
                    (ClientSize.Width - exitPanel.Width) / 2,
                    (ClientSize.Height - exitPanel.Height) / 2
                );
            }

            Resize += (_, _) => CenterExitPanel();
            Shown += (_, _) =>
            {
                CenterExitPanel();
                exitTextBox.Focus();
            };
        }

        private void BlockToggle_CheckedChanged(object? sender, EventArgs e)
        {
            if (blockToggle == null || exitTextBox == null)
                return;

            if (blockToggle.Checked)
            {
                try
                {
                    keyboardBlocker = new KeyboardBlocker();
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    blockToggle.Checked = false;
                    MessageBox.Show(this, $"Keyboard blocking could not be enabled: {ex.Message}",
                        "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                keyboardBlocker?.Dispose();
                keyboardBlocker = null;
            }

            blockToggle.BackColor = blockToggle.Checked
                ? Color.FromArgb(65, 95, 65)
                : Color.FromArgb(35, 35, 35);
            exitTextBox.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                displayTimer.Dispose();
                keyboardBlocker?.Dispose();
                keyboardBlocker = null;
            }
            base.Dispose(disposing);
        }

        private void ExitTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ExitButton_Click(sender, EventArgs.Empty);
            }
        }

        private async void ExitButton_Click(object? sender, EventArgs e)
        {
            if (exiting)
                return;

            if (exitTextBox != null &&
                string.Equals(exitTextBox.Text.Trim(), "EXIT", StringComparison.OrdinalIgnoreCase))
            {
                exiting = true;
                displayTimer.Stop();
                keyboardBlocker?.Dispose();
                keyboardBlocker = null;
                if (dimBrightnessTask is { } pendingBrightness)
                    await pendingBrightness;
                await Task.Run(() => BrightnessController.SetLaptopBrightness(100));

                var errors = new List<string>(bluetooth.Restore());
                try
                {
                    displays.Restore();
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    errors.Add($"Windows could not restore the previous display layout. {ex.Message}");
                }
                if (errors.Count > 0)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors),
                        "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                Application.Exit();
            }
            else
            {
                if (exitTextBox != null)
                {
                    exitTextBox.Clear();
                    exitTextBox.Focus();
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4) || keyData == Keys.Escape)
            {
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
