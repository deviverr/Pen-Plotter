using System.Windows;
using PlotterControl.ViewModels;

namespace PlotterControl;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
