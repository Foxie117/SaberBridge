namespace BSAvatarConverter.Core;

public enum JobState { Waiting, Working, Done, Failed, Skipped }

public sealed record BuiltAvatar(string File, string? Cover);

/// <summary>What is happening right now; percentages are 0..100.</summary>
public sealed record ProgressInfo(string Phase, string? Model, int ModelIndex, int ModelCount, double Overall, InputJob? Job, double JobPercent);

public interface IConversionListener
{
    void Log(string line);
    void Progress(ProgressInfo info);
    void JobState(InputJob job, JobState state, string? detail = null);
    void Built(BuiltAvatar avatar);

    /// <summary>Sets the state only if the job is still marked as working (the report did not mention it).</summary>
    void Finish(InputJob job, JobState state, string? detail);
}

/// <summary>Groups the jobs into Unity runs (all VRM in one, all PMX in one, one per VRChat package) and collects the results.</summary>
public sealed class Converter
{
    private const string Vrm = "VRM2BS.VrmToAvatarConverter.Run";
    private const string Pmx = "VRM2BS.PmxAvatarConverter.Run";
    private const string Vrc = "VRM2BS.VrcAvatarConverter.Run";

    // share of a model's work done when the converter reports each stage
    private static readonly Dictionary<string, double> StageShare = new()
    {
        ["import"] = 0.10, ["skeleton"] = 0.25, ["physics"] = 0.40, ["materials"] = 0.55, ["ik"] = 0.70, ["cover"] = 0.80, ["bundle"] = 0.90,
    };

    // number of "Start importing" lines of a first import, measured once; only used to estimate the percentage
    private const int FirstImportAssets = 1900;

    private readonly string _unity;
    private readonly GameInstall _game;
    private readonly IConversionListener _ui;
    private readonly string _outDir;
    private readonly string _coverDir;

    private int _units;
    private int _doneUnits;
    private double _unitShare;
    private InputJob? _job;
    private string? _model;

    public Converter(string unity, GameInstall game, IConversionListener ui)
    {
        _unity = unity;
        _game = game;
        _ui = ui;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _outDir = Path.Combine(AppPaths.Output, stamp);
        _coverDir = Path.Combine(AppPaths.Output, stamp + "-covers");
    }

    public string OutDir => _outDir;

    public async Task RunAsync(IReadOnlyList<InputJob> jobs, CancellationToken ct)
    {
        Directory.CreateDirectory(_outDir);
        List<InputJob> vrm = jobs.Where(j => j.Kind == JobKind.Vrm).ToList();
        List<InputJob> pmx = jobs.Where(j => j.Kind == JobKind.Pmx).ToList();
        List<InputJob> vrc = jobs.Where(j => j.Kind == JobKind.VrcPackage).ToList();
        _units = vrm.Count + pmx.Count + vrc.Count;

        Report("prepare");
        await Task.Run(() => ProjectSetup.Ensure(_game), ct);

        if (vrm.Count > 0)
        {
            string dir = Path.Combine(AppPaths.Temp, "vrm-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            Dictionary<string, InputJob> byFile = new(StringComparer.OrdinalIgnoreCase);
            foreach (InputJob j in vrm)
            {
                string name = Unique(dir, Path.GetFileName(j.File));
                File.Copy(j.File, Path.Combine(dir, name));
                byFile[name] = j;
            }

            await RunGroup(vrm, Vrm, new[] { "-vrmDir", dir }, label => byFile.GetValueOrDefault(label), (line, done) =>
            {
                foreach (var (file, job) in byFile)
                {
                    if (line.StartsWith($"OK   {file} ")) done(job, JobState.Done, null);
                    else if (line.StartsWith($"FAIL {file}:")) done(job, JobState.Failed, FirstLine(line));
                    else if (line.StartsWith($"SKIP {file}:")) done(job, JobState.Skipped, "duplicate");
                }
            }, ct);
        }

        if (pmx.Count > 0)
        {
            Dictionary<string, InputJob> byName = new(StringComparer.OrdinalIgnoreCase);
            List<string> lines = new();
            foreach (InputJob j in pmx)
            {
                string name = j.Name.Replace('|', '_');
                for (int i = 2; byName.ContainsKey(name); i++) name = $"{j.Name} ({i})";
                byName[name] = j;
                lines.Add($"{name}|{j.File}");
            }

            Directory.CreateDirectory(AppPaths.Temp);
            string jobsFile = Path.Combine(AppPaths.Temp, $"pmx-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(jobsFile, lines, ct);
            await RunGroup(pmx, Pmx, new[] { "-jobsFile", jobsFile }, label => byName.GetValueOrDefault(label), (line, done) =>
            {
                foreach (var (name, job) in byName)
                {
                    if (line.StartsWith($"OK   {name} (")) done(job, JobState.Done, null);
                    else if (line.StartsWith($"FAIL {name}:")) done(job, JobState.Failed, FirstLine(line));
                }
            }, ct);
        }

        // one package per Unity run: packages often share asset GUIDs
        foreach (InputJob j in vrc)
        {
            string folder = new string(j.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
            string import = Path.Combine(AppPaths.UnityProject, "Assets", "VRC_Import", folder);
            try
            {
                _job = j;
                _model = j.Name;
                _ui.JobState(j, JobState.Working);
                Report("unpack");
                int n = await Task.Run(() => UnityPackage.Extract(j.File, import), ct);
                _ui.Log($"{j.Name}: {n} assets unpacked");
                await RunGroup(new[] { j }, Vrc, new[] { "-importDir", $"Assets/VRC_Import/{folder}" }, _ => j, (_, _) => { }, ct);
            }
            finally
            {
                if (Directory.Exists(import)) Directory.Delete(import, true);
                if (File.Exists(import + ".meta")) File.Delete(import + ".meta");
            }
        }

        _job = null;
        _model = null;
        _doneUnits = _units;
        _unitShare = 0;
        Report("done");
    }

    private void Report(string phase) =>
        _ui.Progress(new ProgressInfo(phase, _model, Math.Min(_doneUnits + 1, _units), _units,
            _units == 0 ? 100 : 100.0 * (_doneUnits + _unitShare) / _units, _job, _unitShare * 100));

    private async Task RunGroup(IReadOnlyList<InputJob> jobs, string method, string[] args, Func<string, InputJob?> jobFor,
                                Action<string, Action<InputJob, JobState, string?>> match, CancellationToken ct)
    {
        HashSet<string> before = Directory.GetFiles(_outDir, "*.avatar").ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<InputJob> finished = new();
        int ok = 0;
        int imported = 0;
        bool firstImport = ProjectSetup.IsFirstImport;
        int startUnits = _doneUnits;

        void Done(InputJob job, JobState state, string? detail)
        {
            if (!finished.Add(job)) return;
            _ui.JobState(job, state, detail);
            _doneUnits++;
            _unitShare = 0;
        }

        _unitShare = 0;
        Report(firstImport ? "first-import" : "unity-start");

        UnityResult r = await UnityRunner.RunAsync(_unity, method, args.Concat(new[] { "-outDir", _outDir, "-coverDir", _coverDir, "-previewDir", PreviewCache.Root }), line =>
        {
            if (firstImport)
            {
                ProjectSetup.MarkImported();
                firstImport = false;
            }

            if (line.StartsWith("BEGIN "))
            {
                string label = line[6..].Trim();
                string shortLabel = label[(label.LastIndexOf(" / ", StringComparison.Ordinal) + 1)..].Trim(' ', '/');
                InputJob? job = jobFor(label);
                if (job != null && job != _job)
                {
                    _job = job;
                    _ui.JobState(job, JobState.Working);
                }

                _model = jobs.Count == 1 ? jobs[0].Name : job?.Name ?? label;
                if (jobs.Count == 1 && shortLabel != jobs[0].Name) _model = $"{jobs[0].Name} · {shortLabel}";
                _unitShare = 0.05;
                Report("model");
                return;
            }

            if (line.StartsWith("STAGE "))
            {
                string stage = line[6..].Trim();
                // a package with several avatars keeps cycling through the stages; never move backwards within one unit
                if (StageShare.TryGetValue(stage, out double share)) _unitShare = Math.Max(_unitShare, share);
                Report("stage:" + stage);
                return;
            }

            // after the models: each built .avatar is loaded back, checked and rendered for the preview
            if (line.StartsWith("PREVIEW ") && !line.StartsWith("PREVIEW FAIL"))
            {
                _ui.Log(line);
                _model = line.Split(' ', 3).ElementAtOrDefault(2) is { } f ? Path.GetFileNameWithoutExtension(f) : _model;
                Report("preview");
                return;
            }

            _ui.Log(FirstLine(line));
            match(line, Done);
            if (line.StartsWith("OK ")) ok++;
            if (line.StartsWith("OK ") || line.StartsWith("FAIL ") || line.StartsWith("SKIP ")) Report("model-done");
        }, raw =>
        {
            if (!firstImport || !raw.Contains("Start importing ")) return;
            if (++imported % 25 != 0) return;
            _ui.Progress(new ProgressInfo("first-import", null, 0, _units, Math.Min(99, 100.0 * imported / FirstImportAssets), null, 0));
        }, ct);

        foreach (string file in Directory.GetFiles(_outDir, "*.avatar").Where(f => !before.Contains(f)).OrderBy(f => f))
        {
            string cover = Path.Combine(_coverDir, Path.GetFileNameWithoutExtension(file) + ".png");
            _ui.Built(new BuiltAvatar(file, File.Exists(cover) ? cover : null));
        }

        string? problem = r.Problem ?? (r.ExitCode != 0 && ok == 0 ? $"Unity exited with code {r.ExitCode}" : null);
        if (problem != null) _ui.Log("⚠ " + problem);
        if (imported > 0) _ui.Log($"(first import: {imported} assets)");

        // whatever the report did not mention: a package counts as done if it produced anything
        foreach (InputJob j in jobs.Where(j => !finished.Contains(j)))
        {
            _ui.Finish(j, ok > 0 && jobs.Count == 1 ? JobState.Done : JobState.Failed, problem);
        }

        _doneUnits = startUnits + jobs.Count;
        _unitShare = 0;
        _job = null;
    }

    private static string Unique(string dir, string name)
    {
        string candidate = name;
        for (int i = 2; File.Exists(Path.Combine(dir, candidate)); i++)
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        return candidate;
    }

    private static string FirstLine(string s)
    {
        int i = s.IndexOf('\n');
        return i < 0 ? s : s[..i];
    }
}
