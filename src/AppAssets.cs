using System.Drawing;
using System.Reflection;

namespace MonitorSwitch
{
    internal static class AppAssets
    {
        public static readonly Icon Icon = Load();
        public static readonly Icon TrayIcon = new Icon(Icon, new Size(16, 16));
        private static Icon Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorSwitch.Icon"))
            using (var icon = new Icon(stream, new Size(48, 48))) return (Icon)icon.Clone();
        }
    }
}
