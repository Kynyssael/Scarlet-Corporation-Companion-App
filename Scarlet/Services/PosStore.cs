using System.Text.Json;
using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>Loads and saves open tabs and sales history as JSON in the plugin config directory.</summary>
public class PosStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string path = Path.Combine(directory, "pos-data.json");

    public PosData Load()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PosData>(File.ReadAllText(path), JsonOptions) ?? new PosData();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load POS data, starting fresh");
            File.Copy(path, path + ".bak", overwrite: true);
        }

        return new PosData();
    }

    public void Save(PosData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to save POS data");
        }
    }
}
