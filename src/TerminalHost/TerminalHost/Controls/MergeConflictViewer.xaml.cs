using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Domain;

namespace TerminalHost.Controls;

public class AiSuggestEventArgs(string oursContent, string theirsContent) : EventArgs
{
    public string OursContent { get; } = oursContent;
    public string TheirsContent { get; } = theirsContent;
}

public partial class MergeConflictViewer : UserControl
{
    private ConflictInfo? _conflictInfo;
    private int _currentHunkIndex;
    private bool _isAiSuggestion;
    private bool _suppressAiIndicatorClear;

    public event EventHandler<ConflictResolution>? ResolutionApplied;
    public event EventHandler<string>? ResultContentChanged;
    public event EventHandler<AiSuggestEventArgs>? AiSuggestRequested;

    public MergeConflictViewer()
    {
        InitializeComponent();

        AcceptOursButton.Click += (s, e) => ApplyResolution(ConflictResolution.AcceptOurs);
        AcceptTheirsButton.Click += (s, e) => ApplyResolution(ConflictResolution.AcceptTheirs);
        AcceptBothButton.Click += (s, e) => ApplyResolution(ConflictResolution.AcceptBoth);
        PrevHunkButton.Click += (s, e) => NavigateHunk(-1);
        NextHunkButton.Click += (s, e) => NavigateHunk(1);
        AiSuggestButton.Click += OnAiSuggestClicked;
        ResultTextBox.TextChanged += OnResultTextChanged;
    }

    public void LoadConflict(ConflictInfo? info)
    {
        _conflictInfo = info;
        _currentHunkIndex = 0;
        ClearAiSuggestion();

        var hasConflicts = info != null && info.Hunks.Count > 0;
        AiSuggestButton.IsEnabled = hasConflicts;

        if (!hasConflicts)
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

    public void SetAiLoading(bool isLoading)
    {
        AiSuggestButton.IsEnabled = !isLoading;
        AiSuggestButton.Content = isLoading ? "Thinking..." : "✨ AI Suggest";
    }

    public void ApplyAiSuggestion(string suggestion)
    {
        _suppressAiIndicatorClear = true;
        ResultTextBox.Text = suggestion;
        _suppressAiIndicatorClear = false;

        _isAiSuggestion = true;
        AiSuggestionLabel.Visibility = Visibility.Visible;

        if (_conflictInfo != null)
        {
            while (_conflictInfo.ResolvedLines.Count <= _currentHunkIndex)
                _conflictInfo.ResolvedLines.Add("");
            _conflictInfo.ResolvedLines[_currentHunkIndex] = suggestion;
        }
    }

    private void OnAiSuggestClicked(object sender, RoutedEventArgs e)
    {
        if (_conflictInfo == null || _currentHunkIndex >= _conflictInfo.Hunks.Count) return;
        var hunk = _conflictInfo.Hunks[_currentHunkIndex];
        AiSuggestRequested?.Invoke(this, new AiSuggestEventArgs(hunk.OursContent, hunk.TheirsContent));
    }

    private void OnResultTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressAiIndicatorClear && _isAiSuggestion)
            ClearAiSuggestion();

        ResultContentChanged?.Invoke(this, ResultTextBox.Text);
    }

    private void ClearAiSuggestion()
    {
        _isAiSuggestion = false;
        AiSuggestionLabel.Visibility = Visibility.Collapsed;
    }

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
            ClearAiSuggestion();
            ShowCurrentHunk();
        }
    }
}
