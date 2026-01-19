// Services/SimulationEngine.cs
using factory_automation_system_FAS_.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace factory_automation_system_FAS_.Services
{
    /// <summary>
    /// ✅ 시뮬 로직 담당 (UI 모름 / MVVM-friendly)
    /// - Output에 랜덤 제품 생성
    /// - 카트가 1개 픽업 -> RackA/B 랜덤 드롭 -> 다시 Output
    /// - 경로는 RouteModel의 waypoint를 따라 이동
    /// </summary>
    public sealed class SimulationEngine
    {
        public StationModel Output { get; }
        public StationModel RackA { get; }
        public StationModel RackB { get; }
        public CartModel Cart { get; }
        public RouteModel Route { get; }

        private readonly Random _rng = new();

        // 제품 생성 파라미터 (MVP)
        // tick마다 확률로 1개 생성
        public double SpawnProbabilityPerTick { get; set; } = 0.08; // 0.05~0.12 사이로 튜닝 ㄱㄱ

        // 도착 판정(픽셀)
        public double ArriveEps { get; set; } = 6.0;

        public SimulationEngine()
        {
            Route = RouteModel.CreateDefault();

            // 스테이션 앵커는 "경로 끝점" 기준으로 잡음 (가장 심플)
            Output = new StationModel(StationId.Output, new Point(1010, 760));
            RackA = new StationModel(StationId.RackA, new Point(250, 155));
            RackB = new StationModel(StationId.RackB, new Point(650, 155));

            Cart = new CartModel
            {
                Position = new Point(1010, 760),
                SpeedPxPerSec = 260.0,
                HasLoad = false,
                State = CartState.ToOutput,
                Target = StationId.RackA
            };

            // 처음엔 Output 근처 대기라서 route는 비워도 되지만,
            // 로직 단순화를 위해 “Output로 복귀 루트”를 기본으로 둠.
            SetRoute(Route.BackFromRackA);
        }

        public void Reset()
        {
            Output.Stock = 0;
            RackA.Stock = 0;
            RackB.Stock = 0;

            Cart.Position = Output.Anchor;
            Cart.HasLoad = false;
            Cart.State = CartState.ToOutput;
            Cart.Target = StationId.RackA;

            SetRoute(Route.BackFromRackA);
        }
        public void AddStock(StationId id, int count = 1)
        {
            if (count <= 0) return;

            switch (id)
            {
                case StationId.Output:
                    Output.Stock += count;
                    break;

                case StationId.RackA:
                    RackA.Stock += count;
                    break;

                case StationId.RackB:
                    RackB.Stock += count;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown StationId");
            }
        }


        public void Tick(double dtSeconds)
        {
            // 1) Output 랜덤 생성 제거
         /*   if (_rng.NextDouble() < SpawnProbabilityPerTick)
                Output.Stock++;*/

            // 2) 카트 상태머신
            switch (Cart.State)
            {
                case CartState.ToOutput:
                               //  먼저 도착했는지 확인 (도착이면 이동하지 말고 바로 Loading)
                    if (IsArrived(Cart.Position, Output.Anchor))
                    {
                        Cart.State = CartState.Loading;
                        break;
                    }

                              // 아직 Output이 아니면 이동
                    StepMove(dtSeconds);
                    break;


                case CartState.Loading:
                    // Output에 재고가 있으면 1개 싣고 목적지를 랜덤 선택
                    if (!Cart.HasLoad && Output.Stock > 0)
                    {
                        Output.Stock--;
                        Cart.HasLoad = true;

                        Cart.Target = (_rng.Next(0, 2) == 0) ? StationId.RackA : StationId.RackB;
                        Cart.State = CartState.ToRack;

                        if (Cart.Target == StationId.RackA) SetRoute(Route.ToRackA);
                        else SetRoute(Route.ToRackB);
                    }
                    else
                    {
                        // 재고 없으면 그냥 계속 대기(또는 살짝 흔들기 같은 연출은 나중에)
                        // 여기서는 아무것도 안 함
                    }
                    break;

                case CartState.ToRack:
                    StepMove(dtSeconds);
                    var targetAnchor = (Cart.Target == StationId.RackA) ? RackA.Anchor : RackB.Anchor;
                    if (IsArrived(Cart.Position, targetAnchor))
                    {
                        Cart.State = CartState.Unloading;
                    }
                    break;

                case CartState.Unloading:
                    {
                        // ✅ 방금 도착했던 랙 기억
                        var deliveredTo = Cart.Target; // RackA or RackB 여야 함

                        if (Cart.HasLoad)
                        {
                            if (deliveredTo == StationId.RackA) RackA.Stock++;
                            else if (deliveredTo == StationId.RackB) RackB.Stock++;

                            Cart.HasLoad = false;
                        }

                        // 다시 Output로 복귀
                        var lastRack = Cart.Target;   // ⭐️ unloading 직전의 랙 기억

                        Cart.State = CartState.ToOutput;
                        Cart.Target = StationId.Output;

                        if (lastRack == StationId.RackA)
                            SetRoute(Route.BackFromRackA);
                        else
                            SetRoute(Route.BackFromRackB);


                        break;
                    }

            }
        }

        private void SetRoute(List<Point> route)
        {
            Cart.CurrentRoute = route.ToList();
            Cart.RouteIndex = 0;

            // route의 첫 점이 현재 위치와 같을 수 있으니(예: 출발점),
            // 다음으로 넘어가도록 살짝 보정
            while (Cart.RouteIndex < Cart.CurrentRoute.Count &&
                   Distance(Cart.Position, Cart.CurrentRoute[Cart.RouteIndex]) < ArriveEps)
            {
                Cart.RouteIndex++;
            }
        }

        private void StepMove(double dtSeconds)
        {
            if (Cart.RouteIndex >= Cart.CurrentRoute.Count) return;

            var target = Cart.CurrentRoute[Cart.RouteIndex];
            var pos = Cart.Position;

            var to = target - pos;
            double dist = to.Length;

            if (dist < ArriveEps)
            {
                Cart.Position = target;
                Cart.RouteIndex++;
                return;
            }

            double step = Cart.SpeedPxPerSec * dtSeconds;
            if (step >= dist)
            {
                Cart.Position = target;
                Cart.RouteIndex++;
                return;
            }

            to.Normalize();
            Cart.Position = pos + (to * step);
        }

        private bool IsArrived(Point a, Point b) => Distance(a, b) < ArriveEps;

        private static double Distance(Point a, Point b)
        {
            var d = a - b;
            return d.Length;
        }
    }
}
