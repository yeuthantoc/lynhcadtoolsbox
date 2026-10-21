using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;
using Microsoft.Win32;

namespace LynhCad.Manhole.Plugin.UI
{
    /// <summary>Hộp thoại nhập thông số hố ga. Binding thẳng vào bản sao ManholeParams.</summary>
    public partial class ManholeWindow : Window
    {
        private ObservableCollection<PipeRow> pipes;
        private bool previewQueued;
        /// <summary>Thuộc tính ManholeParams (phần trước dấu chấm của đường dẫn binding) của ô đang có focus; null = không nổi bật gì.</summary>
        private string focusField;

        /// <summary>Thông số sau khi người dùng bấm Draw (đã kiểm tra hợp lệ).</summary>
        public ManholeParams Params { get; private set; }

        /// <param name="updateMode">true: sửa chi tiết đã vẽ (nút Draw đổi thành Update).</param>
        public ManholeWindow(ManholeParams initial, bool updateMode = false)
        {
            InitializeComponent();
            if (updateMode)
            {
                btnDraw.Content = "Update";
                Title += " - Update " + (initial != null ? initial.Id : "");
            }

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
            pipes = new ObservableCollection<PipeRow>(Params.Pipes.Select(Watch));
            pipes.CollectionChanged += (s, e) => QueuePreview();
            DataContext = Params;
            gridPipes.ItemsSource = pipes;
            UpdateTypePanels();
            txtStatus.Text = "";
            QueuePreview();
        }

        private bool IsRect { get { return Params != null && Params.Type == ManholeType.RectangularCast; } }

        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbType.SelectedItem is ManholeType)
                Params.Type = (ManholeType)cbType.SelectedItem;
            UpdateTypePanels();
            QueuePreview();
        }

        /// <summary>Ẩn/hiện các trường riêng của hố chữ nhật / hố tròn.</summary>
        private void UpdateTypePanels()
        {
            if (grpRect == null || txtCastWall == null || Params == null) return;
            bool rect = IsRect;
            grpRect.Visibility = rect ? Visibility.Visible : Visibility.Collapsed;
            grpCirc.Visibility = rect ? Visibility.Collapsed : Visibility.Visible;

            // hố tròn: đúc sẵn dùng chiều dày vách + chiều cao đốt; đổ tại chỗ dùng chiều dày tường BTCT
            bool precast = Params.Type == ManholeType.CircularPrecast;
            grpCirc.Header = precast ? "Circular precast" : "Circular cast in-situ";
            var precastVis = precast ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement el in new UIElement[] { lblPrecastWall, txtPrecastWall, lblRing, txtRing, unitRing })
                el.Visibility = precastVis;
            foreach (UIElement el in new UIElement[] { lblCastWall, txtCastWall })
                el.Visibility = precast ? Visibility.Collapsed : Visibility.Visible;
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

        // ---------------------------------------------------------------- xem trước vị trí ống

        /// <summary>Vẽ lại preview sau khi giá trị đang sửa đã vào binding (gộp nhiều sự kiện liên tiếp thành một lần vẽ).</summary>
        private void QueuePreview()
        {
            if (previewQueued) return;
            previewQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RenderPreview));
        }

        /// <summary>Vẽ cả hai khung xem trước (ống trên mặt bằng, thép trên mặt cắt); khung ở tab đang ẩn chưa có kích thước thì bỏ qua.</summary>
        private void RenderPreview()
        {
            previewQueued = false;
            if (cvPreview == null || cvRebarPreview == null || cvGeneralPreview == null || Params == null || pipes == null) return;
            ManholeParams p;
            try
            {
                p = Params.Clone();
                p.Pipes = pipes.Select(x => x.Spec.Clone()).ToList();
            }
            catch (Exception ex)
            {
                txtPreviewInfo.Text = txtRebarPreviewInfo.Text = txtGeneralPreviewInfo.Text = "Không xem trước được: " + ex.Message;
                return;
            }
            try
            {
                var dp = DimensionPreview.Build(p);
                PreviewRenderer.Render(cvGeneralPreview, dp.Model, focusField == null ? null : new[] { focusField });
                txtGeneralPreviewInfo.Text = dp.Errors.Count == 0 ? "" : "• " + string.Join("\n• ", dp.Errors);
            }
            catch (Exception ex)
            {
                cvGeneralPreview.Children.Clear();
                txtGeneralPreviewInfo.Text = "Không xem trước được: " + ex.Message;
            }
            try
            {
                var pv = PipeLayoutPreview.Build(p);
                PreviewRenderer.Render(cvPreview, pv.Model);
                txtPreviewInfo.Text = pv.Geometry.Errors.Count == 0 ? "" : "• " + string.Join("\n• ", pv.Geometry.Errors);
            }
            catch (Exception ex)
            {
                // preview không bao giờ được làm hỏng hộp thoại
                cvPreview.Children.Clear();
                txtPreviewInfo.Text = "Không xem trước được: " + ex.Message;
            }
            try
            {
                var rp = RebarLayoutPreview.Build(p);
                PreviewRenderer.Render(cvRebarPreview, rp.Model, RebarLayoutPreview.KeysForField(focusField));
                txtRebarPreviewInfo.Text = rp.Errors.Count == 0 ? "" : "• " + string.Join("\n• ", rp.Errors);
            }
            catch (Exception ex)
            {
                cvRebarPreview.Children.Clear();
                txtRebarPreviewInfo.Text = "Không xem trước được: " + ex.Message;
            }
        }

        /// <summary>Ô nhập mất focus / checkbox / combobox đổi (tab General, Reinforcement): binding đã cập nhật → vẽ lại preview.</summary>
        private void OnFieldChanged(object sender, RoutedEventArgs e) { QueuePreview(); }

        private void OnFieldSelectionChanged(object sender, SelectionChangedEventArgs e) { QueuePreview(); }

        /// <summary>
        /// Ô nhập (TextBox / ComboBox / CheckBox) ở tab General hoặc Reinforcement nhận focus: lấy thuộc tính ManholeParams
        /// từ binding của ô (vd "WallThickness", "WallVertical.Spacing" → "WallVertical") để preview làm nổi bật phần tương ứng.
        /// </summary>
        private void OnFieldFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            string field = BindingFieldOf(e.NewFocus as FrameworkElement);
            if (field == focusField) return;
            focusField = field;
            QueuePreview();
        }

        private static string BindingFieldOf(FrameworkElement el)
        {
            DependencyProperty dp = null;
            if (el is TextBox) dp = TextBox.TextProperty;
            else if (el is ComboBox) dp = ComboBox.SelectedItemProperty;
            else if (el is CheckBox) dp = CheckBox.IsCheckedProperty;
            if (dp == null) return null;
            var be = el.GetBindingExpression(dp);
            if (be == null || be.ParentBinding == null || be.ParentBinding.Path == null) return null;
            string path = be.ParentBinding.Path.Path ?? "";
            int dot = path.IndexOf('.');
            return dot > 0 ? path.Substring(0, dot) : (path.Length > 0 ? path : null);
        }

        private void OnPipeCellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { QueuePreview(); }

        /// <summary>Dòng ống: mọi thay đổi ô (kể cả OD tự điền) → vẽ lại preview.</summary>
        private PipeRow Watch(PipeSpec spec)
        {
            var row = new PipeRow(spec);
            row.PropertyChanged += (s, e) => QueuePreview();
            return row;
        }

        /// <summary>Ô DN / OD / vật liệu vào chế độ sửa: đưa focus vào ComboBox và mở danh sách ngay.</summary>
        private void OnPipePreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            var cb = e.EditingElement as ComboBox;
            if (cb == null) return;
            cb.Focus();
            cb.IsDropDownOpen = true;
        }

        private void OnPipeCurrentCellChanged(object sender, EventArgs e) { QueuePreview(); }

        private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) { QueuePreview(); }

        /// <summary>Thông số tab General có thể đã đổi → vẽ lại khi chuyển tab. Bỏ qua sự kiện nổi bọt từ ComboBox / DataGrid.</summary>
        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, tabs)) QueuePreview();
        }

        private void OnAddPipe(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            int n = pipes.Count + 1;
            var last = pipes.LastOrDefault();
            pipes.Add(Watch(new PipeSpec
            {
                Mark = "P" + n,
                Role = PipeRole.Inlet,
                NominalDiameter = last != null ? last.NominalDiameter : 300,
                OutsideDiameter = last != null ? last.OutsideDiameter : PipeCatalog.OutsideDiameter("RCP", 300),
                Material = last != null ? last.Material : "RCP",
                InvertLevel = last != null ? Math.Round(last.InvertLevel + 0.05, 3) : Params.CoverLevel - 2.0,
                AngleDeg = 90,
                Wall = WallSide.North,
            }));
        }

        private void OnRemovePipe(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            foreach (var p in gridPipes.SelectedItems.OfType<PipeRow>().ToList()) pipes.Remove(p);
        }

        private void OnSampleRect(object sender, RoutedEventArgs e) { Bind(ManholeParams.SampleRectangular()); }
        private void OnSampleCirc(object sender, RoutedEventArgs e) { Bind(ManholeParams.SampleCircular()); }
        private void OnSampleCircCast(object sender, RoutedEventArgs e) { Bind(ManholeParams.SampleCircularCast()); }

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
            Params.Pipes = pipes.Select(r => r.Spec).ToList();
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
