using System.Windows;

namespace PersonalLearningAgent;

public partial class MemoryEditWindow : Window
{
    internal MemoryEditWindow(MemoryEntry entry)
    {
        InitializeComponent();
        CategoryInput.Text = entry.Category ?? string.Empty;
        CategoryInput.ToolTip = "Danh mục tùy chọn (ví dụ: C#, SQL, công việc)";
        ContentInput.Text = entry.Text;
        ContentInput.Focus();
        ContentInput.CaretIndex = ContentInput.Text.Length;
    }

    public string EditedText => ContentInput.Text.Trim();

    public string? EditedCategory => string.IsNullOrWhiteSpace(CategoryInput.Text)
        ? null
        : CategoryInput.Text.Trim();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ContentInput.Text))
        {
            MessageBox.Show(
                this,
                "Nội dung ghi nhớ không được để trống.",
                "Chỉnh sửa ghi nhớ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ContentInput.Focus();
            return;
        }

        DialogResult = true;
    }
}
