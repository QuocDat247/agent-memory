using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace PersonalLearningAgent;

public partial class SettingsWindow : Window
{
    private static readonly Regex HexColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public SettingsWindow(string currentColor)
    {
        InitializeComponent();
        ColorInput.Text = currentColor;
    }

    public string SelectedColor { get; private set; } = AppSettings.Default.BackgroundColor;

    public static bool IsValidColor(string? value) =>
        value is not null && HexColorPattern.IsMatch(value);

    private void ColorInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ColorPreview is null || SaveButton is null || ValidationMessage is null)
        {
            return;
        }

        if (TryParseColor(ColorInput.Text, out var color))
        {
            ColorPreview.Background = new SolidColorBrush(color);
            SaveButton.IsEnabled = true;
            ValidationMessage.Text = string.Empty;
        }
        else
        {
            ColorPreview.Background = new SolidColorBrush(Color.FromRgb(244, 246, 251));
            SaveButton.IsEnabled = false;
            ValidationMessage.Text = "Mã màu không hợp lệ. Hãy dùng định dạng #RRGGBB.";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseColor(ColorInput.Text, out var color))
        {
            ValidationMessage.Text = "Mã màu không hợp lệ. Hãy dùng định dạng #RRGGBB.";
            return;
        }

        SelectedColor = ToHex(color);
        DialogResult = true;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (!IsValidColor(value))
        {
            return false;
        }

        color = (Color)ColorConverter.ConvertFromString(value)!;
        return true;
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
