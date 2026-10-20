using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;
using Microsoft.Win32;

namespace LynhCad.Manhole.Plugin.UI
{
    /// <summary>Hộp thoại nhập thông số hố ga. Binding thẳng vào bản sao ManholeParams.</summary>
    public partial class ManholeWindow : Window
    {
        private ObservableCollection<PipeSpec> pipes;

        /// <summary>Thông số sau khi người dùng bấm Draw (đã kiểm tra hợp lệ).</summary>
        public ManholeParams Params { get; private set; }

        public ManholeWindow(ManholeParams initial)
        {
            InitializeComponent();

            cbType.ItemsSource = Enum.GetValues(typeof(ManholeType));
            colRole.ItemsSource = Enum.GetValues(typeof(PipeRole));
            colWall.ItemsSource = Enum.GetValues(typeof(WallSide));
            foreach (var cb in new[] { cbWallV, cbWallH, cbBaseB, cbBaseT, cbRoofB, cbRoofT, cbTrim })
                cb.ItemsSource = RebarCatalog.Sizes;

            Bind(initial ?? ManholeParams.SampleRectangular());
        }

        private void Bind(ManholeParams p)
        {
            Params = p.Clone();
            pipes = new ObservableCollection<PipeSpec>(Params.Pipes);
            DataContext = Params;
            gridPipes.ItemsSource = pipes;
            UpdateTypePanels();
            txtStatus.Text = "";
        }

        private bool IsRect { get { return Params != null && Params.Type == ManholeType.RectangularCast; } }

        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbType.SelectedItem is ManholeType)
                Params.Type = (ManholeType)cbType.SelectedItem;
            UpdateTypePanels();
        }

        /// <summary>Ẩn/hiện các trường riêng của hố chữ nhật / hố tròn.</summary>
        private void UpdateTypePanels()
        {
            if (grpRect == null || Params == null) return;
            bool rect = IsRect;
            grpRect.Visibility = rect ? Visibility.Visible : Visibility.Collapsed;
            grpCirc.Visibility = rect ? Visibility.Collapsed : Visibility.Visible;
            var openVis = rect ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement el in new UIElement[] { lblOpenX, txtOpenX, unitOpenX, lblOpenY, txtOpenY, unitOpenY })
                el.Visibility = openVis;

            colAngle.Visibility = rect ? Visibility.Collapsed : Visibility.Visible;
            colWall.Visibility = rect ? Visibility.Visible : Visibility.Collapsed;
            colOffset.Visibility = rect ? Visibility.Visible : Visibility.Collapsed;
            txtPipeHint.Text = rect
                ? "Wall: N = +Y, E = +X. Offset along the wall from manhole centre (+X / +Y). Section A-A is cut along the outlet pipe."
                : "Angle in plan: 0° = +X (east), counter-clockwise. Section A-A is cut along the outlet pipe.";
        }

        private void OnAddPipe(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            int n = pipes.Count + 1;
            var last = pipes.LastOrDefault();
            pipes.Add(new PipeSpec
            {
                Mark = "P" + n,
                Role = PipeRole.Inlet,
                NominalDiameter = last != null ? last.NominalDiameter : 300,
                OutsideDiameter = last != null ? last.OutsideDiameter : 360,
                Material = last != null ? last.Material : "RCP",
                InvertLevel = last != null ? Math.Round(last.InvertLevel + 0.05, 3) : Params.CoverLevel - 2.0,
                AngleDeg = 90,
                Wall = WallSide.North,
            });
        }

        private void OnRemovePipe(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            foreach (var p in gridPipes.SelectedItems.OfType<PipeSpec>().ToList()) pipes.Remove(p);
        }

        private void OnSampleRect(object sender, RoutedEventArgs e) { Bind(ManholeParams.SampleRectangular()); }
        private void OnSampleCirc(object sender, RoutedEventArgs e) { Bind(ManholeParams.SampleCircular()); }

        private void OnLoadJson(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Manhole parameters (*.json)|*.json", Title = "Load manhole parameters" };
            if (dlg.ShowDialog(this) != true) return;
            try { Bind(ParamsJson.Load(dlg.FileName)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load JSON", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnSaveJson(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            var dlg = new SaveFileDialog
            {
                Filter = "Manhole parameters (*.json)|*.json",
                FileName = (string.IsNullOrEmpty(Params.Id) ? "manhole" : Params.Id) + ".json",
            };
            if (dlg.ShowDialog(this) != true) return;
            try { ParamsJson.Save(Collect(), dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save JSON", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnDraw(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            var p = Collect();
            var g = new ManholeGeometry(p);
            if (!g.IsValid)
            {
                txtStatus.Text = "Cannot draw:\n• " + string.Join("\n• ", g.Errors);
                return;
            }
            Params = p;
            DialogResult = true;
        }

        private ManholeParams Collect()
        {
            Params.Pipes = pipes.ToList();
            return Params;
        }

        /// <summary>Đẩy giá trị đang gõ dở (ô đang focus, dòng DataGrid đang sửa) vào binding.</summary>
        private void CommitEdits()
        {
            gridPipes.CommitEdit(DataGridEditingUnit.Row, true);
            var tb = Keyboard.FocusedElement as TextBox;
            if (tb != null)
            {
                var be = tb.GetBindingExpression(TextBox.TextProperty);
                if (be != null) be.UpdateSource();
            }
        }
    }
}
