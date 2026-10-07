using System;
using System.Numerics;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Simple server-side bot used for testing (wanders, spots enemies with line of sight,
    /// reacts after a delay, aims with error, shoots in bursts). Full tactical AI comes later.
    /// </summary>
    public sealed class ServerBot
    {
        private readonly ServerGame _game;
        private readonly ServerGame.Player _me;
        private readonly Random _rng;
        private Vector3 _goal;
        private int _goalTicks;
        private ServerGame.Player _target;
        private float _react;
        private float _errYaw, _errPitch;
        private int _burst;
        private float _pauseUntil;
        public float ReactionTime = 0.35f;
        public float TurnSpeed = 360f;

        public ServerBot(ServerGame game, ServerGame.Player me, int seed)
        {
            _game = game; _me = me; _rng = new Random(seed);
        }

        public PlayerInput Think(int tick)
        {
            var s = _me.State;
            var cmd = new PlayerInput { Tick = tick, InterpTick = tick };
            float dt = _game.Dt;
            float time = tick * dt;
            if (!s.Alive) { cmd.SetAngles(s.Yaw, s.Pitch); _target = null; return cmd; }

            // perception
            var eye = s.EyePosition;
            ServerGame.Player best = null; float bestD = 60f;
            foreach (var p in _game.Players)
            {
                if (p == _me || !p.State.Alive) continue;
                if (p.State.Team != Team.None && p.State.Team == s.Team) continue;
                var head = p.State.EyePosition;
                float d = Vector3.Distance(eye, head);
                if (d > bestD) continue;
                var dir = (head - eye) / d;
                float dot = Vector3.Dot(dir, VMath.Forward(s.Yaw, s.Pitch));
                if (dot < 0.2f && d > 4f && p != _target) continue;
                if (!_game.World.LineOfSight(eye, head) && !_game.World.LineOfSight(eye, p.State.Position + new Vector3(0, 1.1f, 0))) continue;
                best = p; bestD = d;
            }
            if (best != _target)
            {
                _target = best;
                _react = ReactionTime * (0.8f + (float)_rng.NextDouble() * 0.5f);
                _errYaw = ((float)_rng.NextDouble() - 0.5f) * 6f;
                _errPitch = ((float)_rng.NextDouble() - 0.5f) * 4f;
            }

            float yaw = s.Yaw, pitch = s.Pitch;
            if (_target != null)
            {
                _react -= dt;
                var aim = _target.State.Position + new Vector3(0, _target.State.HullHeight * 0.68f, 0);
                VMath.AnglesFromDir(aim - eye, out float wy, out float wp);
                WeaponLogic.AimPunch(s, out float pp, out float py);
                _errYaw *= MathF.Exp(-dt * 3f); _errPitch *= MathF.Exp(-dt * 3f);
                wy += _errYaw - py * WeaponLogic.RecoilScale * 0.7f;
                wp += _errPitch - pp * WeaponLogic.RecoilScale * 0.7f;
                yaw = s.Yaw + VMath.Clamp(VMath.AngleDelta(s.Yaw, wy), -TurnSpeed * dt, TurnSpeed * dt);
                pitch = s.Pitch + VMath.Clamp(wp - s.Pitch, -TurnSpeed * dt, TurnSpeed * dt);
                float err = MathF.Abs(VMath.AngleDelta(yaw, wy)) + MathF.Abs(wp - pitch);
                var slot = s.ActiveSlot;
                if (slot.Clip == 0 && slot.Reserve > 0) cmd.Buttons |= Buttons.Reload;
                else if (_react <= 0 && err < 4f && time >= _pauseUntil)
                {
                    var def = s.ActiveDef;
                    if (def.Automatic) { cmd.Buttons |= Buttons.Attack; _burst++; if (_burst > 6) { _burst = 0; _pauseUntil = time + 0.3f; } }
                    else if (((tick / 8) & 1) == 0) cmd.Buttons |= Buttons.Attack;
                }
            }
            else
            {
                // wander between spawn points
                if (_goalTicks-- <= 0 || Vector3.Distance(new Vector3(s.Position.X, 0, s.Position.Z), new Vector3(_goal.X, 0, _goal.Z)) < 1f)
                {
                    var sp = _game.Map.Spawns;
                    _goal = sp.Count > 0 ? sp[_rng.Next(sp.Count)].Position : s.Position;
                    _goalTicks = _game.TickRate * 8;
                }
                var to = _goal - s.Position; to.Y = 0;
                if (to.LengthSquared() > 0.01f)
                {
                    VMath.AnglesFromDir(to, out float wy, out _);
                    yaw = s.Yaw + VMath.Clamp(VMath.AngleDelta(s.Yaw, wy), -180f * dt, 180f * dt);
                    pitch *= 0.9f;
                    cmd.Buttons |= Buttons.Forward;
                    if (VMath.HorizontalLength(s.Velocity) < 0.5f && _rng.NextDouble() < 0.05) cmd.Buttons |= Buttons.Jump;
                }
            }
            cmd.SetAngles(yaw, pitch);
            return cmd;
        }
    }
}
