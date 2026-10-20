using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorSwitch
{
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MonitorSwitch";

        internal static string Command(string executable)
        {
            return "\"" + Path.GetFullPath(executable) + "\" --tray";
        }

        internal static bool IsEnabled(RegistryKey key, string executable)
        {
            return key != null && String.Equals(key.GetValue(ValueName) as string, Command(executable), StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetEnabled(RegistryKey key, string executable, bool enabled)
        {
            if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
        }

        public static bool Enabled
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey)) return IsEnabled(key, Application.ExecutablePath); }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                    SetEnabled(key, Application.ExecutablePath, value);
            }
        }
    }
}
