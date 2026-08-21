using System.Windows;
using ViewRenameTool.ViewModels;

namespace ViewRenameTool.Views
{
    public partial class RenameWindow : Window
    {
        public RenameViewModel ViewModel { get; }

        public RenameWindow(RenameViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
