using System.IO;

namespace RFIDReader.Desktop;

internal sealed class ReaderSettings
{
    public string Port { get; set; } = string.Empty;
    public string Block { get; set; } = "4";
    public string Key { get; set; } = "FFFFFFFFFFFF";
    public string Data { get; set; } = string.Empty;

    public static ReaderSettings Load(string path)
    {
        var settings = new ReaderSettings();
        if (!File.Exists(path))
        {
            return settings;
        }

        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "port": settings.Port = value; break;
                case "block": settings.Block = value; break;
                case "key": settings.Key = value; break;
                case "data": settings.Data = value; break;
            }
        }

        return settings;
    }

    public void Save(string path)
    {
        File.WriteAllLines(path,
        [
            "[connection]",
            $"port={Port}",
            string.Empty,
            "[block]",
            $"block={Block}",
            $"key={Key}",
            $"data={Data}"
        ]);
    }
}
