// ViewModels/MainViewModel.cs
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Services;
using factory_automation_system_FAS_.Utils;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace factory_automation_system_FAS_.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        // ===== 기존: Zoom/Pan =====
        public MapViewportModel Viewport { get; } = new();

        public ICommand ZoomCommand { get; }
        public ICommand BeginPanCommand { get; }
        public ICommand PanCommand { get; }
        public ICommand EndPanCommand { get; }
        public ICommand ResetViewCommand { get; }
        public ICommand AddStockCommand { get; }
        public ICommand OpenProductionQueryCommand { get; }
        public ICommand NavigateHomeCommand { get; }

        // 화면 전환 인덱스 (0=메인, 1=생산조회)
        private int _mainTabIndex = 0;
        public int MainTabIndex
        {
            get => _mainTabIndex;
            set { _mainTabIndex = value; OnPropertyChanged(); }
        }

        // ProductionQueryView에 꽂아줄 VM
        public ProductionQueryViewModel ProductionQueryVm { get; } = new();




        // ===== 새로: Simulation =====
        private readonly SimulationEngine _sim = new();
        private readonly DispatcherTimer _timer = new();
        private readonly Stopwatch _sw = new();

        // 카트 표시용 (Canvas.Left/Top 바인딩)
        private double _cartX;
        public double CartX
        {
            get => _cartX;
            private set
            {
                _cartX = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CartLoadX));
            }
        }

        private double _cartY;
        public double CartY
        {
            get => _cartY;
            private set
            {
                _cartY = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CartLoadY));
            }
        }

        private bool _cartHasLoad;
        // 카트 위에 제품 점을 올릴 위치(카트 중앙쯤)
        public double CartLoadX => CartX + 22;
        public double CartLoadY => CartY + 22;

        public bool CartHasLoad
        {
            get => _cartHasLoad;
            private set
            {
                _cartHasLoad = value;
                OnPropertyChanged();
                // (CartLoadX/Y는 위치지만, load 켜질 때도 다시 그리게 안전빵)
                OnPropertyChanged(nameof(CartLoadX));
                OnPropertyChanged(nameof(CartLoadY));
            }
        }


        // 제품 점들(빨간 원) 표시용
        public ObservableCollection<ProductDotVm> ProductDots { get; } = new();

        // (옵션) 우측 패널 등에서 텍스트 표시용
        private int _outputStock;
        public int OutputStock { get => _outputStock; private set { _outputStock = value; OnPropertyChanged(); } }

        private int _rackAStock;
        public int RackAStock { get => _rackAStock; private set { _rackAStock = value; OnPropertyChanged(); } }

        private int _rackBStock;
        public int RackBStock { get => _rackBStock; private set { _rackBStock = value; OnPropertyChanged(); } }

        public MainViewModel()
        {
            ZoomCommand = new RelayCommand<ZoomArgs>(a => Viewport.ZoomAt(a.Delta, a.MousePosOnCanvas));
            BeginPanCommand = new RelayCommand<PanArgs>(a => Viewport.BeginPan(a.MousePosOnHost));
            PanCommand = new RelayCommand<PanArgs>(a => Viewport.PanTo(a.MousePosOnHost));
            EndPanCommand = new RelayCommand(() => Viewport.EndPan());
            ResetViewCommand = new RelayCommand(() => Viewport.Reset());
            AddStockCommand = new RelayCommand<StationId>(id =>
            {
                MessageBox.Show($"AddStockCommand 들어옴: {id}");
                _sim.AddStock(id, 1);
                MessageBox.Show($"추가 후 OutputStock={_sim.Output.Stock}");
                SyncFromSim();
            });

            OpenProductionQueryCommand = new RelayCommand(() => MainTabIndex = 1);
            NavigateHomeCommand = new RelayCommand(() => MainTabIndex = 0);


            // ===== Simulation timer =====
            _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps 느낌
            _timer.Tick += (_, __) => OnTick();

            _sw.Start();
            _timer.Start();

            // 최초 1회 렌더 동기화
            SyncFromSim();
            MainTabIndex = 0;

        }

        private void OnTick()
        {
            double dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();

            // dt가 비정상적으로 큰 경우(디버깅 중 멈춤) 클램프
            if (dt > 0.1) dt = 0.1;

            _sim.Tick(dt);
            SyncFromSim();
        }

        private void SyncFromSim()
        {
            // 1) 카트
            // 카트 사각형을 "중심 기준"으로 두면 보기 좋으니,
            // 여기서 top-left로 변환해서 바인딩해도 됨.
            const double cartSize = 70;
            CartX = _sim.Cart.Position.X - (cartSize / 2);
            CartY = _sim.Cart.Position.Y - (cartSize / 2);
            CartHasLoad = _sim.Cart.HasLoad;

            // 2) 재고 카운트(옵션)
            OutputStock = _sim.Output.Stock;
            RackAStock = _sim.RackA.Stock;
            RackBStock = _sim.RackB.Stock;

            // 3) “렌더링용 제품 점” 좌표 재생성
            RebuildProductDots();
        }

        private void RebuildProductDots()
        {
            ProductDots.Clear();

            // ✅ 각 스테이션 “표시 영역(사각형)”을 XAML의 Border 위치/크기와 맞춤
            // RackA : Left=90,  Top=90,  W=320, H=130
            // RackB : Left=520, Top=90,  W=320, H=130
            // Output: Left=820, Top=650, W=380, H=190

            AddStockDots(StationId.RackA, _sim.RackA.Stock, left: 90, top: 90, width: 320, height: 130);
            AddStockDots(StationId.RackB, _sim.RackB.Stock, left: 520, top: 90, width: 320, height: 130);
            AddStockDots(StationId.Output, _sim.Output.Stock, left: 820, top: 650, width: 380, height: 190);

            // 카트가 들고 있는 제품 1개(카트 위에 표시)
            if (CartHasLoad)
            {
                ProductDots.Add(new ProductDotVm
                {
                    X = CartX + 22, // 카트 내부 적당한 위치
                    Y = CartY + 22
                });
            }
        }

        private void AddStockDots(StationId id, int stock, double left, double top, double width, double height)
        {
            // 점 크기/간격
            const double dot = 26;
            const double gap = 8;

            // 스테이션 안쪽 여백(카드 안에서 아래쪽에 쌓이게)
            double paddingL = 18;
            double paddingT = 56; // 텍스트 영역 피해서 아래쪽
            double paddingB = 14;

            double startX = left + paddingL;
            double startY = top + paddingT;

            double usableW = Math.Max(0, width - paddingL * 2);
            double usableH = Math.Max(0, height - paddingT - paddingB);

            int cols = (int)Math.Max(1, Math.Floor((usableW + gap) / (dot + gap)));
            int rows = (int)Math.Max(1, Math.Floor((usableH + gap) / (dot + gap)));
            int max = cols * rows;

            // 너무 많이 쌓이면 UI가 터지니까 MVP에서는 상한(예: 30)
            int count = Math.Min(stock, Math.Min(max, 30));

            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;

                double x = startX + col * (dot + gap);
                double y = startY + row * (dot + gap);

                ProductDots.Add(new ProductDotVm { X = x, Y = y });
            }
        }
     

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
