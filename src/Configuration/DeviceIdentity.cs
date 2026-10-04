using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SyncPlayer;

static class DeviceIdentity
{
    const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncPlayer", "settings.json");
    public static string ShortTag(string identity, int length = 6)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var text = new StringBuilder(); int bits = 0; uint buffer = 0;
        foreach (byte value in hash) {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5 && text.Length < length) { bits -= 5; text.Append(Alphabet[(int)(buffer >> bits & 31)]); }
            if (text.Length == length) break;
        }
        return text.ToString();
    }
    public static string DefaultName
    {
        get {
            string machine = Environment.MachineName;
            try { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"); machine += "|" + key?.GetValue("MachineGuid"); } catch { machine += "|" + Environment.UserName; }
            return "PC-" + ShortTag(machine);
        }
    }
    public static string Validate(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 32 || name.Any(char.IsControl)) throw new ArgumentException(Localization.T("设备名称须为 1–32 个字符"));
        return name;
    }
    public static string LoadName(string? path = null)
    {
        try { var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path ?? SettingsPath)); if (saved?.TryGetValue("name", out var name) == true) return Validate(name); } catch { }
        return DefaultName;
    }
    public static void SaveName(string name, string? settingsPath = null) => SaveSetting("name", Validate(name), settingsPath);
    public static string? LoadSetting(string key, string? path = null)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path ?? SettingsPath))?.GetValueOrDefault(key); } catch { return null; }
    }
    public static void SaveSetting(string key, string value, string? path = null)
    {
        path ??= SettingsPath;
        Dictionary<string, string> saved;
        try { saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); } catch { saved = new(); }
        saved[key] = value;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(saved));
        File.Move(path + ".tmp", path, true);
    }
    public static string DisplayName(DeviceView device, DeviceView[] devices, string ownName) => devices.Count(p => p.Name.Equals(device.Name, StringComparison.OrdinalIgnoreCase)) > 1 || device.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase)
        ? device.Name + " · " + ShortTag(device.Id, 4) : device.Name;
}
