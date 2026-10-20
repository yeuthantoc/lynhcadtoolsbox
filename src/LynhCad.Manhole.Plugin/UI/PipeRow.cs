using System.ComponentModel;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Plugin.UI
{
    /// <summary>
    /// Một dòng của bảng ống trong hộp thoại: bọc PipeSpec và báo PropertyChanged để DataGrid cập nhật ô
    /// (PipeSpec là lớp dữ liệu thuần của Core, không có INotifyPropertyChanged).
    /// Đổi DN hoặc vật liệu → OD tự lấy từ PipeCatalog; danh sách chọn DN / OD / vật liệu cũng lấy từ đó.
    /// </summary>
    public sealed class PipeRow : INotifyPropertyChanged
    {
        public PipeSpec Spec { get; private set; }

        public PipeRow(PipeSpec spec) { Spec = spec; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify(params string[] names)
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in names) h(this, new PropertyChangedEventArgs(n));
        }

        public string Mark { get { return Spec.Mark; } set { Spec.Mark = value; Notify("Mark"); } }
        public PipeRole Role { get { return Spec.Role; } set { Spec.Role = value; Notify("Role"); } }

        public double NominalDiameter
        {
            get { return Spec.NominalDiameter; }
            set
            {
                if (Spec.NominalDiameter == value) return;
                Spec.NominalDiameter = value;
                Spec.OutsideDiameter = PipeCatalog.OutsideDiameter(Spec.Material, value);
                Notify("NominalDiameter", "OutsideDiameter", "OutsideDiameterOptions");
            }
        }

        public double OutsideDiameter { get { return Spec.OutsideDiameter; } set { Spec.OutsideDiameter = value; Notify("OutsideDiameter"); } }

        public string Material
        {
            get { return Spec.Material; }
            set
            {
                if (Spec.Material == value) return;
                Spec.Material = value;
                Spec.OutsideDiameter = PipeCatalog.OutsideDiameter(value, Spec.NominalDiameter);
                Notify("Material", "OutsideDiameter");
            }
        }

        public double InvertLevel { get { return Spec.InvertLevel; } set { Spec.InvertLevel = value; Notify("InvertLevel"); } }
        public double AngleDeg { get { return Spec.AngleDeg; } set { Spec.AngleDeg = value; Notify("AngleDeg"); } }
        public WallSide Wall { get { return Spec.Wall; } set { Spec.Wall = value; Notify("Wall"); } }
        public double Offset { get { return Spec.Offset; } set { Spec.Offset = value; Notify("Offset"); } }

        // Danh sách cho ComboBox trong ô (ItemsSource theo dòng)
        public double[] SizeOptions { get { return PipeCatalog.Sizes; } }
        public string[] MaterialOptions { get { return PipeCatalog.Materials; } }
        public double[] OutsideDiameterOptions { get { return PipeCatalog.OutsideDiameters(Spec.NominalDiameter); } }
    }
}
