using System;
using System.Collections.Generic;
using System.Text;

namespace VRM2BS
{
    /// <summary>
    /// Repairs VRM 0.x files whose secondaryAnimation.boneGroups omit fields that UniVRM's 0.x -> 1.0 migration
    /// requires (e.g. "center"), which makes the import fail with KeyNotFoundException.
    /// </summary>
    public static class VrmFixer
    {
        private static readonly (string key, string value)[] BoneGroupDefaults =
        {
            ("comment", "\"\""),
            ("stiffiness", "1"),
            ("gravityPower", "0"),
            ("gravityDir", "{\"x\":0,\"y\":-1,\"z\":0}"),
            ("dragForce", "0.4"),
            ("center", "-1"),
            ("hitRadius", "0.02"),
            ("bones", "[]"),
            ("colliderGroups", "[]"),
        };

        public static byte[] FixVrm0SpringBoneGroups(byte[] glb)
        {
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) return glb; // "glTF"

            int jsonLength = BitConverter.ToInt32(glb, 12);
            if (BitConverter.ToUInt32(glb, 16) != 0x4E4F534A) return glb; // "JSON"

            string json = Encoding.UTF8.GetString(glb, 20, jsonLength);
            string fixedJson = PatchBoneGroups(json);
            if (fixedJson == json) return glb;

            byte[] jsonBytes = Encoding.UTF8.GetBytes(fixedJson);
            int padded = (jsonBytes.Length + 3) & ~3;
            int rest = glb.Length - 20 - jsonLength;

            byte[] result = new byte[20 + padded + rest];
            Array.Copy(glb, 0, result, 0, 12);
            BitConverter.GetBytes((uint)result.Length).CopyTo(result, 8);
            BitConverter.GetBytes(padded).CopyTo(result, 12);
            BitConverter.GetBytes(0x4E4F534Au).CopyTo(result, 16);
            Array.Copy(jsonBytes, 0, result, 20, jsonBytes.Length);
            for (int i = jsonBytes.Length; i < padded; i++) result[20 + i] = 0x20;
            Array.Copy(glb, 20 + jsonLength, result, 20 + padded, rest);
            return result;
        }

        private static string PatchBoneGroups(string json)
        {
            int key = json.IndexOf("\"boneGroups\"", StringComparison.Ordinal);
            if (key < 0) return json;

            int arrayStart = json.IndexOf('[', key);
            if (arrayStart < 0) return json;

            // find each top-level object inside the boneGroups array
            List<(int start, int end)> objects = new List<(int, int)>();
            int depth = 0, objStart = -1;
            bool inString = false;

            for (int i = arrayStart + 1; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{' || c == '[')
                {
                    if (depth == 0 && c == '{') objStart = i;
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    if (depth == 0) break; // end of boneGroups array
                    depth--;
                    if (depth == 0 && c == '}') objects.Add((objStart, i));
                }
            }

            StringBuilder sb = new StringBuilder(json);

            for (int k = objects.Count - 1; k >= 0; k--)
            {
                (int start, int end) = objects[k];
                string obj = json.Substring(start, end - start + 1);
                StringBuilder insert = new StringBuilder();

                foreach ((string name, string value) in BoneGroupDefaults)
                {
                    if (obj.IndexOf($"\"{name}\"", StringComparison.Ordinal) < 0) insert.Append($"\"{name}\":{value},");
                }

                if (insert.Length == 0) continue;

                bool empty = obj.Substring(1).TrimStart().StartsWith("}");
                if (empty) insert.Length--; // drop trailing comma
                sb.Insert(start + 1, insert.ToString());
            }

            return sb.ToString();
        }
    }
}
