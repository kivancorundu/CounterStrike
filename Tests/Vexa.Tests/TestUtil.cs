using System;
using System.IO;
using System.Numerics;
using Vexa.Core;

namespace Vexa.Tests
{
    internal static class TestUtil
    {
        public const float HU = VMath.HU;

        public static MapData LoadMap(string name = "training")
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Maps", name + ".vxmap");
            return MapData.Parse(File.ReadAllText(path));
        }

        /// <summary>Flat floor (top at y=0) plus optional extra boxes.</summary>
        public static CollisionWorld Flat(params (Vector3 min, Vector3 max)[] boxes)
        {
            var w = new CollisionWorld();
            w.AddBox(new Vector3(-100, -1, -100), new Vector3(100, 0, 100), SurfaceMaterial.Concrete);
            foreach (var b in boxes) w.AddBox(b.min, b.max, SurfaceMaterial.Wood);
            w.Build();
            return w;
        }

        public static PlayerState Player(Vector3 pos, float yaw = 0, WeaponSlotKind active = WeaponSlotKind.Melee)
        {
            var s = PlayerState.Spawn(Team.T, pos, yaw);
            s.Active = active;
            return s;
        }

        public static PlayerInput Cmd(int tick, Buttons b, float yaw = 0, float pitch = 0, WeaponSelect sel = WeaponSelect.None)
        {
            var c = new PlayerInput { Tick = tick, Buttons = b, Select = sel };
            c.SetAngles(yaw, pitch);
            return c;
        }

        public sealed class ShotRecorder : IShotSink
        {
            public int Count;
            public ShotInfo Last;
            public void OnShot(in ShotInfo shot) { Count++; Last = shot; }
        }

        /// <summary>Runs ticks with a fixed button set; returns the final state.</summary>
        public static PlayerState Run(ref PlayerState s, CollisionWorld w, int tickRate, float seconds, Buttons b, ref int tick, float yaw = 0, IShotSink sink = null, Action<PlayerState> each = null)
        {
            var ctx = new SimContext { World = w, Dt = 1f / tickRate, PlayerId = 1, Shots = sink };
            int n = (int)MathF.Round(seconds * tickRate);
            for (int i = 0; i < n; i++)
            {
                tick++;
                PlayerSimulation.Step(ref s, Cmd(tick, b, yaw), ctx);
                each?.Invoke(s);
            }
            return s;
        }
    }
}
