using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlackOut
{
    public class LauncherForm : Form
    {
        private readonly Button initiateButton;

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
                Location = new Point((340 - 180) / 2, (150 - 42) / 2),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };

            initiateButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            initiateButton.FlatAppearance.BorderSize = 10;

            initiateButton.Click += InitiateButton_Click;

            Controls.Add(initiateButton);
        }

        private void InitiateButton_Click(object? sender, EventArgs e)
        {
            DisplayController displays;
            try
            {
                displays = new DisplayController();
                displays.DisconnectExtendedDisplays();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, $"Blackout could not disconnect extended displays. {ex.Message}",
                    "Black Out", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            BrightnessController.SetLaptopBrightness(1);
            Hide();
            var blackout = new BlackoutForm(displays)
            {
                StartPosition = FormStartPosition.Manual,
                Bounds = Screen.PrimaryScreen!.Bounds
            };
            blackout.Show();
        }
    }
}
