using factory_automation_system_FAS_.Models;
using System;
using System.Collections.ObjectModel;

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
        // ===== Map 좌표계(= MapView에서 CroppedBitmap으로 잘라서 쓰는 영역) =====
        // Cropped 영역: (x=0, y=150, w=3100, h=1700) 기준으로 좌표 잡음
        // 레인 3개: 상/중/하 (너가 그린 QR1-4 / QR2-5 / QR3-6 느낌)
        private const double LeftX = 1180;
        private const double RightX = 2200;

        private static readonly double[] LaneY = new double[]
        {
            430,   // 상단 레인
            720,   // 중단 레인
            1040   // 하단 레인
        };

        // 이동 속도(px/sec)
        private const double Speed = 650;

        // 드롭 대기(초) - 너가 “drop은 1분대로”라 해서 60초
        private const double DropWaitSeconds = 60;

        private readonly Random _rng = new();

        // ===== 공개 상태 =====
        public CartModel Cart { get; } = new()
        {
            Position = new PointD(LeftX, LaneY[1]),
            State = CartState.IdleAtLeft,
            HasLoad = true // 초기엔 들고 출발해도 되고, 아니면 false로 시작해도 됨
        };

        public StationModel RackA { get; } = new() { Id = StationId.RackA, Name = "RackA", Stock = 0 };
        public StationModel RackB { get; } = new() { Id = StationId.RackB, Name = "RackB", Stock = 0 };
        public StationModel Output { get; } = new() { Id = StationId.Output, Name = "Output", Stock = 0 };

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
                    // 다음 사이클 시작: 레인 랜덤 선택 + 제품 픽업(현재는 항상 가능 처리)
                    _laneIndex = _rng.Next(0, 3);
                    Cart.Position = new PointD(LeftX, LaneY[_laneIndex]);
                    Cart.HasLoad = true;

                    AddLog("PICK", $"Lane{_laneIndex + 1} (LEFT)", Cart.Position.X, Cart.Position.Y);

                    Cart.State = CartState.ToDrop;
                    _dropWaitAcc = 0;
                    break;

                case CartState.ToDrop:
                    // 오른쪽으로 이동
                    {
                        double nx = Cart.Position.X + Speed * dt;
                        if (nx >= RightX)
                        {
                            nx = RightX;
                            Cart.Position = new PointD(nx, LaneY[_laneIndex]);

                            // 도착 -> 드롭 대기
                            Cart.State = CartState.DroppingWait;
                            _dropWaitAcc = 0;
                        }
                        else
                        {
                            Cart.Position = new PointD(nx, LaneY[_laneIndex]);
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

                        AddLog("DROP", $"Lane{_laneIndex + 1} (RIGHT)", Cart.Position.X, Cart.Position.Y);

                        Cart.State = CartState.ReturningEmpty;
                        _dropWaitAcc = 0;
                    }
                    break;

                case CartState.ReturningEmpty:
                    // 왼쪽으로 복귀
                    {
                        double nx = Cart.Position.X - Speed * dt;
                        if (nx <= LeftX)
                        {
                            nx = LeftX;
                            Cart.Position = new PointD(nx, LaneY[_laneIndex]);

                            Cart.State = CartState.IdleAtLeft; // 다음 사이클
                        }
                        else
                        {
                            Cart.Position = new PointD(nx, LaneY[_laneIndex]);
                        }
                    }
                    break;
            }
        }

        public void AddStock(StationId id, int amount)
        {
            if (amount <= 0) return;

            if (id == StationId.Output) Output.Stock += amount;
            if (id == StationId.RackA) RackA.Stock += amount;
            if (id == StationId.RackB) RackB.Stock += amount;
        }

        private void AddLog(string evt, string station, double x, double y)
        {
            Logs.Add(new LogRecord
            {
                Timestamp = DateTime.Now,
                Event = evt,
                Station = station,
                Detail = $"x={x:0}, y={y:0}"
            });
        }
    }
}
