using System.Windows;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace newgdq.Views
{
    /// <summary>
    /// 实时速度曲线窗口，贴在主窗口下方。
    /// 双线面积图：橙=速度(字/分)，灰=击键(键/秒×10)。
    /// </summary>
    public partial class SpeedChartWindow : Window
    {
        private readonly Window _owner;
        private readonly AreaSeries _speedArea;
        private readonly PlotModel _model;

        public SpeedChartWindow(Window owner)
        {
            InitializeComponent();
            _owner = owner;
            Owner = owner;

            _model = new PlotModel
            {
                Background          = OxyColor.FromRgb(0x1E, 0x1E, 0x1E),
                PlotAreaBorderColor = OxyColors.Transparent,
                TextColor           = OxyColor.FromRgb(0x88, 0x88, 0x88),
                PlotMargins         = new OxyThickness(36, 8, 12, 24),
            };
            _model.Axes.Add(new LinearAxis
            {
                Position           = AxisPosition.Bottom,
                AxislineColor      = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
                Minimum            = 0,
                TickStyle          = TickStyle.None,
                IsAxisVisible      = true,
                FontSize           = 10,
            });
            _model.Axes.Add(new LinearAxis
            {
                Position           = AxisPosition.Left,
                AxislineColor      = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
                Minimum            = 0,
                TickStyle          = TickStyle.None,
                IsAxisVisible      = true,
                FontSize           = 10,
            });

            _speedArea = new AreaSeries
            {
                Color           = OxyColor.FromRgb(0xFF, 0xB0, 0x2E),
                StrokeThickness = 2,
                MarkerType      = MarkerType.None,
                Fill            = OxyColor.FromArgb(0x44, 0xFF, 0xB0, 0x2E),
                InterpolationAlgorithm = InterpolationAlgorithms.CanonicalSpline,
            };
            _model.Series.Add(_speedArea);
            Plot.Model = _model;

            owner.LocationChanged += (s, e) => UpdatePosition();
            owner.SizeChanged     += (s, e) => UpdatePosition();
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (_owner == null) return;
            Left = _owner.Left + (_owner.Width - Width) / 2;
            Top  = _owner.Top + _owner.Height + 4;
        }

        public void Reset()
        {
            _speedArea.Points.Clear();
            _model.InvalidatePlot(true);
        }

        /// <summary>追加一个采样点（秒, 速度字/分）。</summary>
        public void AddPoint(double seconds, double speed)
        {
            _speedArea.Points.Add(new DataPoint(seconds, speed));
            _model.InvalidatePlot(true);
        }
    }
}
