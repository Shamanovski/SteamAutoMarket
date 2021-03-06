﻿namespace SteamAutoMarket.UI.Pages
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Windows;

    using SteamAutoMarket.Core;
    using SteamAutoMarket.Properties;
    using SteamAutoMarket.UI.Repository.Context;
    using SteamAutoMarket.UI.Repository.Settings;
    using SteamAutoMarket.UI.Utils.Logger;

    /// <summary>
    /// Interaction logic for Logs.xaml
    /// </summary>
    public partial class LogsWindow : INotifyPropertyChanged
    {
        private static string logs;

        public LogsWindow()
        {
            this.InitializeComponent();
            this.DataContext = this;
            UiGlobalVariables.LogsWindow = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static string GlobalLogs
        {
            get => logs;
            set
            {
                if (UiGlobalVariables.LogsWindow != null)
                {
                    UiGlobalVariables.LogsWindow.Logs = value;
                }
                else
                {
                    logs = value;
                }
            }
        }

        public string Logs
        {
            get => logs;
            set
            {
                logs = value;
                this.OnPropertyChanged();
                if (this.ScrollLogsToEnd)
                {
                    Application.Current.Dispatcher.Invoke(() => this.LogTextBox.ScrollToEnd());
                }
            }
        }

        public bool ScrollLogsToEnd
        {
            get => SettingsProvider.GetInstance().ScrollLogsToEnd;
            set
            {
                SettingsProvider.GetInstance().ScrollLogsToEnd = value;
                this.OnPropertyChanged();
            }
        }

        public string SelectedLoggerLevel
        {
            get => SettingsProvider.GetInstance().LoggerLevel;
            set
            {
                SettingsProvider.GetInstance().LoggerLevel = value;
                Logger.UpdateLoggerLevel(value);
                this.OnPropertyChanged();
            }
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void OpenLogFileButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (File.Exists(Logger.LogFilePath) == false)
            {
                ErrorNotify.CriticalMessageBox($"{Logger.LogFilePath} directory not found");
                return;
            }

            Process.Start(Logger.LogFilePath);
        }
    }
}