using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SaberBridge
{
    /// <summary>
    /// Converts a mixed list of Quest jobs in one Unity run, so the app's single Convert button needs one editor start.
    /// Jobs file lines: "source|outDir"; .bloq becomes a .cyoob, anything else is treated as a .saber.
    ///   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.QuestJobs.Run -jobsFile jobs.txt
    /// </summary>
    public static class QuestJobs
    {
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string jobsFile = args[Array.IndexOf(args, "-jobsFile") + 1];
            string[] jobs = File.ReadAllLines(jobsFile).Where(l => l.Trim().Length > 0).ToArray();

            for (int i = 0; i < jobs.Length; i++)
            {
                string[] parts = jobs[i].Split('|');
                string file = Path.GetFileName(parts[0]);
                SaberToWhacker.Log($"BEGIN {i + 1}/{jobs.Length} {file}");
                try
                {
                    bool note = Path.GetExtension(parts[0]).Equals(".bloq", StringComparison.OrdinalIgnoreCase);
                    string result = note ? BloqToCyoob.Convert(parts[0], parts[1]) : SaberToWhacker.Convert(parts[0], parts[1]);
                    SaberToWhacker.Log($"OK   {file} -> {result}");
                }
                catch (Exception ex)
                {
                    SaberToWhacker.Log($"FAIL {file}: {ex.Message}");
                    Debug.LogException(ex);
                }
            }
        }
    }
}
