﻿namespace AutoUpdater
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    using Microsoft.Win32;

    internal partial class UpdateForm : Form
    {
        public UpdateForm()
        {
            InitializeComponent();
            this.UseLatestIE();
            buttonSkip.Visible = AutoUpdater.ShowSkipButton;
            buttonRemindLater.Visible = AutoUpdater.ShowRemindLaterButton;
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(UpdateForm));
            this.Text = string.Format(
                resources.GetString("$this.Text", CultureInfo.CurrentCulture),
                AutoUpdater.AppTitle,
                AutoUpdater.CurrentVersion);
            labelUpdate.Text = string.Format(
                resources.GetString("labelUpdate.Text", CultureInfo.CurrentCulture),
                AutoUpdater.AppTitle);
            labelDescription.Text = string.Format(
                resources.GetString("labelDescription.Text", CultureInfo.CurrentCulture),
                AutoUpdater.AppTitle,
                AutoUpdater.CurrentVersion,
                AutoUpdater.InstalledVersion);
            if (string.IsNullOrEmpty(AutoUpdater.ChangelogURL))
            {
                this.HideReleaseNotes = true;
                var reduceHeight = labelReleaseNotes.Height + webBrowser.Height;
                labelReleaseNotes.Hide();
                webBrowser.Hide();

                this.Height -= reduceHeight;

                buttonSkip.Location = new Point(buttonSkip.Location.X, buttonSkip.Location.Y - reduceHeight);
                buttonRemindLater.Location = new Point(
                    buttonRemindLater.Location.X,
                    buttonRemindLater.Location.Y - reduceHeight);
                buttonUpdate.Location = new Point(buttonUpdate.Location.X, buttonUpdate.Location.Y - reduceHeight);
            }
        }

        public sealed override string Text
        {
            get
            {
                return base.Text;
            }
            set
            {
                base.Text = value;
            }
        }

        private bool HideReleaseNotes { get; set; }

        private void ButtonRemindLaterClick(object sender, EventArgs e)
        {
            if (AutoUpdater.LetUserSelectRemindLater)
            {
                var remindLaterForm = new RemindLaterForm();

                var dialogResult = remindLaterForm.ShowDialog();

                if (dialogResult.Equals(DialogResult.OK))
                {
                    AutoUpdater.RemindLaterTimeSpan = remindLaterForm.RemindLaterFormat;
                    AutoUpdater.RemindLaterAt = remindLaterForm.RemindLaterAt;
                }
                else if (dialogResult.Equals(DialogResult.Abort))
                {
                    this.ButtonUpdateClick(sender, e);
                    return;
                }
                else
                {
                    return;
                }
            }

            using (var updateKey = Registry.CurrentUser.CreateSubKey(AutoUpdater.RegistryLocation))
            {
                if (updateKey != null)
                {
                    updateKey.SetValue("version", AutoUpdater.CurrentVersion);
                    updateKey.SetValue("skip", 0);
                    var remindLaterDateTime = DateTime.Now;
                    switch (AutoUpdater.RemindLaterTimeSpan)
                    {
                        case RemindLaterFormat.Days:
                            remindLaterDateTime = DateTime.Now + TimeSpan.FromDays(AutoUpdater.RemindLaterAt);
                            break;
                        case RemindLaterFormat.Hours:
                            remindLaterDateTime = DateTime.Now + TimeSpan.FromHours(AutoUpdater.RemindLaterAt);
                            break;
                        case RemindLaterFormat.Minutes:
                            remindLaterDateTime = DateTime.Now + TimeSpan.FromMinutes(AutoUpdater.RemindLaterAt);
                            break;
                    }

                    updateKey.SetValue(
                        "remindlater",
                        remindLaterDateTime.ToString(CultureInfo.CreateSpecificCulture("en-US").DateTimeFormat));
                    AutoUpdater.SetTimer(remindLaterDateTime);
                }
            }

            this.DialogResult = DialogResult.Cancel;
        }

        private void ButtonSkipClick(object sender, EventArgs e)
        {
            using (var updateKey = Registry.CurrentUser.CreateSubKey(AutoUpdater.RegistryLocation))
            {
                if (updateKey != null)
                {
                    updateKey.SetValue("version", AutoUpdater.CurrentVersion.ToString());
                    updateKey.SetValue("skip", 1);
                }
            }
        }

        private void ButtonUpdateClick(object sender, EventArgs e)
        {
            if (AutoUpdater.OpenDownloadPage)
            {
                var processStartInfo = new ProcessStartInfo(AutoUpdater.DownloadURL);

                Process.Start(processStartInfo);

                this.DialogResult = DialogResult.OK;
            }
            else
            {
                if (AutoUpdater.DownloadUpdate())
                {
                    this.DialogResult = DialogResult.OK;
                }
            }
        }

        private void UpdateForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            AutoUpdater.Running = false;
        }

        private void UpdateFormLoad(object sender, EventArgs e)
        {
            if (!this.HideReleaseNotes)
            {
                webBrowser.Navigate(AutoUpdater.ChangelogURL);
            }
        }

        private void UseLatestIE()
        {
            var ieValue = 0;
            switch (webBrowser.Version.Major)
            {
                case 11:
                    ieValue = 11001;
                    break;
                case 10:
                    ieValue = 10001;
                    break;
                case 9:
                    ieValue = 9999;
                    break;
                case 8:
                    ieValue = 8888;
                    break;
                case 7:
                    ieValue = 7000;
                    break;
            }

            if (ieValue != 0)
            {
                using (var registryKey = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION",
                    true))
                {
                    registryKey?.SetValue(
                        Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName),
                        ieValue,
                        RegistryValueKind.DWord);
                }
            }
        }
    }
}