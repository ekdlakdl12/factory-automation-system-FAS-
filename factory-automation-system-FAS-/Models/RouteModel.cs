// Models/RouteModel.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace factory_automation_system_FAS_.Models
{
    public sealed class RouteModel
    {
        public List<Point> ToRackA { get; } = new();
        public List<Point> ToRackB { get; } = new();
        public List<Point> BackFromRackA { get; } = new();
        public List<Point> BackFromRackB { get; } = new();

        public static RouteModel CreateDefault()
        {

            var r = new RouteModel();

            r.ToRackA.AddRange(new[]
            {
                new Point(1010,760),
                new Point(1010,570),
                new Point(470,570),
                new Point(470,440),
                new Point(250,440),
                new Point(250,155),
            });

            r.ToRackB.AddRange(new[]
            {
                new Point(1010,760),
                new Point(1010,570),
                new Point(790,570),
                new Point(790,440),
                new Point(650,440),
                new Point(650,155),
            });

            // 돌아오는 길은 역순
            r.BackFromRackA.AddRange(new[]
            {
                new Point(250,155),
                new Point(250,440),
                new Point(470,440),
                new Point(470,570),
                new Point(1010,570),
                new Point(1010,760),
            });

            r.BackFromRackB.AddRange(new[]
            {
                new Point(650,155),
                new Point(650,440),
                new Point(790,440),
                new Point(790,570),
                new Point(1010,570),
                new Point(1010,760),
            });

            return r;
        }
    }
}

