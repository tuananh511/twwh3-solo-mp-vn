using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TWWH3SoloMp;

internal sealed record BinaryPatch(
    string Name,
    string Description,
    int ExpectedOffset,
    BytePattern Before,
    int ReplaceOffset,
    byte[] Replace);

internal sealed record BytePattern(byte?[] Bytes)
{
    public int Length => Bytes.Length;

    public static BytePattern Parse(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte?[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            bytes[i] = parts[i] is "??" or "**"
                ? null
                : Convert.ToByte(parts[i], 16);
        }

        return new BytePattern(bytes);
    }

    public static byte[] ParseExact(string value)
    {
        var pattern = Parse(value);
        if (pattern.Bytes.Any(value => value is null))
        {
            throw new InvalidOperationException("replacement bytes may not contain wildcards");
        }

        return pattern.Bytes.Select(value => value!.Value).ToArray();
    }
}

internal sealed record PatchProfile(
    string Name,
    string? DisplayVersion,
    string? Platform,
    string? ExeSha256,
    long? ExeSize,
    BinaryPatch[] Patches);

internal sealed record PatchSetConfig(int SchemaVersion, PatchProfileConfig[] Profiles);

internal sealed record PatchProfileConfig(
    string Name,
    string? DisplayVersion,
    string? Platform,
    string? ExeSha256,
    long? ExeSize,
    PatchConfig[] Patches);

internal sealed record PatchConfig(
    string Name,
    string Description,
    string ExpectedOffset,
    string Before,
    string? After,
    int? ReplaceOffset,
    string? Replace);

internal enum PatchState
{
    Unapplied,
    Applied,
    Missing,
    Ambiguous
}

internal enum ReportKind
{
    Normal,
    Detail,
    Success,
    Warning,
    Error,
    Header
}

internal sealed record ReportLine(string Text, ReportKind Kind = ReportKind.Normal);

internal sealed record OperationReport(int ExitCode, IReadOnlyList<ReportLine> Lines);

internal sealed class ReportBuilder
{
    private readonly List<ReportLine> _lines = [];

    public void Add(string text = "", ReportKind kind = ReportKind.Normal)
    {
        _lines.Add(new ReportLine(text, kind));
    }

    public OperationReport Build(int exitCode)
    {
        return new OperationReport(exitCode, _lines);
    }
}

internal static class Program
{
    private const string BackupFileName = "Warhammer3.vanilla.exe";
    private static readonly Lazy<PatchProfile[]> PatchProfiles = new(LoadPatchProfiles);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                RunGui();
                return 0;
            }

            AttachToParentConsole();

            if (args.Length > 3)
            {
                PrintUsage();
                return 2;
            }

            var command = args[0].ToLowerInvariant();
            string? exeArg = null;
            var force = false;

            foreach (var arg in args.Skip(1))
            {
                if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase))
                {
                    force = true;
                    continue;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"unknown option: {arg}");
                    return 2;
                }

                if (exeArg is not null)
                {
                    Console.Error.WriteLine("only one executable path may be provided");
                    return 2;
                }

                exeArg = arg;
            }

            var exePath = Path.GetFullPath(exeArg ?? "Warhammer3.exe");

            return command switch
            {
                "status" => PrintStatus(exePath),
                "apply" => ApplyPatches(exePath, force),
                "restore" => RestoreBackup(exePath),
                _ => PrintUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int PrintUsage()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        Console.WriteLine($"TWWH3 Solo Multiplayer Patcher v{version}");
        Console.WriteLine("Licensed under the MIT License.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  twwh3-solo-mp-patcher status  [path\\to\\Warhammer3.exe]");
        Console.WriteLine("  twwh3-solo-mp-patcher apply   [path\\to\\Warhammer3.exe] [--force]");
        Console.WriteLine("  twwh3-solo-mp-patcher restore [path\\to\\Warhammer3.exe]");
        Console.WriteLine();
        Console.WriteLine("If no executable path is provided, ./Warhammer3.exe is used.");
        return 2;
    }

    private static int PrintStatus(string exePath)
    {
        var report = BuildStatusReport(exePath);
        WriteReport(report);
        return report.ExitCode;
    }

    private static int ApplyPatches(string exePath, bool force)
    {
        var report = BuildApplyReport(exePath, force);
        WriteReport(report);
        return report.ExitCode;
    }

    private static int RestoreBackup(string exePath)
    {
        var report = BuildRestoreReport(exePath);
        WriteReport(report);
        return report.ExitCode;
    }

    private static OperationReport BuildStatusReport(string exePath, PatchProfile? selectedProfile = null)
    {
        var report = new ReportBuilder();
        var data = ReadExecutable(exePath);
        var sha256 = Sha256(exePath);
        var profile = selectedProfile ?? SelectProfile(data, sha256);

        report.Add($"File: {exePath}", ReportKind.Header);
        report.Add($"Size: {data.Length} bytes", ReportKind.Detail);
        report.Add($"SHA-256: {sha256}", ReportKind.Detail);
        AddProfileStatus(report, profile, data.Length, sha256);
        report.Add();

        var exitCode = 0;
        var patchStates = new List<PatchState>();

        foreach (var patch in profile.Patches)
        {
            var (state, offsets) = ClassifyPatch(data, patch);
            patchStates.Add(state);
            report.Add($"{patch.Name}: {StateName(state)} ({FormatOffsets(offsets)})", ReportKindForPatchState(state));
            report.Add($"  Expected offset: 0x{patch.ExpectedOffset:X}", ReportKind.Detail);
            report.Add($"  {patch.Description}", ReportKind.Detail);

            if (state is PatchState.Missing or PatchState.Ambiguous)
            {
                exitCode = 2;
            }
        }

        if (!ProfileMatches(profile, data.Length, sha256) &&
            !patchStates.All(state => state is PatchState.Applied))
        {
            exitCode = 2;
        }

        return report.Build(exitCode);
    }

    private static OperationReport BuildApplyReport(string exePath, bool force, PatchProfile? selectedProfile = null)
    {
        var report = new ReportBuilder();
        var data = ReadExecutable(exePath);
        var sha256 = Sha256(exePath);
        var profile = selectedProfile ?? SelectProfile(data, sha256);
        report.Add($"File: {exePath}", ReportKind.Header);
        report.Add($"SHA-256 before: {sha256}", ReportKind.Detail);
        AddProfileStatus(report, profile, data.Length, sha256);

        var planned = new List<(BinaryPatch Patch, int Offset)>();

        foreach (var patch in profile.Patches)
        {
            var (state, offsets) = ClassifyPatch(data, patch);
            switch (state)
            {
                case PatchState.Applied:
                    report.Add($"{patch.Name}: already applied at 0x{offsets[0]:X}", ReportKind.Success);
                    break;
                case PatchState.Unapplied:
                    planned.Add((patch, offsets[0]));
                    report.Add($"{patch.Name}: will apply at 0x{offsets[0]:X}", ReportKind.Warning);
                    break;
                default:
                    report.Add($"{patch.Name}: cannot apply; state is {StateName(state)}", ReportKind.Error);
                    if (offsets.Count > 0)
                    {
                        report.Add($"  hits: {FormatOffsets(offsets)}", ReportKind.Detail);
                    }
                    return report.Build(2);
            }
        }

        if (planned.Count == 0)
        {
            report.Add("No changes needed.", ReportKind.Success);
            return report.Build(0);
        }

        if (!ProfileMatches(profile, data.Length, sha256))
        {
            report.Add("Cannot apply because the executable does not match the selected patch profile.", ReportKind.Error);
            return report.Build(2);
        }

        var backupPath = BackupPathFor(exePath);
        if (File.Exists(backupPath))
        {
            if (!force)
            {
                report.Add($"Backup already exists: {backupPath}", ReportKind.Error);
                report.Add("Use --force only if this executable is definitely unmodified, or restore/remove the backup first.", ReportKind.Warning);
                return report.Build(2);
            }

            if (planned.Count != profile.Patches.Length)
            {
                report.Add($"Backup already exists: {backupPath}", ReportKind.Error);
                report.Add("Refusing to overwrite it because this executable is not fully unpatched.", ReportKind.Error);
                report.Add("Restore/remove the existing backup first if you need to create a fresh one.", ReportKind.Warning);
                return report.Build(2);
            }

            report.Add("--force supplied: overwriting backup with the verified unpatched executable.", ReportKind.Warning);
        }

        File.Copy(exePath, backupPath, overwrite: true);
        report.Add($"Created backup: {backupPath}", ReportKind.Success);

        foreach (var (patch, offset) in planned)
        {
            if (!MatchesAt(data, patch.Before, offset))
            {
                report.Add($"{patch.Name}: bytes changed before write; restoring backup and aborting.", ReportKind.Error);
                File.Copy(backupPath, exePath, overwrite: true);
                return report.Build(2);
            }

            Array.Copy(patch.Replace, 0, data, offset + patch.ReplaceOffset, patch.Replace.Length);
            report.Add($"{patch.Name}: applied at 0x{offset:X}", ReportKind.Success);
        }

        File.WriteAllBytes(exePath, data);
        report.Add($"SHA-256 after:  {Sha256(exePath)}", ReportKind.Detail);
        report.Add("Done.", ReportKind.Success);
        return report.Build(0);
    }

    private static OperationReport BuildRestoreReport(string exePath)
    {
        var report = new ReportBuilder();
        var backupPath = BackupPathFor(exePath);
        if (!File.Exists(backupPath))
        {
            report.Add($"Backup not found: {backupPath}", ReportKind.Error);
            return report.Build(2);
        }

        File.Copy(backupPath, exePath, overwrite: true);
        File.Delete(backupPath);
        report.Add($"Restored {exePath} from {backupPath}", ReportKind.Success);
        report.Add("Deleted backup after successful restore.", ReportKind.Success);
        report.Add($"SHA-256: {Sha256(exePath)}", ReportKind.Detail);
        return report.Build(0);
    }

    private static string BackupPathFor(string exePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? ".";
        return Path.Combine(directory, BackupFileName);
    }

    private static byte[] ReadExecutable(string exePath)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("file not found", exePath);
        }

        return File.ReadAllBytes(exePath);
    }

    private static (PatchState State, List<int> Offsets) ClassifyPatch(byte[] data, BinaryPatch patch)
    {
        var appliedPattern = AppliedPatternFor(patch);
        if (MatchesAt(data, patch.Before, patch.ExpectedOffset))
        {
            return (PatchState.Unapplied, [patch.ExpectedOffset]);
        }

        if (MatchesAt(data, appliedPattern, patch.ExpectedOffset))
        {
            return (PatchState.Applied, [patch.ExpectedOffset]);
        }

        var beforeOffsets = FindAll(data, patch.Before);
        var afterOffsets = FindAll(data, appliedPattern);
        var allOffsets = beforeOffsets.Concat(afterOffsets).ToList();

        if (allOffsets.Count == 0)
        {
            return (PatchState.Missing, []);
        }

        return (PatchState.Ambiguous, allOffsets);
    }

    private static List<int> FindAll(byte[] data, BytePattern needle)
    {
        var offsets = new List<int>();
        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            if (MatchesAt(data, needle, i))
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static bool MatchesAt(byte[] data, BytePattern needle, int offset)
    {
        if (offset < 0 || offset + needle.Length > data.Length)
        {
            return false;
        }

        for (var i = 0; i < needle.Length; i++)
        {
            var expected = needle.Bytes[i];
            if (expected is not null && data[offset + i] != expected.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static PatchProfile SelectProfile(byte[] data, string sha256)
    {
        var profiles = PatchProfiles.Value;
        var matchedProfile = profiles.FirstOrDefault(profile => ProfileMatches(profile, data.Length, sha256));
        if (matchedProfile is not null)
        {
            return matchedProfile;
        }

        return profiles
            .Select(profile => new
            {
                Profile = profile,
                Score = profile.Patches.Count(patch => ClassifyPatch(data, patch).State is not PatchState.Missing)
            })
            .OrderByDescending(match => match.Score)
            .FirstOrDefault(match => match.Score > 0)
            ?.Profile ?? profiles[0];
    }

    private static PatchProfile[] LoadPatchProfiles()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("patches.json")
            ?? throw new InvalidOperationException("embedded patches.json resource was not found");
        var config = JsonSerializer.Deserialize<PatchSetConfig>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("embedded patches.json resource could not be read");

        if (config.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"unsupported patches.json schema version: {config.SchemaVersion}");
        }

        if (config.Profiles.Length == 0)
        {
            throw new InvalidOperationException("patches.json does not contain any profiles");
        }

        return config.Profiles
            .Select(profile => new PatchProfile(
                profile.Name,
                profile.DisplayVersion,
                profile.Platform,
                NormalizeOptionalHash(profile.ExeSha256),
                profile.ExeSize,
                profile.Patches.Select(ToBinaryPatch).ToArray()))
            .ToArray();
    }

    private static BinaryPatch ToBinaryPatch(PatchConfig patch)
    {
        var before = BytePattern.Parse(patch.Before);
        var replaceOffset = patch.ReplaceOffset ?? 0;
        var replace = patch.Replace is not null
            ? BytePattern.ParseExact(patch.Replace)
            : BytePattern.ParseExact(patch.After ?? throw new InvalidOperationException($"{patch.Name}: patch must define replace or after bytes"));

        if (replaceOffset < 0 || replaceOffset + replace.Length > before.Length)
        {
            throw new InvalidOperationException($"{patch.Name}: replacement range must fit inside before pattern");
        }

        return new BinaryPatch(
            patch.Name,
            patch.Description,
            ParseOffset(patch.ExpectedOffset),
            before,
            replaceOffset,
            replace);
    }

    private static BytePattern AppliedPatternFor(BinaryPatch patch)
    {
        var bytes = patch.Before.Bytes.ToArray();
        for (var i = 0; i < patch.Replace.Length; i++)
        {
            bytes[patch.ReplaceOffset + i] = patch.Replace[i];
        }

        return new BytePattern(bytes);
    }

    private static int ParseOffset(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(value[2..], 16)
            : Convert.ToInt32(value, 10);
    }

    private static string? NormalizeOptionalHash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static void WriteReport(OperationReport report)
    {
        foreach (var line in report.Lines)
        {
            Console.WriteLine(line.Text);
        }
    }

    private static void AddProfileStatus(ReportBuilder report, PatchProfile profile, long exeSize, string sha256)
    {
        report.Add($"Profile: {ProfileDisplayName(profile)}", ReportKind.Header);

        if (profile.ExeSize is not null && profile.ExeSize != exeSize)
        {
            report.Add($"Profile size: mismatch; expected {profile.ExeSize} bytes", ReportKind.Error);
        }
        else if (profile.ExeSize is not null)
        {
            report.Add("Profile size: matched", ReportKind.Success);
        }

        if (profile.ExeSha256 is null)
        {
            report.Add("Profile SHA-256: not configured", ReportKind.Warning);
        }
        else if (profile.ExeSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            report.Add("Profile SHA-256: matched", ReportKind.Success);
        }
        else
        {
            report.Add($"Profile SHA-256: mismatch; expected {profile.ExeSha256}", ReportKind.Error);
        }
    }

    private static string ProfileDisplayName(PatchProfile profile)
    {
        var displayName = profile.DisplayVersion ?? profile.Name;
        if (!string.IsNullOrWhiteSpace(profile.Platform))
        {
            displayName += $" ({profile.Platform})";
        }

        return displayName;
    }

    private static ReportKind ReportKindForPatchState(PatchState state)
    {
        return state switch
        {
            PatchState.Applied => ReportKind.Success,
            PatchState.Unapplied => ReportKind.Warning,
            PatchState.Missing or PatchState.Ambiguous => ReportKind.Error,
            _ => ReportKind.Normal
        };
    }

    private static bool ProfileMatches(PatchProfile profile, long exeSize, string sha256)
    {
        if (profile.ExeSize is not null && profile.ExeSize != exeSize)
        {
            return false;
        }

        return profile.ExeSha256 is null ||
            profile.ExeSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatOffsets(IReadOnlyCollection<int> offsets)
    {
        return offsets.Count == 0
            ? "-"
            : string.Join(", ", offsets.Select(offset => $"0x{offset:X}"));
    }

    private static string StateName(PatchState state)
    {
        return state.ToString().ToLowerInvariant();
    }

    private static void RunGui()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void AttachToParentConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
        {
            return;
        }

        var standardOutput = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var standardError = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetOut(standardOutput);
        Console.SetError(standardError);
    }

    private static partial class NativeMethods
    {
        public const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int processId);
    }

    private sealed class MainForm : Form
    {
        private readonly TextBox _exePathBox;
        private readonly ComboBox _profileBox;
        private readonly RichTextBox _outputBox;
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly List<Button> _operationButtons = [];
        private readonly PatchProfile[] _profiles;

        public MainForm()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
            Text = $"TWWH3 Solo Multiplayer Patcher v{version}";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 500);
            Size = new Size(840, 560);
            _profiles = PatchProfiles.Value;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var intro = new Label
            {
                AutoSize = true,
                Text = "Select your Warhammer3.exe, then check status, apply the patch, or restore the backup."
            };
            root.Controls.Add(intro, 0, 0);

            var pathRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 8)
            };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            pathRow.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Executable:",
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            }, 0, 0);

            _exePathBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = DefaultGuiExePath()
            };
            pathRow.Controls.Add(_exePathBox, 1, 0);

            var browseButton = new Button
            {
                Text = "Browse...",
                AutoSize = true,
                Margin = new Padding(8, 0, 0, 0)
            };
            _operationButtons.Add(browseButton);
            browseButton.Click += (_, _) => BrowseForExecutable();
            pathRow.Controls.Add(browseButton, 2, 0);
            root.Controls.Add(pathRow, 0, 1);

            var profileRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            profileRow.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Game version:",
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            }, 0, 0);

            _profileBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var profile in _profiles)
            {
                _profileBox.Items.Add(new ProfileListItem(profile));
            }

            if (_profileBox.Items.Count > 0)
            {
                _profileBox.SelectedIndex = 0;
            }

            profileRow.Controls.Add(_profileBox, 1, 0);
            root.Controls.Add(profileRow, 0, 2);

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };

            _outputBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9),
                Text = "Ready.\r\n",
                BorderStyle = BorderStyle.Fixed3D
            };

            buttonRow.Controls.Add(MakeOperationButton("Check Status", async (_, _) => await RunReportAsync(() => BuildStatusReport(SelectedExePath(), SelectedProfile()))));
            buttonRow.Controls.Add(MakeOperationButton("Apply Patch", async (_, _) => await ConfirmAndRunAsync(
                "Apply the solo multiplayer patch to this executable?",
                () => BuildApplyReport(SelectedExePath(), force: false, SelectedProfile()))));
            buttonRow.Controls.Add(MakeOperationButton("Restore Backup", async (_, _) => await ConfirmAndRunAsync(
                "Restore this executable from Warhammer3.vanilla.exe?",
                () => BuildRestoreReport(SelectedExePath()))));
            buttonRow.Controls.Add(MakeOperationButton("Clear Log", (_, _) => _outputBox.Clear()));
            root.Controls.Add(buttonRow, 0, 3);

            var progressRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = 18,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0,
                Visible = false
            };
            progressRow.Controls.Add(_progressBar, 0, 0);

            _statusLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Text = "Idle"
            };
            progressRow.Controls.Add(_statusLabel, 1, 0);
            root.Controls.Add(progressRow, 0, 4);

            root.Controls.Add(_outputBox, 0, 5);

            Controls.Add(root);
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.Click += onClick;
            return button;
        }

        private Button MakeOperationButton(string text, EventHandler onClick)
        {
            var button = MakeButton(text, onClick);
            _operationButtons.Add(button);
            return button;
        }

        private static string DefaultGuiExePath()
        {
            return CandidateExePaths().FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static IEnumerable<string> CandidateExePaths()
        {
            yield return Path.GetFullPath("Warhammer3.exe");
            yield return Path.Combine(AppContext.BaseDirectory, "Warhammer3.exe");

            foreach (var steamPath in SteamInstallPaths().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var libraryPath in SteamLibraryPaths(steamPath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(
                        libraryPath,
                        "steamapps",
                        "common",
                        "Total War WARHAMMER III",
                        "Warhammer3.exe");
                }
            }
        }

        private static IEnumerable<string> SteamInstallPaths()
        {
            if (!OperatingSystem.IsWindows())
            {
                yield break;
            }

            var currentUserSteamPath = Registry.CurrentUser
                .OpenSubKey(@"Software\Valve\Steam")
                ?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(currentUserSteamPath))
            {
                yield return NormalizeSteamPath(currentUserSteamPath);
            }

            var localMachineSteamPath = Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                ?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(localMachineSteamPath))
            {
                yield return NormalizeSteamPath(localMachineSteamPath);
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "Steam");
            }
        }

        private static IEnumerable<string> SteamLibraryPaths(string steamPath)
        {
            yield return steamPath;

            var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                yield break;
            }

            foreach (var line in File.ReadLines(libraryFoldersPath))
            {
                var quotedParts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (quotedParts.Length < 2)
                {
                    continue;
                }

                var key = quotedParts[0];
                var value = quotedParts[1];
                if (key.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                    int.TryParse(key, out _))
                {
                    yield return NormalizeSteamPath(value);
                }
            }
        }

        private static string NormalizeSteamPath(string value)
        {
            return value.Replace('/', Path.DirectorySeparatorChar);
        }

        private string SelectedExePath()
        {
            var value = _exePathBox.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Choose Warhammer3.exe first.");
            }

            return Path.GetFullPath(value);
        }

        private void BrowseForExecutable()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select Warhammer3.exe",
                Filter = "Warhammer3.exe|Warhammer3.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                FileName = "Warhammer3.exe"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _exePathBox.Text = dialog.FileName;
                _ = RunReportAsync(() => BuildStatusReport(SelectedExePath(), SelectedProfile()));
            }
        }

        private PatchProfile SelectedProfile()
        {
            if (_profileBox.SelectedItem is ProfileListItem item)
            {
                return item.Profile;
            }

            throw new InvalidOperationException("Choose a game version first.");
        }

        private async Task ConfirmAndRunAsync(string message, Func<OperationReport> operation)
        {
            if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            await RunReportAsync(operation);
        }

        private async Task RunReportAsync(Func<OperationReport> operation)
        {
            if (_progressBar.Visible)
            {
                return;
            }

            SetBusy(true);
            var report = await Task.Run(() => CaptureReport(operation));
            SetBusy(false);
            AppendReport(report);
        }

        private static OperationReport CaptureReport(Func<OperationReport> operation)
        {
            try
            {
                var report = operation();
                var lines = report.Lines
                    .Append(new ReportLine(""))
                    .Append(new ReportLine($"Exit code: {report.ExitCode}", report.ExitCode == 0 ? ReportKind.Success : ReportKind.Error))
                    .ToArray();
                return new OperationReport(report.ExitCode, lines);
            }
            catch (Exception ex)
            {
                return new OperationReport(1, [new ReportLine($"error: {ex.Message}", ReportKind.Error), new ReportLine(""), new ReportLine("Exit code: 1", ReportKind.Error)]);
            }
        }

        private void SetBusy(bool isBusy)
        {
            Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
            _progressBar.Visible = isBusy;
            _progressBar.MarqueeAnimationSpeed = isBusy ? 30 : 0;
            _statusLabel.Text = isBusy ? "Working..." : "Idle";
            _exePathBox.Enabled = !isBusy;
            _profileBox.Enabled = !isBusy;
            foreach (var button in _operationButtons)
            {
                button.Enabled = !isBusy;
            }
        }

        private void AppendReport(OperationReport report)
        {
            if (_outputBox.TextLength > 0 && !_outputBox.Text.EndsWith("\r\n", StringComparison.Ordinal))
            {
                _outputBox.AppendText("\r\n");
            }

            foreach (var line in report.Lines)
            {
                AppendLogLine(line);
            }

            _outputBox.AppendText("\r\n");
            _outputBox.SelectionStart = _outputBox.TextLength;
            _outputBox.ScrollToCaret();
        }

        private void AppendLogLine(ReportLine line)
        {
            _outputBox.SelectionStart = _outputBox.TextLength;
            _outputBox.SelectionLength = 0;
            _outputBox.SelectionColor = ColorForReportKind(line.Kind);
            if (line.Kind == ReportKind.Header)
            {
                _outputBox.SelectionFont = new Font(_outputBox.Font, FontStyle.Bold);
            }

            _outputBox.AppendText(line.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"));
            _outputBox.AppendText("\r\n");
            _outputBox.SelectionColor = _outputBox.ForeColor;
            _outputBox.SelectionFont = _outputBox.Font;
        }

        private static Color ColorForReportKind(ReportKind kind)
        {
            return kind switch
            {
                ReportKind.Success => Color.DarkGreen,
                ReportKind.Warning => Color.DarkOrange,
                ReportKind.Error => Color.Firebrick,
                ReportKind.Detail => Color.DimGray,
                ReportKind.Header => Color.Black,
                _ => SystemColors.WindowText
            };
        }

        private sealed class ProfileListItem(PatchProfile profile)
        {
            public PatchProfile Profile { get; } = profile;

            public override string ToString()
            {
                return ProfileDisplayName(Profile);
            }
        }
    }
}
