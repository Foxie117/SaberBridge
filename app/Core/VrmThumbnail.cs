using System.Text;
using System.Text.Json;

namespace SaberBridge.Core;

/// <summary>Reads the thumbnail a VRM file carries (VRM 0.x meta.texture, or VRM 1.0 meta.thumbnailImage) out of the GLB.</summary>
public static class VrmThumbnail
{
    public static Stream? Open(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            using BinaryReader r = new(fs);
            if (r.ReadUInt32() != 0x46546C67) return null; // "glTF"
            r.ReadUInt32();
            r.ReadUInt32();
            int jsonLength = r.ReadInt32();
            if (r.ReadUInt32() != 0x4E4F534A) return null; // "JSON"
            string json = Encoding.UTF8.GetString(r.ReadBytes(jsonLength));
            long binStart = 20 + jsonLength + 8;

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("extensions", out JsonElement ext)) return null;
            int image = -1;
            if (ext.TryGetProperty("VRM", out JsonElement vrm0) && vrm0.TryGetProperty("meta", out JsonElement meta0)
                && meta0.TryGetProperty("texture", out JsonElement tex) && tex.GetInt32() >= 0
                && root.TryGetProperty("textures", out JsonElement textures) && tex.GetInt32() < textures.GetArrayLength())
                image = textures[tex.GetInt32()].GetProperty("source").GetInt32();
            else if (ext.TryGetProperty("VRMC_vrm", out JsonElement vrm1) && vrm1.TryGetProperty("meta", out JsonElement meta1)
                     && meta1.TryGetProperty("thumbnailImage", out JsonElement img))
                image = img.GetInt32();
            if (image < 0 || !root.TryGetProperty("images", out JsonElement images) || image >= images.GetArrayLength()) return null;

            JsonElement view = root.GetProperty("bufferViews")[images[image].GetProperty("bufferView").GetInt32()];
            long offset = view.TryGetProperty("byteOffset", out JsonElement o) ? o.GetInt64() : 0;
            int length = view.GetProperty("byteLength").GetInt32();
            fs.Position = binStart + offset;
            return new MemoryStream(r.ReadBytes(length));
        }
        catch (Exception)
        {
            // no thumbnail, or not a VRM: the card shows none
            return null;
        }
    }
}
