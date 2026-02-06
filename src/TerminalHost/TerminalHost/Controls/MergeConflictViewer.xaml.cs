using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Domain;

namespace TerminalHost.Controls;

public partial class MergeConflictViewer : UserControl
{
    private ConflictInfo? _conflictInfo;
    private int _currentHunkIndex;

    public event EventHandler<ConflictResolution>? ResolutionApplied;
    public event EventHandler<string>? ResultContentChanged;

    public MergeConflictViewer()
    {
        InitializeComponent();

        AcceptOursButton.Click += (s, e) => ApplyResolution(ConflictResolution.AcceptOurs);
        AcceptTheirsButton.Click += (s, e) => ApplyResolution(ConflictResolution.AcceptTheirs);
        AcceptBothButton.Click += (s, e) => ApplyResolution(ConflictResolution.AcceptBoth);
        PrevHunkButton.Click += (s, e) => NavigateHunk(-1);
        NextHunkButton.Click += (s, e) => NavigateHunk(1);
        ResultTextBox.TextChanged += (s, e) => ResultContentChanged?.Invoke(this, ResultTextBox.Text);
    }

    public void LoadConflict(ConflictInfo? info)
    {
        _conflictInfo = info;
        _currentHunkIndex = 0;

        if (info == null || info.Hunks.Count == 0)
        {
            OursTextBox.Text = "";
            TheirsTextBox.Text = "";
            ResultTextBox.Text = "";
            HunkCountText.Text = "No conflicts";
            return;
        }

        ShowCurrentHunk();
    }

    public string GetResultContent() => ResultTextBox.Text;

    private void ShowCurrentHunk()
    {
        if (_conflictInfo == null || _currentHunkIndex >= _conflictInfo.Hunks.Count) return;

        var hunk = _conflictInfo.Hunks[_currentHunkIndex];
        OursTextBox.Text = hunk.OursContent;
        TheirsTextBox.Text = hunk.TheirsContent;
        ResultTextBox.Text = _conflictInfo.ResolvedLines.Count > _currentHunkIndex
            ? _conflictInfo.ResolvedLines[_currentHunkIndex]
            : hunk.OursContent;

        HunkCountText.Text = $"Conflict {_currentHunkIndex + 1} / {_conflictInfo.Hunks.Count}";
        PrevHunkButton.IsEnabled = _currentHunkIndex > 0;
        NextHunkButton.IsEnabled = _currentHunkIndex < _conflictInfo.Hunks.Count - 1;
    }

    private void ApplyResolution(ConflictResolution resolution)
    {
        if (_conflictInfo == null || _currentHunkIndex >= _conflictInfo.Hunks.Count) return;

        var hunk = _conflictInfo.Hunks[_currentHunkIndex];
        var resolved = resolution switch
        {
            ConflictResolution.AcceptOurs => hunk.OursContent,
            ConflictResolution.AcceptTheirs => hunk.TheirsContent,
            ConflictResolution.AcceptBoth => hunk.OursContent + "\n" + hunk.TheirsContent,
            _ => ResultTextBox.Text
        };

        ResultTextBox.Text = resolved;

        // Update resolved lines
        while (_conflictInfo.ResolvedLines.Count <= _currentHunkIndex)
            _conflictInfo.ResolvedLines.Add("");
        _conflictInfo.ResolvedLines[_currentHunkIndex] = resolved;

        ResolutionApplied?.Invoke(this, resolution);
    }

    private void NavigateHunk(int delta)
    {
        if (_conflictInfo == null) return;

        // Save current resolution
        if (_conflictInfo.ResolvedLines.Count > _currentHunkIndex)
        {
            _conflictInfo.ResolvedLines[_currentHunkIndex] = ResultTextBox.Text;
        }

        var newIndex = _currentHunkIndex + delta;
        if (newIndex >= 0 && newIndex < _conflictInfo.Hunks.Count)
        {
            _currentHunkIndex = newIndex;
            ShowCurrentHunk();
        }
    }
}
