using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace TerminalHost.Views.Controls;

/// <summary>
/// Icon picker popup control for selecting icons/emoji for settings.
/// </summary>
public partial class IconPickerPopup : UserControl
{
    public static readonly DependencyProperty SelectedIconProperty =
        DependencyProperty.Register(nameof(SelectedIcon), typeof(string), typeof(IconPickerPopup),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SelectedIcon
    {
        get => (string)GetValue(SelectedIconProperty);
        set => SetValue(SelectedIconProperty, value);
    }

    public event EventHandler<string>? IconSelected;

    public ObservableCollection<IconItem> CurrentIcons { get; } = [];

    // Icon definitions by category
    private static readonly IconItem[] CommonIcons =
    [
        new("📁", "Folder"),
        new("📂", "Open Folder"),
        new("📄", "Document"),
        new("📝", "Edit"),
        new("💾", "Save"),
        new("🔧", "Settings"),
        new("⚙", "Gear"),
        new("🔍", "Search"),
        new("🔎", "Magnifier"),
        new("✏", "Pencil"),
        new("🗑", "Trash"),
        new("📋", "Clipboard"),
        new("📌", "Pin"),
        new("🏷", "Tag"),
        new("⭐", "Star"),
        new("❤", "Heart"),
        new("✅", "Check"),
        new("❌", "Cross"),
        new("⚠", "Warning"),
        new("ℹ", "Info"),
        new("🔔", "Bell"),
        new("🔒", "Lock"),
        new("🔓", "Unlock"),
        new("🔗", "Link"),
        new("📊", "Chart"),
        new("📈", "Trending"),
        new("💻", "Computer"),
        new("🖥", "Desktop"),
        new("⌨", "Keyboard"),
        new("🖱", "Mouse"),
        new("🎮", "Game"),
        new("🤖", "Robot"),
    ];

    private static readonly IconItem[] SymbolIcons =
    [
        new("→", "Right Arrow"),
        new("←", "Left Arrow"),
        new("↑", "Up Arrow"),
        new("↓", "Down Arrow"),
        new("↔", "Left Right"),
        new("↕", "Up Down"),
        new("▶", "Play"),
        new("⏸", "Pause"),
        new("⏹", "Stop"),
        new("⏺", "Record"),
        new("⏭", "Next"),
        new("⏮", "Previous"),
        new("●", "Circle"),
        new("○", "Empty Circle"),
        new("■", "Square"),
        new("□", "Empty Square"),
        new("▲", "Triangle Up"),
        new("▼", "Triangle Down"),
        new("◆", "Diamond"),
        new("◇", "Empty Diamond"),
        new("★", "Star"),
        new("☆", "Empty Star"),
        new("♦", "Diamond Suit"),
        new("♣", "Club"),
        new("♠", "Spade"),
        new("♥", "Heart Suit"),
        new("✓", "Check"),
        new("✗", "Cross"),
        new("†", "Dagger"),
        new("‡", "Double Dagger"),
        new("§", "Section"),
        new("¶", "Paragraph"),
    ];

    private static readonly IconItem[] LetterIcons =
    [
        new("A", "A"), new("B", "B"), new("C", "C"), new("D", "D"),
        new("E", "E"), new("F", "F"), new("G", "G"), new("H", "H"),
        new("I", "I"), new("J", "J"), new("K", "K"), new("L", "L"),
        new("M", "M"), new("N", "N"), new("O", "O"), new("P", "P"),
        new("Q", "Q"), new("R", "R"), new("S", "S"), new("T", "T"),
        new("U", "U"), new("V", "V"), new("W", "W"), new("X", "X"),
        new("Y", "Y"), new("Z", "Z"),
        new("AI", "AI"), new("ML", "ML"), new("GH", "GitHub"),
        new("PR", "Pull Request"), new("CI", "CI/CD"), new("DB", "Database"),
    ];

    // Nerd Font icons (requires Cascadia Code NF or similar)
    // Using Font Awesome and Devicon glyphs that are commonly available
    private static readonly IconItem[] NerdIcons =
    [
        // Font Awesome icons (nf-fa-*)
        new("\uf013", "Gear"),           // nf-fa-gear
        new("\uf015", "Home"),           // nf-fa-home
        new("\uf07b", "Folder"),         // nf-fa-folder
        new("\uf07c", "Folder Open"),    // nf-fa-folder_open
        new("\uf15b", "File"),           // nf-fa-file
        new("\uf15c", "File Text"),      // nf-fa-file_text
        new("\uf121", "Code"),           // nf-fa-code
        new("\uf126", "Git Branch"),     // nf-fa-code_fork
        new("\uf120", "Terminal"),       // nf-fa-terminal
        new("\uf0c3", "Flask"),          // nf-fa-flask
        new("\uf0e7", "Lightning"),      // nf-fa-bolt
        new("\uf021", "Refresh"),        // nf-fa-refresh
        new("\uf002", "Search"),         // nf-fa-search
        new("\uf00c", "Check"),          // nf-fa-check
        new("\uf00d", "Times"),          // nf-fa-times
        new("\uf071", "Warning"),        // nf-fa-warning
        new("\uf05a", "Info"),           // nf-fa-info_circle
        new("\uf0eb", "Lightbulb"),      // nf-fa-lightbulb_o
        new("\uf023", "Lock"),           // nf-fa-lock
        new("\uf09c", "Unlock"),         // nf-fa-unlock
        new("\uf0c5", "Copy"),           // nf-fa-copy
        new("\uf0ea", "Paste"),          // nf-fa-paste
        new("\uf0c4", "Cut"),            // nf-fa-cut
        new("\uf0e2", "Undo"),           // nf-fa-undo
        new("\uf01e", "Redo"),           // nf-fa-repeat
        new("\uf1c9", "File Code"),      // nf-fa-file_code_o
        new("\uf085", "Cogs"),           // nf-fa-cogs
        new("\uf0ad", "Wrench"),         // nf-fa-wrench
        new("\uf1de", "Sliders"),        // nf-fa-sliders
        new("\uf080", "Bar Chart"),      // nf-fa-bar_chart
    ];

    public IconPickerPopup()
    {
        InitializeComponent();
        DataContext = this;

        // Wire up category tab changes
        CommonTab.Checked += (_, _) => LoadIcons(CommonIcons);
        SymbolsTab.Checked += (_, _) => LoadIcons(SymbolIcons);
        LettersTab.Checked += (_, _) => LoadIcons(LetterIcons);
        NerdTab.Checked += (_, _) => LoadIcons(NerdIcons);

        // Load common icons by default
        LoadIcons(CommonIcons);
    }

    private void LoadIcons(IconItem[] icons)
    {
        CurrentIcons.Clear();
        foreach (var icon in icons)
        {
            CurrentIcons.Add(icon);
        }
    }

    private void IconButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string icon)
        {
            SelectedIcon = icon;
            IconSelected?.Invoke(this, icon);
        }
    }
}

public record IconItem(string Icon, string Name);
