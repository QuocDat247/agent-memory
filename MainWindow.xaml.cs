using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private readonly Dictionary<RichTextBox, ChatMessage> _chatMessages = [];
    private readonly DispatcherTimer _undoDeleteTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private AgentMode _mode = AgentMode.Remember;
    private MemoryEntry? _selectedMemoryToDelete;
    private MemoryEntry? _pendingDeletedMemory;
    private int _searchResultLimit = 5;
    private RichTextBox? _selectedChatText;
    private TextPointer? _selectedTextStart;
    private TextPointer? _selectedTextEnd;

    public MainWindow()
    {
        InitializeComponent();
        AddMessage("Xin chào! Chọn “Ghi nhớ” để lưu thông tin hoặc “Đọc” để tìm lại nội dung gần giống nhất trong dữ liệu đã lưu.", isUser: false);
        _undoDeleteTimer.Tick += UndoDeleteTimer_Tick;
        DataLocation.Text = $"Tệp dữ liệu cục bộ: {_memoryStore.DataFilePath}";
        LoadAppearanceSettings();
        UpdateModeButtons();
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

        if (dialog.ShowDialog() != true)
        {
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

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".jsonl",
            FileName = $"PersonalLearningAgent-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl",
            Filter = "JSON Lines (*.jsonl)|*.jsonl",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _memoryStore.ExportBackup(dialog.FileName);
            MessageBox.Show(this, "Đã sao lưu dữ liệu ghi nhớ.", "Sao lưu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ShowError($"Không thể sao lưu dữ liệu:\n{exception.Message}");
        }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "JSON Lines (*.jsonl)|*.jsonl"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Dữ liệu trong tệp sao lưu sẽ được gộp vào dữ liệu hiện tại theo ID. Các ID đã có sẽ được giữ nguyên, không ghi đè. Tiếp tục?",
            "Xác nhận khôi phục",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = _memoryStore.MergeBackup(dialog.FileName);
            MessageBox.Show(
                this,
                $"Đã thêm {result.AddedCount} ghi nhớ mới. Bỏ qua {result.SkippedCount} ID đã có.",
                "Khôi phục hoàn tất",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (JsonException exception)
        {
            ShowError($"Tệp sao lưu không hợp lệ; không có dữ liệu nào được nhập:\n{exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError($"Không thể khôi phục dữ liệu:\n{exception.Message}");
        }
    }

    private void UndoDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeletedMemory is not { } entry)
        {
            return;
        }

        _undoDeleteTimer.Stop();
        try
        {
            _memoryStore.Restore(entry);
            _pendingDeletedMemory = null;
            UndoDeleteButton.Visibility = Visibility.Collapsed;
            var responseText = $"Đã khôi phục ghi nhớ:\n{entry.Text}\n\nID: {entry.Id}";
            AddMessage(
                responseText,
                isUser: false,
                memoryEntry: entry,
                memoryTextOffset: "Đã khôi phục ghi nhớ:\n".Length);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _pendingDeletedMemory = null;
            UndoDeleteButton.Visibility = Visibility.Collapsed;
            ShowError($"Không thể khôi phục ghi nhớ:\n{exception.Message}");
        }
    }

    private void UndoDeleteTimer_Tick(object? sender, EventArgs e)
    {
        _undoDeleteTimer.Stop();
        _pendingDeletedMemory = null;
        UndoDeleteButton.Visibility = Visibility.Collapsed;
    }

    private void DataButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _memoryStore.EnsureDataFile();
            Process.Start(new ProcessStartInfo
            {
                FileName = _memoryStore.DataFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            ShowError($"Không thể mở tệp dữ liệu:\n{exception.Message}");
        }
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
            "Thao tác này sẽ xóa ghi nhớ tương ứng. Bạn có thể hoàn tác trong 10 giây. Tiếp tục?",
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

            _undoDeleteTimer.Stop();
            _pendingDeletedMemory = null;
            UndoDeleteButton.Visibility = Visibility.Collapsed;
            var matchingResponses = _chatMessages
                .Where(pair => pair.Value.MemoryEntry is { } entry
                    && string.Equals(entry.Id, selectedMemory.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ClearSelectedMemory();
            if (matchingResponses.Any(response => response.Key == _selectedChatText))
            {
                ClearSelectedChatText();
            }

            foreach (var response in matchingResponses)
            {
                ChatHistory.Children.Remove(response.Value.Row);
                _chatMessages.Remove(response.Key);
            }

            _pendingDeletedMemory = selectedMemory;
            UndoDeleteButton.Visibility = Visibility.Visible;
            _undoDeleteTimer.Start();
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

    private void EditMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RichTextBox textBox }
            || !_chatMessages.TryGetValue(textBox, out var message)
            || message.MemoryEntry is not { } entry)
        {
            return;
        }

        var dialog = new MemoryEditWindow(entry) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.EditedText))
        {
            return;
        }

        var highlights = MapHighlights(entry.Text, dialog.EditedText, entry.Highlights ?? []);
        try
        {
            var updatedEntry = _memoryStore.Update(entry with
            {
                Text = dialog.EditedText,
                Category = dialog.EditedCategory,
                Highlights = highlights
            });
            RefreshMemoryMessages(updatedEntry);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError($"Không thể cập nhật ghi nhớ:\n{exception.Message}");
        }
    }

    private void PinMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RichTextBox textBox }
            || !_chatMessages.TryGetValue(textBox, out var message)
            || message.MemoryEntry is not { } entry)
        {
            return;
        }

        try
        {
            var updatedEntry = _memoryStore.UpdatePinned(entry.Id, !entry.IsPinned);
            RefreshMemoryMessages(updatedEntry);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError($"Không thể cập nhật trạng thái ghim:\n{exception.Message}");
        }
    }

    private void ChatMessage_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DeleteButton is null || ColorButton is null)
        {
            return;
        }

        if (sender is RichTextBox richTextBox
            && !richTextBox.Selection.IsEmpty
            && richTextBox.Selection.Text.Any(character => !char.IsWhiteSpace(character)))
        {
            _selectedChatText = richTextBox;
            _selectedTextStart = richTextBox.Selection.Start;
            _selectedTextEnd = richTextBox.Selection.End;
            _selectedMemoryToDelete = _chatMessages.TryGetValue(richTextBox, out var message)
                ? message.MemoryEntry
                : null;
        }
        else if (!DeleteButton.IsKeyboardFocusWithin
                 && !DeleteButton.IsMouseOver
                 && !ColorButton.IsKeyboardFocusWithin
                 && !ColorButton.IsMouseOver
                 && !ColorPalettePopup.IsOpen)
        {
            _selectedMemoryToDelete = null;
            ClearSelectedChatText();
        }

        UpdateChatActionButtons();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedChatText is null || _selectedTextStart is null || _selectedTextEnd is null)
        {
            UpdateChatActionButtons();
            return;
        }

        ColorPalettePopup.IsOpen = !ColorPalettePopup.IsOpen;
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string colorHex }
            || _selectedTextStart is null
            || _selectedTextEnd is null
            || _selectedChatText is null)
        {
            return;
        }

        var color = (Color)ColorConverter.ConvertFromString(colorHex)!;
        var selectedRange = new TextRange(_selectedTextStart, _selectedTextEnd);
        if (_chatMessages.TryGetValue(_selectedChatText, out var message)
            && message.MemoryEntry is { } memoryEntry)
        {
            try
            {
                var selectionStart = new TextRange(
                    _selectedChatText.Document.ContentStart,
                    _selectedTextStart).Text.Length;
                var selectionLength = selectedRange.Text.Length;
                var memoryStart = Math.Max(0, selectionStart - message.MemoryTextOffset);
                var memoryEnd = Math.Min(
                    memoryEntry.Text.Length,
                    (long)selectionStart + selectionLength - message.MemoryTextOffset);
                if (memoryEnd > memoryStart)
                {
                    var highlights = (memoryEntry.Highlights ?? [])
                        .Append(new MemoryHighlight(memoryStart, (int)memoryEnd - memoryStart, colorHex))
                        .ToArray();
                    var updatedEntry = _memoryStore.UpdateHighlights(memoryEntry.Id, highlights);
                    message = message with { MemoryEntry = updatedEntry };
                    _chatMessages[_selectedChatText] = message;
                    _selectedMemoryToDelete = updatedEntry;
                }
            }
            catch (JsonException exception)
            {
                ShowError($"Không thể đọc dữ liệu ghi nhớ: {exception.Message}");
                return;
            }
            catch (IOException exception)
            {
                ShowError($"Không thể lưu màu chữ vào dữ liệu: {exception.Message}");
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowError($"Không có quyền cập nhật tệp ghi nhớ: {exception.Message}");
                return;
            }
            catch (InvalidOperationException exception)
            {
                ShowError(exception.Message);
                return;
            }
        }

        selectedRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
        ColorPalettePopup.IsOpen = false;
    }

    private void ExportWord_Click(object sender, RoutedEventArgs e) => ExportChatMessage(sender, isWord: true);

    private void ExportExcel_Click(object sender, RoutedEventArgs e) => ExportChatMessage(sender, isWord: false);

    private void ExportChatMessage(object sender, bool isWord)
    {
        if (sender is not Button { Tag: RichTextBox textBox }
            || !_chatMessages.TryGetValue(textBox, out var message))
        {
            return;
        }

        var extension = isWord ? ".docx" : ".xlsx";
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = extension,
            FileName = $"Tin-nhan-{message.SentAt:yyyyMMdd-HHmmss}{extension}",
            Filter = isWord
                ? "Tài liệu Word (*.docx)|*.docx"
                : "Sổ làm việc Excel (*.xlsx)|*.xlsx",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var exportData = new ChatExportData(message.SentAt, GetTextSegments(textBox));
        try
        {
            if (isWord)
            {
                ChatExporter.WriteWord(dialog.FileName, exportData);
            }
            else
            {
                ChatExporter.WriteExcel(dialog.FileName, exportData);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or OpenXmlPackageException)
        {
            ShowError($"Không thể xuất tệp {(isWord ? "Word" : "Excel")}:\n{exception.Message}");
            return;
        }

        MessageBox.Show(
            this,
            $"Đã xuất tệp thành công:\n{dialog.FileName}",
            "Xuất nội dung",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static IReadOnlyList<ChatTextSegment> GetTextSegments(RichTextBox textBox)
    {
        var segments = new List<ChatTextSegment>();
        foreach (var paragraph in textBox.Document.Blocks.OfType<Paragraph>())
        {
            AddInlineSegments(paragraph.Inlines, segments);
        }

        return segments;
    }

    private static void AddInlineSegments(InlineCollection inlines, ICollection<ChatTextSegment> segments)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run && run.Text.Length > 0)
            {
                var localForeground = run.ReadLocalValue(TextElement.ForegroundProperty);
                var color = localForeground is SolidColorBrush brush
                    ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
                    : null;
                segments.Add(new ChatTextSegment(run.Text, color));
            }
            else if (inline is Span span)
            {
                AddInlineSegments(span.Inlines, segments);
            }
            else if (inline is LineBreak)
            {
                segments.Add(new ChatTextSegment("\n", null));
            }
        }
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

    private void CategoryInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        CategoryHint.Visibility = string.IsNullOrWhiteSpace(CategoryInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SearchResultCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitSearchResultLimit(showError: false);
    }

    private void SearchResultCountComboBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitSearchResultLimit(showError: true);
    }

    private void SearchResultCountComboBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSearchResultLimit(showError: true);
            MessageInput.Focus();
            e.Handled = true;
        }
    }

    private void CommitSearchResultLimit(bool showError)
    {
        var value = SearchResultCountComboBox.Text.Trim();
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0)
        {
            _searchResultLimit = limit;
            SearchResultCountComboBox.Text = limit.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (showError)
        {
            MessageBox.Show(
                this,
                "Hãy nhập số nguyên lớn hơn 0 cho số kết quả tìm kiếm.",
                "Số kết quả không hợp lệ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        SearchResultCountComboBox.Text = _searchResultLimit.ToString(CultureInfo.InvariantCulture);
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
        CategoryInputHost.Visibility = _mode == AgentMode.Remember ? Visibility.Visible : Visibility.Collapsed;
        SearchResultCountPanel.Visibility = _mode == AgentMode.Read ? Visibility.Visible : Visibility.Collapsed;
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
                var entry = _memoryStore.Append(text, CategoryInput.Text);
                CategoryInput.Clear();
                var responseText = $"Mình đã ghi nhớ nội dung này.\nID: {entry.Id}";
                AddMessage(
                    responseText,
                    isUser: false,
                    memoryEntry: entry,
                    memoryTextOffset: "Mình đã ghi nhớ nội dung này.\n".Length);
                return;
            }

            var entries = _memoryStore.ReadAll();
            if (entries.Count == 0)
            {
                AddMessage("Chưa có ghi nhớ nào được lưu. Chọn “Ghi nhớ” để thêm nội dung.", isUser: false);
                return;
            }

            var matches = MemoryMatcher.FindMatches(text, entries, _searchResultLimit);
            if (matches.Count == 0)
            {
                AddMessage("Mình chưa tìm thấy nội dung có từ nào đủ gần với yêu cầu này. Thử dùng từ khóa khác nhé.", isUser: false);
                return;
            }

            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                var percentage = Math.Round(match.Similarity * 100);
                var title = $"Kết quả {index + 1} ({percentage}% tương đồng):\n";
                var responseText = $"{title}{match.Entry.Text}\n\nID: {match.Entry.Id}";
                AddMessage(
                    responseText,
                    isUser: false,
                    memoryEntry: match.Entry,
                    memoryTextOffset: title.Length);
            }
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

    private void AddMessage(
        string text,
        bool isUser,
        MemoryEntry? memoryEntry = null,
        int memoryTextOffset = 0)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            Foreground = isUser ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 34, 48))
        };
        foreach (var inline in CreateMessageInlines(text, memoryEntry, memoryTextOffset))
        {
            paragraph.Inlines.Add(inline);
        }

        var messageText = new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = true,
            Cursor = Cursors.IBeam,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                Blocks = { paragraph }
            }
        };
        messageText.SelectionChanged += ChatMessage_SelectionChanged;

        var sentAt = DateTimeOffset.Now;
        var exportButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var pinIndicator = new TextBlock
        {
            Text = memoryEntry?.IsPinned == true ? "Đã ghim" : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(181, 71, 8)),
            FontSize = 11
        };
        var categoryIndicator = new TextBlock
        {
            Text = memoryEntry?.Category ?? string.Empty,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            FontSize = 11
        };
        exportButtons.Children.Add(pinIndicator);
        exportButtons.Children.Add(categoryIndicator);
        Button? pinButton = null;
        if (memoryEntry is not null)
        {
            exportButtons.Children.Add(CreateMemoryActionButton("Sửa", messageText, EditMemoryButton_Click));
            pinButton = CreateMemoryActionButton(
                memoryEntry.IsPinned ? "Bỏ ghim" : "Ghim",
                messageText,
                PinMemoryButton_Click);
            exportButtons.Children.Add(pinButton);
        }

        exportButtons.Children.Add(CreateExportButton("Word", messageText, ExportWord_Click));
        exportButtons.Children.Add(CreateExportButton("Excel", messageText, ExportExcel_Click));

        var timestamp = new TextBlock
        {
            Text = sentAt.ToString("dd/MM/yyyy HH:mm"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = isUser
                ? new SolidColorBrush(Color.FromRgb(220, 228, 255))
                : new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11
        };
        var messageContent = new StackPanel();
        messageContent.Children.Add(exportButtons);
        messageContent.Children.Add(messageText);
        messageContent.Children.Add(timestamp);

        var message = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = isUser ? new Thickness(0, 0, 10, 0) : new Thickness(10, 0, 0, 0),
            Padding = new Thickness(14),
            Background = isUser
                ? new SolidColorBrush(Color.FromRgb(54, 92, 245))
                : new SolidColorBrush(Color.FromRgb(242, 244, 247)),
            CornerRadius = new CornerRadius(10),
            Child = messageContent
        };

        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(message, isUser ? 1 : 0);
        row.Children.Add(message);
        ChatHistory.Children.Add(row);
        _chatMessages.Add(messageText, new ChatMessage(
            sentAt,
            memoryEntry,
            memoryTextOffset,
            row,
            text,
            isUser,
            pinButton,
            pinIndicator,
            categoryIndicator));

        ChatScrollViewer.ScrollToEnd();
    }

    private Button CreateExportButton(string label, RichTextBox messageText, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Tag = messageText,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(4, 0, 0, 6),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 221)),
            Cursor = Cursors.Hand,
            Style = (Style)FindResource("RoundedButtonStyle")
        };
        button.Click += handler;
        return button;
    }

    private Button CreateMemoryActionButton(string label, RichTextBox messageText, RoutedEventHandler handler)
    {
        var button = CreateExportButton(label, messageText, handler);
        button.ToolTip = label == "Sửa" ? "Chỉnh sửa nội dung và danh mục" : "Ghim hoặc bỏ ghim ghi nhớ";
        return button;
    }

    private static void UpdateMessageDocument(
        RichTextBox textBox,
        string text,
        MemoryEntry entry,
        int memoryTextOffset,
        bool isUser)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            Foreground = isUser ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 34, 48))
        };
        foreach (var inline in CreateMessageInlines(text, entry, memoryTextOffset))
        {
            paragraph.Inlines.Add(inline);
        }

        textBox.Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Blocks = { paragraph }
        };
    }

    private static IReadOnlyList<MemoryHighlight> MapHighlights(
        string oldText,
        string newText,
        IReadOnlyList<MemoryHighlight> highlights)
    {
        var prefixLength = 0;
        while (prefixLength < oldText.Length
               && prefixLength < newText.Length
               && oldText[prefixLength] == newText[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < oldText.Length - prefixLength
               && suffixLength < newText.Length - prefixLength
               && oldText[oldText.Length - suffixLength - 1] == newText[newText.Length - suffixLength - 1])
        {
            suffixLength++;
        }

        var oldSuffixStart = oldText.Length - suffixLength;
        var newSuffixStart = newText.Length - suffixLength;
        var mapped = new List<MemoryHighlight>();
        foreach (var highlight in highlights)
        {
            var start = highlight.Start;
            var end = highlight.Start + highlight.Length;
            var prefixEnd = Math.Min(end, prefixLength);
            if (prefixEnd > start)
            {
                mapped.Add(highlight with { Length = prefixEnd - start });
            }

            var suffixStart = Math.Max(start, oldSuffixStart);
            if (end > suffixStart)
            {
                mapped.Add(highlight with
                {
                    Start = newSuffixStart + (suffixStart - oldSuffixStart),
                    Length = end - suffixStart
                });
            }
        }

        return MemoryStore.NormalizeHighlights(newText, mapped);
    }

    private void RefreshMemoryMessages(MemoryEntry updatedEntry)
    {
        foreach (var (textBox, message) in _chatMessages.ToArray())
        {
            if (message.MemoryEntry is not { } currentEntry
                || !string.Equals(currentEntry.Id, updatedEntry.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contentStart = Math.Clamp(message.MemoryTextOffset, 0, message.DisplayText.Length);
            var contentEnd = Math.Min(contentStart + currentEntry.Text.Length, message.DisplayText.Length);
            var updatedText = message.DisplayText[..contentStart]
                + updatedEntry.Text
                + message.DisplayText[contentEnd..];
            if (!string.Equals(updatedText, message.DisplayText, StringComparison.Ordinal))
            {
                UpdateMessageDocument(textBox, updatedText, updatedEntry, message.MemoryTextOffset, message.IsUser);
            }

            if (message.PinButton is not null)
            {
                message.PinButton.Content = updatedEntry.IsPinned ? "Bỏ ghim" : "Ghim";
            }

            message.PinIndicator.Text = updatedEntry.IsPinned ? "Đã ghim" : string.Empty;
            message.CategoryIndicator.Text = updatedEntry.Category ?? string.Empty;
            _chatMessages[textBox] = message with
            {
                DisplayText = updatedText,
                MemoryEntry = updatedEntry
            };
        }

        if (_selectedMemoryToDelete is { } selectedMemory
            && string.Equals(selectedMemory.Id, updatedEntry.Id, StringComparison.OrdinalIgnoreCase))
        {
            _selectedMemoryToDelete = updatedEntry;
            UpdateDeleteButton();
        }
    }

    private static List<Inline> CreateMessageInlines(
        string text,
        MemoryEntry? memoryEntry,
        int memoryTextOffset)
    {
        if (memoryEntry?.Highlights is not { Count: > 0 } highlights)
        {
            return [new Run(text)];
        }

        var normalized = MemoryStore.NormalizeHighlights(memoryEntry.Text, highlights);
        var boundaries = new SortedSet<int> { 0, text.Length };
        foreach (var highlight in normalized)
        {
            var start = Math.Clamp(memoryTextOffset + highlight.Start, 0, text.Length);
            var end = Math.Clamp((long)memoryTextOffset + highlight.Start + highlight.Length, 0, text.Length);
            boundaries.Add(start);
            boundaries.Add((int)end);
        }

        var points = boundaries.ToArray();
        var inlines = new List<Inline>();
        for (var index = 0; index < points.Length - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            if (end <= start)
            {
                continue;
            }

            var highlight = normalized.LastOrDefault(item =>
                start >= memoryTextOffset + item.Start
                && start < (long)memoryTextOffset + item.Start + item.Length);
            var run = new Run(text[start..end]);
            if (highlight is not null)
            {
                run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(highlight.Color)!);
            }

            inlines.Add(run);
        }

        return inlines;
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

    private void UpdateChatActionButtons()
    {
        UpdateDeleteButton();
        var hasSelection = _selectedChatText is not null
            && _selectedTextStart is not null
            && _selectedTextEnd is not null
            && new TextRange(_selectedTextStart, _selectedTextEnd).Text.Any(character => !char.IsWhiteSpace(character));
        ColorButton.IsEnabled = hasSelection;
        ColorButton.Opacity = hasSelection ? 1 : 0.5;
    }

    private void ClearSelectedChatText()
    {
        _selectedChatText = null;
        _selectedTextStart = null;
        _selectedTextEnd = null;
        ColorPalettePopup.IsOpen = false;
        if (ColorButton is not null)
        {
            UpdateChatActionButtons();
        }
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

    private sealed record ChatMessage(
        DateTimeOffset SentAt,
        MemoryEntry? MemoryEntry,
        int MemoryTextOffset,
        Grid Row,
        string DisplayText,
        bool IsUser,
        Button? PinButton,
        TextBlock PinIndicator,
        TextBlock CategoryIndicator);
}
