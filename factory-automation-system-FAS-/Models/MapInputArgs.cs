// Models/MapInputArgs.cs
using System.Windows;

namespace factory_automation_system_FAS_.Models
{
    public sealed class ZoomArgs
    {
        public int Delta { get; init; }
        public Point MousePosOnCanvas { get; init; }
    }

    public sealed class PanArgs
    {
        public Point MousePosOnHost { get; init; }
    }
}
