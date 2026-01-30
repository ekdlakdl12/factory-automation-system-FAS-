// ViewModels/MainViewModel.cs
using factory_automation_system_FAS_.Models;
using factory_automation_system_FAS_.Services;
using factory_automation_system_FAS_.Utils;
using factory_automation_system_FAS_.ViewModels.MapEntities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
        public ICommand NavigateSettingsCommand { get; }
        public ICommand SelectEntityCommand { get; }
        public ICommand NudgeSelectedEntityCommand { get; }

        // ✅ Layout Edit / Snap
        public ICommand ToggleEditModeCommand { get; }
        public ICommand SetGridSizeCommand { get; }

        private bool _isEditMode = true; // ✅ 처음엔 바로 편집 가능하게 ON
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                _isEditMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsViewMode));
            }
        }
        public bool IsViewMode => !IsEditMode;

        private int _gridSize = 10;
        public int GridSize
        {
            get => _gridSize;
            set
            {
                if (value < 1) value = 1;
                if (value > 200) value = 200;
                _gridSize = value;
                OnPropertyChanged();
            }
        }

        // ✅ Export JSON
        public ICommand ExportMapEntitiesCommand { get; }

        // ===== New: Map Entities =====
        public ObservableCollection<MapEntityVM> MapEntities { get; } = new();
        private readonly Dictionary<string, MapEntityVM> _entityIndex = new();

        private MapEntityVM? _selectedEntity;
        public MapEntityVM? SelectedEntity
        {
            get => _selectedEntity;
            set
            {
                _selectedEntity = value;
                OnPropertyChanged();
            }
        }

        // (디버그용) 엔티티 수
        public int MapEntityCount => MapEntities.Count;

        // 화면 전환 인덱스 (0=메인, 1=생산조회, 2=설정)
        private int _mainTabIndex = 0;
        public int MainTabIndex
        {
            get => _mainTabIndex;
            set { _mainTabIndex = value; OnPropertyChanged(); }
        }

        public ProductionQueryViewModel ProductionQueryVm { get; } = new();
        public SettingsViewModel SettingsVm { get; } = new();

        // ===== Simulation =====
        private readonly SimulationEngine _sim = new();
        private readonly DispatcherTimer _timer = new();
        private readonly Stopwatch _sw = new();

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
        public double CartLoadX => CartX + 22;
        public double CartLoadY => CartY + 22;

        public bool CartHasLoad
        {
            get => _cartHasLoad;
            private set
            {
                _cartHasLoad = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CartLoadX));
                OnPropertyChanged(nameof(CartLoadY));
            }
        }

        public ObservableCollection<ProductDotVm> ProductDots { get; } = new();

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
                _sim.AddStock(id, 1);
                SyncFromSim();
            });

            OpenProductionQueryCommand = new RelayCommand(() => MainTabIndex = 1);
            NavigateHomeCommand = new RelayCommand(() => MainTabIndex = 0);
            NavigateSettingsCommand = new RelayCommand(() => MainTabIndex = 2);

            SelectEntityCommand = new RelayCommand<MapEntityVM>(vm =>
            {
                if (vm is null) return;
                SelectedEntity = vm;
            });

            NudgeSelectedEntityCommand = new RelayCommand<string>(dir =>
            {
                if (SelectedEntity is null) return;
                if (string.IsNullOrWhiteSpace(dir)) return;

                double step = 1;

                if (dir.StartsWith("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    step = 10;
                    dir = dir.Replace("Shift", "", StringComparison.OrdinalIgnoreCase);
                }

                switch (dir)
                {
                    case "Left": SelectedEntity.X -= step; break;
                    case "Right": SelectedEntity.X += step; break;
                    case "Up": SelectedEntity.Y -= step; break;
                    case "Down": SelectedEntity.Y += step; break;
                    default: return;
                }

                // ✅ edit mode에서는 nudge 후 grid snap
                if (IsEditMode && GridSize > 1)
                {
                    SelectedEntity.X = Snap(SelectedEntity.X, GridSize);
                    SelectedEntity.Y = Snap(SelectedEntity.Y, GridSize);
                }

                SelectedEntity.Touch();
            });

            // ✅ Edit mode / grid
            ToggleEditModeCommand = new RelayCommand(() =>
            {
                IsEditMode = !IsEditMode;
            });

            SetGridSizeCommand = new RelayCommand<int>(s =>
            {
                if (s < 1) s = 1;
                if (s > 200) s = 200;
                GridSize = s;
            });

            // ✅ Export command
            ExportMapEntitiesCommand = new RelayCommand(() => ExportMapEntitiesToJson());

            // ===== Simulation timer =====
            _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
            _timer.Tick += (_, __) => OnTick();

            _sw.Start();
            _timer.Start();

            // ✅ 여기서 JSON 로드 (장비 + 레이아웃)
            LoadMapEntitiesFromJson();

            SyncFromSim();
            MainTabIndex = 0;
        }

        private void OnTick()
        {
            double dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();

            if (dt > 0.1) dt = 0.1;

            _sim.Tick(dt);
            SyncFromSim();
        }

        private void SyncFromSim()
        {
            const double cartSize = 70;
            CartX = _sim.Cart.Position.X - (cartSize / 2);
            CartY = _sim.Cart.Position.Y - (cartSize / 2);
            CartHasLoad = _sim.Cart.HasLoad;

            OutputStock = _sim.Output.Stock;
            RackAStock = _sim.RackA.Stock;
            RackBStock = _sim.RackB.Stock;

            RebuildProductDots();
        }

        private void RebuildProductDots()
        {
            ProductDots.Clear();

            if (CartHasLoad)
            {
                ProductDots.Add(new ProductDotVm
                {
                    X = CartX + 22,
                    Y = CartY + 22
                });
            }
        }

        // =========================================================
        // Step 2: JSON Seed Loading
        // =========================================================

        private void LoadMapEntitiesFromJson()
        {
            // 중복 로드 방지
            if (MapEntities.Count > 0) return;

            try
            {
                string jsonPath = GetSeedJsonPath();
                if (!File.Exists(jsonPath))
                {
                    InitMapEntities_FallbackSeed();
                    return;
                }

                string json = File.ReadAllText(jsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var items = JsonSerializer.Deserialize<List<MapEntitySeedDto>>(json, options) ?? new List<MapEntitySeedDto>();
                if (items.Count == 0)
                {
                    InitMapEntities_FallbackSeed();
                    return;
                }

                foreach (var dto in items)
                {
                    var vm = CreateVmFromDto(dto);
                    if (vm != null)
                        AddEntity(vm);
                }

                // OPS UI: Prefer focusing the viewport to the meaningful region (entities bbox)
                // instead of fitting the entire 3100x1700 map (which contains large blank areas).
                ApplyOpsFocusRectFromEntities();
            }
            catch
            {
                InitMapEntities_FallbackSeed();
            }
        }

        private static string GetSeedJsonPath()
        {
            // 실행 폴더 기준: Data/map_entities.seed.json
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "Data", "map_entities.seed.json");
        }

        private void AddEntity(MapEntityVM vm)
        {
            if (_entityIndex.ContainsKey(vm.Id))
                return;

            MapEntities.Add(vm);
            _entityIndex[vm.Id] = vm;

            vm.PropertyChanged += (_, __) =>
            {
                // 디버그 카운트 갱신 등
                OnPropertyChanged(nameof(MapEntityCount));
            };

            OnPropertyChanged(nameof(MapEntityCount));
        }

        private void InitMapEntities_FallbackSeed()
        {
            // 실패 시 최소 엔티티(디버그용)
            MapEntities.Clear();
            _entityIndex.Clear();

            var c1 = new ConveyorVM("In_Conv_1")
            {
                DisplayName = "In Conv 1",
                X = 200,
                Y = 200,
                Width = 140,
                Height = 18,
                Direction = Direction.Right,
                Lane = 1,
                Status = EntityStatus.Running,
                Speed = 1.2,
                ZIndex = 10,
                HasItem = true
            };

            var s1 = new SensorVM("ItemDetect_1")
            {
                DisplayName = "Item Detect 1",
                X = 360,
                Y = 195,
                Width = 14,
                Height = 14,
                SensorType = SensorType.ItemDetect,
                Status = EntityStatus.Normal,
                IsTriggered = true,
                ZIndex = 20
            };

            var cam = new CameraVM("Camera_1")
            {
                DisplayName = "CCTV 1",
                X = 420,
                Y = 180,
                Width = 18,
                Height = 18,
                Status = EntityStatus.Offline,
                IsStreaming = false,
                ZIndex = 20
            };

            AddEntity(c1);
            AddEntity(s1);
            AddEntity(cam);

            ApplyOpsFocusRectFromEntities();
        }

        /// <summary>
        /// Compute a world-space ROI (entities bounding box + padding) and set it on the viewport.
        /// This is OPS UI behavior: initial view should highlight the operational area, not the whole map.
        /// </summary>
        private void ApplyOpsFocusRectFromEntities()
        {
            if (MapEntities.Count == 0) return;

            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;

            foreach (var e in MapEntities)
            {
                // Entities are positioned by top-left (X,Y). Include width/height so the ROI covers the full icon/shape.
                double x1 = e.X;
                double y1 = e.Y;
                double x2 = e.X + Math.Max(1, e.Width);
                double y2 = e.Y + Math.Max(1, e.Height);

                if (x1 < minX) minX = x1;
                if (y1 < minY) minY = y1;
                if (x2 > maxX) maxX = x2;
                if (y2 > maxY) maxY = y2;
            }

            if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY))
                return;

            // World padding (in map pixels). Tune for OPS readability.
            const double pad = 260;

            minX -= pad;
            minY -= pad;
            maxX += pad;
            maxY += pad;

            // Clamp into overall content bounds to avoid excessive empty margins.
            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(Viewport.ContentWidth, maxX);
            maxY = Math.Min(Viewport.ContentHeight, maxY);

            double w = Math.Max(10, maxX - minX);
            double h = Math.Max(10, maxY - minY);

            Viewport.SetFocusRect(new Rect(minX, minY, w, h));
        }

        private MapEntityVM? CreateVmFromDto(MapEntitySeedDto dto)
        {
            if (dto is null) return null;
            var type = (dto.Type ?? "").Trim().ToLowerInvariant();
            var id = (dto.Id ?? "").Trim();

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
                return null;

            MapEntityVM? vm = type switch
            {
                "layoutzone" => new LayoutZoneVM(id),
                "conveyorblock" => new ConveyorBlockVM(id),
                "path" => new PathVM(id),
                "conveyor" => new ConveyorVM(id),
                "sensor" => new SensorVM(id),
                "camera" => new CameraVM(id),
                "barcode" => new BarcodeScannerVM(id),
                "qr" => new BarcodeScannerVM(id),
                _ => new DeviceVMImpl(id)
            };

            // 공통
            vm.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? id : dto.DisplayName!;
            vm.X = dto.X;
            vm.Y = dto.Y;
            vm.Width = dto.Width <= 0 ? vm.Width : dto.Width;
            vm.Height = dto.Height <= 0 ? vm.Height : dto.Height;
            vm.ZIndex = dto.ZIndex;
            vm.LabelOrientation = ParseLabelOrientation(dto.LabelOrientation);
            // ===== TEMP: Output(Out_*) coordinate correction (do not touch viewport) =====
            if (id.StartsWith("Out_", StringComparison.OrdinalIgnoreCase))
            {
                const double ax = 2.3820026466696116;
                const double bx = -922.8695059550137;
                const double ay = 0.6259361098070776;
                const double by = 410.84950036885516;

                vm.X = ax * vm.X + bx;
                vm.Y = ay * vm.Y + by;

                // If size mismatch is visible (your measurements show it is), scale size too:
                vm.Width = ax * vm.Width;
                vm.Height = ay * vm.Height;
            }

            // 대부분은 편집 가능
            vm.IsInteractive = true;

            // 타입별
            if (vm is LayoutZoneVM z)
            {
                z.IsInteractive = false; // 레이아웃은 기본적으로 드래그 금지(원하면 true로 바꿔도 됨)
                z.Kind = dto.Kind ?? "";
                z.LabelFontSize = dto.LabelFontSize <= 0 ? 18 : dto.LabelFontSize;
            }
            else if (vm is ConveyorBlockVM cb)
            {
                cb.ShowCenterLine = dto.ShowCenterLine;
            }
            else if (vm is PathVM p)
            {
                p.IsInteractive = false;
                p.Thickness = dto.Thickness <= 0 ? 6 : dto.Thickness;

                // points: "x,y x,y x,y"
                p.Points.Clear();
                var raw = (dto.Points ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var xy = part.Split(',');
                        if (xy.Length != 2) continue;
                        if (double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) &&
                            double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                        {
                            p.Points.Add(new Point(px, py));
                        }
                    }
                }
            }
            else if (vm is ConveyorVM c)
            {
                c.Lane = dto.Lane;
                c.Speed = dto.Speed;
                c.Direction = ParseDirection(dto.Direction);
                c.Status = ParseStatus(dto.Status);
                c.HasItem = dto.HasItem;
            }
            else if (vm is SensorVM s)
            {
                s.SensorType = ParseSensorType(dto.SensorType);
                s.IsTriggered = dto.IsTriggered;
                s.Status = ParseStatus(dto.Status);
            }
            else if (vm is CameraVM cam)
            {
                cam.IsStreaming = dto.IsStreaming;
                cam.Status = ParseStatus(dto.Status);
            }
            else if (vm is BarcodeScannerVM b)
            {
                b.LastCode = dto.LastCode ?? "";
                b.Status = ParseStatus(dto.Status);
            }
            else
            {
                vm.Status = ParseStatus(dto.Status);
                vm.HasItem = dto.HasItem;
            }

            return vm;
        }

        private static Direction ParseDirection(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Direction.None;
            return s.Trim().ToLowerInvariant() switch
            {
                "left" => Direction.Left,
                "right" => Direction.Right,
                "up" => Direction.Up,
                "down" => Direction.Down,
                _ => Direction.None
            };
        }

        private static SensorType ParseSensorType(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return SensorType.Unknown;
            return s.Trim().ToLowerInvariant() switch
            {
                "itemdetect" => SensorType.ItemDetect,
                "position" => SensorType.Position,
                "temperature" => SensorType.Temperature,
                "speed" => SensorType.Speed,
                "pressure" => SensorType.Pressure,
                _ => SensorType.Unknown
            };
        }

        private static EntityStatus ParseStatus(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return EntityStatus.Normal;
            return s.Trim().ToLowerInvariant() switch
            {
                "offline" => EntityStatus.Offline,
                "idle" => EntityStatus.Idle,
                "normal" => EntityStatus.Normal,
                "running" => EntityStatus.Running,
                "warning" => EntityStatus.Warning,
                "fault" => EntityStatus.Fault,
                _ => EntityStatus.Normal
            };
        }


        private static LabelOrientation ParseLabelOrientation(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return LabelOrientation.Horizontal;
            return s.Trim().ToLowerInvariant() switch
            {
                "v" => LabelOrientation.Vertical,
                "vert" => LabelOrientation.Vertical,
                "vertical" => LabelOrientation.Vertical,
                "h" => LabelOrientation.Horizontal,
                "horiz" => LabelOrientation.Horizontal,
                "horizontal" => LabelOrientation.Horizontal,
                _ => LabelOrientation.Horizontal
            };
        }

        private static double Snap(double v, int grid)
        {
            if (grid <= 1) return v;
            return Math.Round(v / grid) * grid;
        }
        private static (double x, double y, double w, double h) ToSeedSpace(MapEntityVM vm)
{
    double x = vm.X, y = vm.Y, w = vm.Width, h = vm.Height;

    if (vm.Id.StartsWith("Out_", StringComparison.OrdinalIgnoreCase))
    {
        const double ax = 2.3820026466696116;
        const double bx = -922.8695059550137;
        const double ay = 0.6259361098070776;
        const double by = 410.84950036885516;

        x = (x - bx) / ax;
        y = (y - by) / ay;
        w = w / ax;
        h = h / ay;
    }

    return (x, y, w, h);
}

        public void ExportMapEntitiesToJson()
        {
            try
            {
                var path = GetSeedJsonPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var list = new List<object>();

                foreach (var vm in MapEntities)
                {
                    // Layout
                    if (vm is LayoutZoneVM z)
                    {
                        list.Add(new
                        {
                            type = "layoutzone",
                            id = z.Id,
                            displayName = z.DisplayName,
                            x = z.X,
                            y = z.Y,
                            width = z.Width,
                            height = z.Height,
                            zIndex = z.ZIndex,
                            kind = z.Kind,
                            labelFontSize = z.LabelFontSize
                        });
                        continue;
                    }

                    if (vm is ConveyorBlockVM cb)
                    {
                        list.Add(new
                        {
                            type = "conveyorblock",
                            id = cb.Id,
                            displayName = cb.DisplayName,
                            x = cb.X,
                            y = cb.Y,
                            width = cb.Width,
                            height = cb.Height,
                            zIndex = cb.ZIndex,
                            showCenterLine = cb.ShowCenterLine
                        });
                        continue;
                    }

                    if (vm is PathVM p)
                    {
                        // "x,y x,y ..."
                        var pts = string.Join(" ",
                            p.Points.Select(pt =>
                                $"{pt.X.ToString("0.###", CultureInfo.InvariantCulture)},{pt.Y.ToString("0.###", CultureInfo.InvariantCulture)}"));

                        list.Add(new
                        {
                            type = "path",
                            id = p.Id,
                            displayName = p.DisplayName,
                            points = pts,
                            thickness = p.Thickness,
                            zIndex = p.ZIndex
                        });
                        continue;
                    }

                    // Equipment / Device
                    if (vm is ConveyorVM c)
                    {
                        var (x, y, w, h) = ToSeedSpace(c);

                        list.Add(new
                        {
                            type = "conveyor",
                            id = c.Id,
                            displayName = c.DisplayName,
                            x = x,
                            y = y,
                            width = w,
                            height = h,
                            labelOrientation = c.LabelOrientation.ToString(),
                            direction = c.Direction.ToString(),
                            lane = c.Lane,
                            status = c.Status.ToString(),
                            speed = c.Speed,
                            zIndex = c.ZIndex,
                            hasItem = c.HasItem
                        });
                        continue;
                    }


                    if (vm is SensorVM s)
                    {
                        var (x, y, w, h) = ToSeedSpace(s);

                        list.Add(new
                        {
                            type = "sensor",
                            id = s.Id,
                            displayName = s.DisplayName,
                            x = x,
                            y = y,
                            width = w,
                            height = h,
                            labelOrientation = s.LabelOrientation.ToString(),
                            sensorType = s.SensorType.ToString(),
                            status = s.Status.ToString(),
                            isTriggered = s.IsTriggered,
                            zIndex = s.ZIndex
                        });
                        continue;
                    }


                    if (vm is CameraVM cam)
                    {
                        var (x, y, w, h) = ToSeedSpace(cam);

                        list.Add(new
                        {
                            type = "camera",
                            id = cam.Id,
                            displayName = cam.DisplayName,
                            x = x,
                            y = y,
                            width = w,
                            height = h,
                            labelOrientation = cam.LabelOrientation.ToString(),
                            status = cam.Status.ToString(),
                            isStreaming = cam.IsStreaming,
                            zIndex = cam.ZIndex
                        });
                        continue;
                    }


                    if (vm is BarcodeScannerVM b)
                    {
                        var (x, y, w, h) = ToSeedSpace(b);

                        list.Add(new
                        {
                            type = "barcode",
                            id = b.Id,
                            displayName = b.DisplayName,
                            x = x,
                            y = y,
                            width = w,
                            height = h,
                            labelOrientation = b.LabelOrientation.ToString(),
                            status = b.Status.ToString(),
                            lastCode = b.LastCode,
                            zIndex = b.ZIndex
                        });
                        continue;
                    }


                    // fallback
                    var (x0, y0, w0, h0) = ToSeedSpace(vm);

                    list.Add(new
                    {
                        type = "device",
                        id = vm.Id,
                        displayName = vm.DisplayName,
                        x = x0,
                        y = y0,
                        width = w0,
                        height = h0,
                        labelOrientation = vm.LabelOrientation.ToString(),
                        status = vm.Status.ToString(),
                        zIndex = vm.ZIndex,
                        hasItem = vm.HasItem
                    });

                }

                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(path, json);

                MessageBox.Show($"Saved: {path}", "Layout Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== DTO =====
        private sealed class MapEntitySeedDto
        {
            public string? Type { get; set; }
            public string? Id { get; set; }
            public string? DisplayName { get; set; }

            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public int ZIndex { get; set; }

            
            public string? LabelOrientation { get; set; }
// LayoutZone
            public string? Kind { get; set; }
            public int LabelFontSize { get; set; }

            // ConveyorBlock
            public bool ShowCenterLine { get; set; }

            // Path
            public string? Points { get; set; }
            public double Thickness { get; set; }

            // Conveyor
            public string? Direction { get; set; }
            public int Lane { get; set; }
            public string? Status { get; set; }
            public double Speed { get; set; }
            public bool HasItem { get; set; }

            // Sensor
            public string? SensorType { get; set; }
            public bool IsTriggered { get; set; }

            // Camera
            public bool IsStreaming { get; set; }

            // Barcode
            public string? LastCode { get; set; }
        }

        // fallback VM for unknown type
        private sealed class DeviceVMImpl : DeviceVM
        {
            public DeviceVMImpl(string id) : base(id) { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
