// ViewModels/MapEntities/MapEntityVM.cs
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace factory_automation_system_FAS_.ViewModels.MapEntities
{
    // ===== 공통 Enum =====

    public enum EntityStatus
    {
        Offline = 0,
        Idle = 1,
        Normal = 2,
        Running = 3,
        Warning = 4,
        Fault = 5
    }

    public enum Direction
    {
        None = 0,
        Left,
        Right,
        Up,
        Down
    }

    public enum SensorType
    {
        Unknown = 0,
        ItemDetect,
        Position,
        Temperature,
        Speed,
        Pressure
    }

    public enum WorkState
    {
        Unknown = 0,
        Waiting,
        Processing,
        Stopped,
        Fault
    }

    // ===== Base VM =====

    public abstract class MapEntityVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            return true;
        }

        protected void Raise([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        protected MapEntityVM(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            _displayName = id;
            _lastUpdated = DateTimeOffset.Now;
        }

        public string Id { get; }

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        // ===== Geometry (크롭 기준 좌표계) =====
        private double _x;
        public double X { get => _x; set => SetProperty(ref _x, value); }

        private double _y;
        public double Y { get => _y; set => SetProperty(ref _y, value); }

        private double _width = 20;
        public double Width { get => _width; set => SetProperty(ref _width, value); }

        private double _height = 20;
        public double Height { get => _height; set => SetProperty(ref _height, value); }

        private double _rotation;
        public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }

        private int _zIndex;
        public int ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }

        private bool _isInteractive = true;
        public bool IsInteractive
        {
            get => _isInteractive;
            set => SetProperty(ref _isInteractive, value);
        }


        // ===== Runtime State =====
        private EntityStatus _status = EntityStatus.Normal;
        public EntityStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                    Touch();
            }
        }

        private bool _hasItem;
        public bool HasItem
        {
            get => _hasItem;
            set
            {
                if (SetProperty(ref _hasItem, value))
                    Touch();
            }
        }

        private DateTimeOffset _lastUpdated;
        public DateTimeOffset LastUpdated
        {
            get => _lastUpdated;
            private set => SetProperty(ref _lastUpdated, value);
        }

        public void Touch() => LastUpdated = DateTimeOffset.Now;

        // 디버그/툴팁용 기본 문자열
        public virtual string TooltipText
            => $"{Id}\nStatus: {Status}\nHasItem: {HasItem}\nUpdated: {LastUpdated:HH:mm:ss}";
    }

    // ===== Category VM (관리 편하게 그룹화) =====

    public abstract class EquipmentVM : MapEntityVM
    {
        protected EquipmentVM(string id) : base(id) { }
    }

    public abstract class DeviceVM : MapEntityVM
    {
        protected DeviceVM(string id) : base(id) { }
    }

    public abstract class OverlayVM : MapEntityVM
    {
        protected OverlayVM(string id) : base(id) { }
    }

    // ===== Derived VMs =====

    public sealed class ConveyorVM : EquipmentVM
    {
        public ConveyorVM(string id) : base(id) { }

        private int _lane;
        public int Lane { get => _lane; set => SetProperty(ref _lane, value); }

        private Direction _direction = Direction.Right;
        public Direction Direction { get => _direction; set => SetProperty(ref _direction, value); }

        private double _speed;
        public double Speed
        {
            get => _speed;
            set
            {
                if (SetProperty(ref _speed, value))
                    Touch();
            }
        }

        private bool _isBlocked;
        public bool IsBlocked
        {
            get => _isBlocked;
            set
            {
                if (SetProperty(ref _isBlocked, value))
                    Touch();
            }
        }

        public override string TooltipText
            => $"{Id}\nConveyor\nStatus: {Status}\nLane: {Lane} Dir: {Direction}\nSpeed: {Speed:0.##}\nHasItem: {HasItem} Blocked: {IsBlocked}";
    }

    public sealed class StationVM : EquipmentVM
    {
        public StationVM(string id) : base(id) { }

        private WorkState _workState = WorkState.Unknown;
        public WorkState WorkState
        {
            get => _workState;
            set
            {
                if (SetProperty(ref _workState, value))
                    Touch();
            }
        }

        private string? _currentProductId;
        public string? CurrentProductId
        {
            get => _currentProductId;
            set
            {
                if (SetProperty(ref _currentProductId, value))
                    Touch();
            }
        }

        public override string TooltipText
            => $"{Id}\nStation\nStatus: {Status}\nWork: {WorkState}\nProduct: {CurrentProductId ?? "-"}\nHasItem: {HasItem}";
    }

    public sealed class RackVM : EquipmentVM
    {
        public RackVM(string id) : base(id) { }

        private int _count;
        public int Count
        {
            get => _count;
            set
            {
                if (SetProperty(ref _count, value))
                    Touch();
            }
        }

        private int _capacity = 10;
        public int Capacity
        {
            get => _capacity;
            set => SetProperty(ref _capacity, value);
        }

        public override string TooltipText
            => $"{Id}\nRack\nStatus: {Status}\nCount: {Count}/{Capacity}";
    }

    public sealed class SensorVM : DeviceVM
    {
        public SensorVM(string id) : base(id) { }

        private SensorType _sensorType = SensorType.Unknown;
        public SensorType SensorType { get => _sensorType; set => SetProperty(ref _sensorType, value); }

        private bool _isTriggered;
        public bool IsTriggered
        {
            get => _isTriggered;
            set
            {
                if (SetProperty(ref _isTriggered, value))
                    Touch();
            }
        }

        private double? _numericValue;
        public double? NumericValue
        {
            get => _numericValue;
            set
            {
                if (SetProperty(ref _numericValue, value))
                    Touch();
            }
        }

        private string? _unit;
        public string? Unit { get => _unit; set => SetProperty(ref _unit, value); }

        public override string TooltipText
            => $"{Id}\nSensor ({SensorType})\nStatus: {Status}\nTriggered: {IsTriggered}\nValue: {(NumericValue.HasValue ? NumericValue.Value.ToString("0.##") : "-")}{Unit ?? ""}";
    }

    public sealed class CameraVM : DeviceVM
    {
        public CameraVM(string id) : base(id) { }

        private bool _isStreaming;
        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (SetProperty(ref _isStreaming, value))
                    Touch();
            }
        }

        private string? _streamUrl;
        public string? StreamUrl { get => _streamUrl; set => SetProperty(ref _streamUrl, value); }

        public override string TooltipText
            => $"{Id}\nCamera\nStatus: {Status}\nStreaming: {IsStreaming}\nUrl: {StreamUrl ?? "-"}";
    }

    public sealed class BarcodeScannerVM : DeviceVM
    {
        public BarcodeScannerVM(string id) : base(id) { }

        private string? _lastCode;
        public string? LastCode
        {
            get => _lastCode;
            set
            {
                if (SetProperty(ref _lastCode, value))
                    Touch();
            }
        }

        private DateTimeOffset? _lastReadTime;
        public DateTimeOffset? LastReadTime
        {
            get => _lastReadTime;
            set => SetProperty(ref _lastReadTime, value);
        }

        public override string TooltipText
            => $"{Id}\nBarcode\nStatus: {Status}\nCode: {LastCode ?? "-"}\nRead: {(LastReadTime.HasValue ? LastReadTime.Value.ToString("HH:mm:ss") : "-")}";
    }

    public sealed class ZoneVM : OverlayVM
    {
        public ZoneVM(string id) : base(id) { }

        private double _opacity = 0.15;
        public double Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }

        public override string TooltipText
            => $"{Id}\nZone\nStatus: {Status}";
    }
}
