// Services/SimulationEngine.cs
using factory_automation_system_FAS_.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace factory_automation_system_FAS_.Services
{
    /// <summary>
    /// Phase C-3 (A안 기반) 최소 시뮬레이션 엔진
    /// - RC카는 3개 레인 중 랜덤 1개 선택
    /// - 왼쪽(파란 영역)에서 제품 Pick -> 오른쪽(빨간 영역)에서 Drop
    /// - Drop 지점에서는 60초 대기(=drop cycle)
    /// - Drop 후 빈 카트로 왼쪽으로 복귀
    /// - 제품 아이콘은 Cart.HasLoad 로 표현 (카트에 붙어서 이동)
    /// </summary>
    public sealed class SimulationEngine
    {
        // ===== Map 좌표(너가 crop 기준으로 잡은 좌표계) =====
        private const double LeftX = 1180;
        private const double RightX = 2200;

        private static readonly double[] LaneY = new double[]
        {
            430,   // 상단 레인 (QR1~QR4)
            720,   // 중단 레인 (QR2~QR5)
            1040   // 하단 레인 (QR3~QR6)
        };

        // 이동 속도(px/sec)
        private const double SpeedPxPerSec = 650;

        // 드롭 대기(초)
        private const double DropWaitSeconds = 60;

        private readonly Random _rng = new();

        // ===== 공개 상태(MainViewModel이 바인딩/참조하는 것들) =====
        public CartModel Cart { get; } = new CartModel
        {
            Position = new Point(LeftX, LaneY[1]),
            HasLoad = false,
            State = CartState.IdleAtLeft,
            SpeedPxPerSec = SpeedPxPerSec
        };

        // 네 StationModel은 (StationId, Point) 생성자 기반 + Stock 보유
        public StationModel RackA { get; } = new StationModel(StationId.RackA, new Point(0, 0));
        public StationModel RackB { get; } = new StationModel(StationId.RackB, new Point(0, 0));
        public StationModel Output { get; } = new StationModel(StationId.Output, new Point(0, 0));

        public ObservableCollection<LogRecord> Logs { get; } = new();

        // ===== 내부 상태 =====
        private int _laneIndex = 1;
        private double _dropWaitAcc = 0;

        public void Tick(double dt)
        {
            if (dt <= 0) return;

            switch (Cart.State)
            {
                case CartState.IdleAtLeft:
                    // 다음 사이클 시작: 랜덤 레인 선택 + 픽업(현재는 항상 가능 처리)
                    _laneIndex = _rng.Next(0, 3);

                    // 왼쪽 픽업 지점으로 스냅
                    Cart.Position = new Point(LeftX, LaneY[_laneIndex]);

                    // 픽업 => 제품 탑재
                    Cart.HasLoad = true;

                    Logs.Add(LogRecord.DeviceLog(DateTime.Now, "RC카", $"PICK @ Lane{_laneIndex + 1} (LEFT)"));

                    Cart.State = CartState.ToDrop;
                    _dropWaitAcc = 0;
                    break;

                case CartState.ToDrop:
                    // 오른쪽으로 이동
                    {
                        double nx = Cart.Position.X + Cart.SpeedPxPerSec * dt;

                        if (nx >= RightX)
                        {
                            nx = RightX;
                            Cart.Position = new Point(nx, LaneY[_laneIndex]);

                            Cart.State = CartState.DroppingWait;
                            _dropWaitAcc = 0;
                        }
                        else
                        {
                            Cart.Position = new Point(nx, LaneY[_laneIndex]);
                        }
                    }
                    break;

                case CartState.DroppingWait:
                    _dropWaitAcc += dt;

                    if (_dropWaitAcc >= DropWaitSeconds)
                    {
                        // 드롭 완료
                        Cart.HasLoad = false;
                        Output.Stock += 1;

                        Logs.Add(LogRecord.DeviceLog(DateTime.Now, "RC카", $"DROP @ Lane{_laneIndex + 1} (RIGHT)"));

                        Cart.State = CartState.ReturningEmpty;
                        _dropWaitAcc = 0;
                    }
                    break;

                case CartState.ReturningEmpty:
                    // 빈 카트로 왼쪽 복귀
                    {
                        double nx = Cart.Position.X - Cart.SpeedPxPerSec * dt;

                        if (nx <= LeftX)
                        {
                            nx = LeftX;
                            Cart.Position = new Point(nx, LaneY[_laneIndex]);

                            Cart.State = CartState.IdleAtLeft; // 다음 사이클
                        }
                        else
                        {
                            Cart.Position = new Point(nx, LaneY[_laneIndex]);
                        }
                    }
                    break;
            }
        }

        public void AddStock(StationId id, int amount)
        {
            if (amount <= 0) return;

            if (id == StationId.Output) Output.Stock += amount;
            else if (id == StationId.RackA) RackA.Stock += amount;
            else if (id == StationId.RackB) RackB.Stock += amount;
        }
    }
}
