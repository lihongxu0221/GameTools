using System.Windows;

namespace GameTools.App.Views;

/// <summary>
/// 主窗口视图。
/// </summary>
/// <remarks>
/// 纯视图职责：仅负责 XAML 加载与 DataContext 关联，全部行为通过
/// <see cref="ViewModels.MainViewModel"/> 的命令与绑定属性驱动，
/// 便于在无 UI 环境下对 ViewModel 进行单元测试。
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// 初始化主窗口。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}