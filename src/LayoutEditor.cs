using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonitorSwitch
{
    internal static class LayoutEditor
    {
        public static void Resolution(string text, out uint width, out uint height)
        {
            string[] parts = (text ?? "").ToLowerInvariant().Replace('×', 'x').Split('x');
            width = height = 0;
            if (parts.Length != 2 || !UInt32.TryParse(parts[0].Trim(), out width) || !UInt32.TryParse(parts[1].Trim(), out height) || width < 320 || width > 32768 || height < 200 || height > 32768)
                throw new InvalidDataException("Inserisci una risoluzione valida, per esempio 1920 x 1080.");
        }
        public static List<MonitorSetting> Build(int mode, List<MonitorSetting> selections)
        {
            var result = selections.Select(s => s.Copy()).ToList();
            if (result.Count == 0) throw new InvalidDataException("Seleziona almeno un monitor.");
            int x = 0, i = 0;
            foreach (MonitorSetting setting in result)
            {
                if (mode == 2) { setting.Group = 0; setting.X = 0; setting.Y = 0; }
                else if (mode != 0) { setting.Group = i++; setting.X = x; setting.Y = 0; x += (int)setting.Width; }
            }
            if (mode == 0 && !result.Any(s => s.X == 0 && s.Y == 0))
            {
                int dx = result[0].X, dy = result[0].Y;
                foreach (MonitorSetting setting in result) { setting.X -= dx; setting.Y -= dy; }
            }
            Profile.CheckLayout(result); return result;
        }
    }
}
