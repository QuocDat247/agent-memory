using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PersonalLearningAgent;

public partial class MainWindow : Window
{
    private static readonly Brush SelectedModeBrush = new SolidColorBrush(Color.FromRgb(54, 92, 245));
    private static readonly Brush SelectedModeForeground = Brushes.White;
    private static readonly Brush UnselectedModeBrush = Brushes.White;
    private static readonly Brush UnselectedModeForeground = new SolidColorBrush(Color.FromRgb(71, 84, 103));

    private readonly MemoryStore _memoryStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly Dictionary<TextBox, (MemoryEntry Entry, Border Bubble)> _memoryResponses = [];
    private readonly List<Border> _chatBubbles = [];
    private AgentMode _mode = AgentMode.Remember;
    private MemoryEntry? _selectedMemoryToDelete;

    public MainWindow()
    {
        InitializeComponent();
        _chatBubbles.Add(WelcomeBubble);
        DataLocation.Text = $"Tệp dữ liệu cục bộ: {_memoryStore.DataFilePath}";
        LoadAppearanceSettings();
        UpdateModeButtons();
        UpdateChatBubbleWidths();
        MessageInput.Focus();
    }

    private void RememberModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetMode(AgentMode.Remember);
    }

    private void ReadModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetMode(AgentMode.Read);
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        ProcessMessage();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var currentColor = GetColorString(Background);
        var dialog = new SettingsWindow(currentColor)
        {
            Owner = this
        };
        dialog.PreviewColorChanged += ApplyBackgroundColor;

        if (dialog.ShowDialog() != true)
        {
            ApplyBackgroundColor(currentColor);
            return;
        }

        try
        {
            _settingsStore.Save(new AppSettings(dialog.SelectedColor));
            ApplyBackgroundColor(dialog.SelectedColor);
        }
        catch (IOException exception)
        {
            ApplyBackgroundColor(currentColor);
            ShowError($"Không thể lưu cài đặt: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            ApplyBackgroundColor(currentColor);
            ShowError($"Không có quyền lưu cài đặt: {exception.Message}");
        }
    }

    private void ChatScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateChatBubbleWidths();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)UpdateChatBubbleWidths);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMemoryToDelete is not { } selectedMemory)
        {
            UpdateDeleteButton();
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Thao tác này sẽ xóa toàn bộ ghi nhớ đã lưu tương ứng với câu trả lời đang chọn. Không thể hoàn tác. Bạn có muốn tiếp tục?",
            "Xác nhận xóa ghi nhớ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!_memoryStore.Delete(selectedMemory.Id))
            {
                ClearSelectedMemory();
                AddMessage("Không tìm thấy ghi nhớ này trong tệp dữ liệu; có thể mục đó đã bị xóa.", isUser: false);
                return;
            }

            var matchingResponses = _memoryResponses
                .Where(pair => string.Equals(pair.Value.Entry.Id, selectedMemory.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ClearSelectedMemory();
            foreach (var response in matchingResponses)
            {
                ChatHistory.Children.Remove(response.Value.Bubble);
                _chatBubbles.Remove(response.Value.Bubble);
                _memoryResponses.Remove(response.Key);
            }

            AddMessage("Đã xóa ghi nhớ đã chọn.", isUser: false);
        }
        catch (JsonException exception)
        {
            ShowError($"Không thể đọc dữ liệu ghi nhớ: {exception.Message}");
        }
        catch (IOException exception)
        {
            ShowError($"Không thể cập nhật tệp ghi nhớ: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowError($"Không có quyền xóa ghi nhớ: {exception.Message}");
        }
    }

    private void ChatMessage_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DeleteButton is null)
        {
            return;
        }

        if (sender is TextBox textBox
            && textBox.SelectionLength > 0
            && _memoryResponses.TryGetValue(textBox, out var response)
            && textBox.SelectedText.Any(character => !char.IsWhiteSpace(character)))
        {
            _selectedMemoryToDelete = response.Entry;
        }
        else if (!DeleteButton.IsKeyboardFocusWithin && !DeleteButton.IsMouseOver)
        {
            _selectedMemoryToDelete = null;
        }

        UpdateDeleteButton();
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            ProcessMessage();
        }
    }

    private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        InputHint.Visibility = string.IsNullOrEmpty(MessageInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetMode(AgentMode mode)
    {
        _mode = mode;
        UpdateModeButtons();
        MessageInput.Focus();
    }

    private void UpdateModeButtons()
    {
        SetButtonSelected(RememberModeButton, _mode == AgentMode.Remember);
        SetButtonSelected(ReadModeButton, _mode == AgentMode.Read);
        ModeDescription.Text = _mode == AgentMode.Remember
            ? "Nhập nội dung mới để lưu."
            : "Nhập nội dung hoặc từ khóa để tìm.";
        InputHint.Text = _mode == AgentMode.Remember
            ? "Nhập nội dung cần ghi nhớ..."
            : "Nhập nội dung hoặc từ khóa cần đọc...";
    }

    private static void SetButtonSelected(Button button, bool selected)
    {
        button.Background = selected ? SelectedModeBrush : UnselectedModeBrush;
        button.Foreground = selected ? SelectedModeForeground : UnselectedModeForeground;
        button.BorderBrush = selected ? SelectedModeBrush : new SolidColorBrush(Color.FromRgb(208, 213, 221));
    }

    private void ProcessMessage()
    {
        var text = MessageInput.Text.Trim();
        if (text.Length == 0)
        {
            MessageInput.Focus();
            return;
        }

        AddMessage(text, isUser: true);
        MessageInput.Clear();

        try
        {
            if (_mode == AgentMode.Remember)
            {
                var entry = _memoryStore.Append(text);
                AddMessage($"Mình đã ghi nhớ nội dung này.\nID: {entry.Id}", isUser: false);
                return;
            }

            var entries = _memoryStore.ReadAll();
            if (entries.Count == 0)
            {
                AddMessage("Chưa có ghi nhớ nào được lưu. Chọn “Ghi nhớ” để thêm nội dung.", isUser: false);
                return;
            }

            var match = MemoryMatcher.FindBest(text, entries);
            if (match is null)
            {
                AddMessage("Mình chưa tìm thấy nội dung có từ nào đủ gần với yêu cầu này. Thử dùng từ khóa khác nhé.", isUser: false);
                return;
            }

            var percentage = Math.Round(match.Similarity * 100);
            AddMessage(
                $"Ghi nhớ phù hợp nhất ({percentage}% tương đồng):\n{match.Entry.Text}\n\nID: {match.Entry.Id}",
                isUser: false,
                memoryEntry: match.Entry);
        }
        catch (JsonException exception)
        {
            AddMessage($"Không thể đọc dữ liệu ghi nhớ: {exception.Message}", isUser: false);
        }
        catch (IOException exception)
        {
            AddMessage($"Không thể truy cập tệp ghi nhớ: {exception.Message}", isUser: false);
        }
        catch (UnauthorizedAccessException exception)
        {
            AddMessage($"Không có quyền truy cập tệp ghi nhớ: {exception.Message}", isUser: false);
        }
    }

    private void AddMessage(string text, bool isUser, MemoryEntry? memoryEntry = null)
    {
        var messageText = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = isUser ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 34, 48)),
            Focusable = true,
            Cursor = Cursors.IBeam
        };
        messageText.SelectionChanged += ChatMessage_SelectionChanged;

        var message = new Border
        {
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 680,
            Margin = isUser ? new Thickness(48, 0, 0, 12) : new Thickness(0, 0, 48, 12),
            Padding = new Thickness(14),
            Background = isUser
                ? new SolidColorBrush(Color.FromRgb(54, 92, 245))
                : new SolidColorBrush(Color.FromRgb(242, 244, 247)),
            CornerRadius = new CornerRadius(10),
            Child = messageText
        };

        ChatHistory.Children.Add(message);
        _chatBubbles.Add(message);
        if (memoryEntry is not null)
        {
            _memoryResponses.Add(messageText, (memoryEntry, message));
        }

        ChatScrollViewer.ScrollToEnd();
    }

    private void UpdateChatBubbleWidths()
    {
        if (ChatScrollViewer is null)
        {
            return;
        }

        var availableWidth = ChatScrollViewer.ActualWidth;

        if (availableWidth <= 0)
        {
            availableWidth = ChatScrollViewer.ViewportWidth;
            if (availableWidth <= 0)
            {
                return;
            }
        }

        foreach (var bubble in _chatBubbles)
        {
            var horizontalMargin = bubble.Margin.Left + bubble.Margin.Right;
            bubble.Width = Math.Max(0, availableWidth * 0.88 - horizontalMargin);
        }
    }

    private void LoadAppearanceSettings()
    {
        try
        {
            ApplyBackgroundColor(_settingsStore.Load().BackgroundColor);
        }
        catch (JsonException exception)
        {
            ShowError($"Không thể đọc cài đặt giao diện; đang dùng màu nền mặc định.\n{exception.Message}");
            ApplyBackgroundColor(AppSettings.Default.BackgroundColor);
        }
        catch (IOException exception)
        {
            ShowError($"Không thể đọc tệp cài đặt; đang dùng màu nền mặc định.\n{exception.Message}");
            ApplyBackgroundColor(AppSettings.Default.BackgroundColor);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowError($"Không có quyền đọc tệp cài đặt; đang dùng màu nền mặc định.\n{exception.Message}");
            ApplyBackgroundColor(AppSettings.Default.BackgroundColor);
        }
    }

    private void ApplyBackgroundColor(string color)
    {
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
    }

    private static string GetColorString(Brush brush) =>
        brush is SolidColorBrush solidColorBrush
            ? $"#{solidColorBrush.Color.R:X2}{solidColorBrush.Color.G:X2}{solidColorBrush.Color.B:X2}"
            : AppSettings.Default.BackgroundColor;

    private void UpdateDeleteButton()
    {
        if (DeleteButton is null)
        {
            return;
        }

        DeleteButton.IsEnabled = _selectedMemoryToDelete is not null;
        DeleteButton.Opacity = DeleteButton.IsEnabled ? 1 : 0.5;
    }

    private void ClearSelectedMemory()
    {
        _selectedMemoryToDelete = null;
        UpdateDeleteButton();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Trợ lý học tập cá nhân", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private enum AgentMode
    {
        Remember,
        Read
    }
}
