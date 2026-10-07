using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vexa.Core.AI;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Server-side bot: navigation on the auto-generated grid, perception (line of sight + smoke + flash),
    /// reaction time, aim error, recoil control, tap/burst/spray, counter-strafing, team objectives
    /// (T: take a site and plant, CT: hold sites, retake and defuse) and economy-aware buying.
    /// Difficulty comes from MatchConfig.BotDifficulty (0 easy .. 1 expert).
    /// </summary>
    public sealed class ServerBot
    {
        private readonly ServerGame _g;
        private readonly ServerGame.Player _me;
        private readonly Random _rng;

        // navigation
        private List<Vector3> _path;
        private int _pathIdx;
        private Vector3 _goal;
        private string _goalKey = "";
        private int _repathTick, _stuckTick;
        private Vector3 _stuckPos;

        // combat
        private ServerGame.Player _target;
        private float _react, _errYaw, _errPitch;
        private int _burst;
        private float _pauseUntil;
        private bool _aimHead;
        private float _lastSeen = -99;
        private Vector3 _lastSeenPos;
        private Vector3? _heard; private float _heardTime = -99;
        private float _blindUntil;
        private int _strafeDir = 1; private float _strafeUntil;
        private bool _tapToggle;

        // plan
        internal string Site;
        internal Vector3 HoldSpot;
        private bool _utilityDone;
        private int _throwStage; private float _throwTime;
        private int _nextPerceive;

        float Skill => _g.Config.BotDifficulty;
        float ReactionTime => VMath.Lerp(0.65f, 0.2f, Skill);
        float AimError => VMath.Lerp(3.5f, 0.7f, Skill);
        float TurnSpeed => VMath.Lerp(220f, 650f, Skill);
        float SprayControl => VMath.Lerp(0.25f, 0.92f, Skill);
        float HeadshotChance => VMath.Lerp(0.1f, 0.55f, Skill);

        public ServerBot(ServerGame g, ServerGame.Player me, int seed) { _g = g; _me = me; _rng = new Random(seed); }

        NavGrid Nav => _g.Nav;
        float Now => _g.Tick * _g.Dt;

        public void OnSpawn() { _path = null; _goalKey = ""; _target = null; _utilityDone = false; _throwStage = 0; _heard = null; }
        public void OnDamaged(ServerGame.Player attacker)
        {
            if (attacker != null && attacker != _me && IsEnemy(attacker)) { _heard = attacker.State.Position; _heardTime = Now; }
        }
        public void OnFlashed(float dur) => _blindUntil = Now + dur * 0.85f;

        bool IsEnemy(ServerGame.Player p) => p.State.Team == Team.None || p.State.Team != _me.State.Team;

        // ---------------- team-level hooks ----------------
        public static void PlanRound(ServerGame g)
        {
            var sites = g.Map.Sites;
            if (sites.Count == 0) return;
            var tSite = sites[g.Rng.Next(sites.Count)];
            int ci = 0;
            foreach (var p in g.Players)
            {
                if (p.Bot == null) continue;
                if (p.State.Team == Team.T) { p.Bot.Site = tSite.Name; p.Bot.HoldSpot = g.Nav.RandomNodeIn(tSite, g.Rng); }
                else if (p.State.Team == Team.CT)
                {
                    var s = sites[ci++ % sites.Count];
                    p.Bot.Site = s.Name; p.Bot.HoldSpot = g.Nav.RandomNodeIn(s, g.Rng);
                }
            }
        }

        public static void OnBombPlanted(ServerGame g)
        {
            foreach (var p in g.Players) if (p.Bot != null) p.Bot._goalKey = "";
        }

        public static void OnAnyDeath(ServerGame g, ServerGame.Player victim, ServerGame.Player attacker)
        {
            foreach (var p in g.Players) if (p.Bot != null && p.Bot._target == victim) p.Bot._target = null;
        }

        public static void HearNoise(ServerGame g, ServerGame.Player src, Vector3 pos, float radius)
        {
            foreach (var p in g.Players)
            {
                if (p.Bot == null || p == src || !p.State.Alive) continue;
                if (src != null && !p.Bot.IsEnemy(src)) continue;
                if (Vector3.Distance(p.State.Position, pos) > radius) continue;
                p.Bot._heard = pos; p.Bot._heardTime = p.Bot.Now;
            }
        }

        // ---------------- economy ----------------
        public static void Buy(ServerGame g, ServerGame.Player p)
        {
            bool T = p.State.Team == Team.T;
            bool Try(ItemId i) => g.BotTryBuy(p, i);
            ItemId W(WeaponId w) => Items.FromWeapon(w);
            if (g.Config.Mode == GameMode.Deathmatch || g.Config.Mode == GameMode.Practice)
            {
                var opts = new[] { WeaponId.Ak47, WeaponId.M4a4, WeaponId.M4a1s, WeaponId.Awp, WeaponId.Galil, WeaponId.Famas, WeaponId.Mp9, WeaponId.Deagle };
                Try(W(opts[g.Rng.Next(opts.Length)]));
                return;
            }
            bool pistolRound = g.RoundsPlayed == 0 || g.RoundsPlayed == g.Config.HalfRounds;
            if (pistolRound)
            {
                int r = g.Rng.Next(3);
                if (r == 0) Try(ItemId.Vest);
                else if (r == 1) { Try(W(WeaponId.P250)); Try(ItemId.Flash); }
                else { Try(ItemId.Smoke); Try(ItemId.Flash); }
                if (!T && g.Rng.Next(3) == 0) Try(ItemId.DefuseKit);
                return;
            }
            var team = g.Players.Where(x => x.State.Team == p.State.Team).ToList();
            double avg = team.Average(x => x.Money);
            string mode = avg >= 3900 ? "full" : (avg >= 2600 && g.LossCounter(p.State.Team) >= 2) ? "force" : "eco";
            bool hasPrimary = !p.State.Primary.IsEmpty;
            if (mode == "eco")
            {
                if (p.Money > 2500 && g.Rng.Next(2) == 0) Try(W(g.Rng.Next(2) == 0 ? WeaponId.P250 : WeaponId.Deagle));
                return;
            }
            if (mode == "force" && !hasPrimary)
            {
                foreach (var w in T ? new[] { WeaponId.Galil, WeaponId.Mac10, WeaponId.Ump45 } : new[] { WeaponId.Famas, WeaponId.Mp9, WeaponId.Ump45 })
                    if (p.Money >= Weapons.Get(w).Price + 650 && Try(W(w))) break;
                Try(p.Money >= 1000 ? ItemId.VestHelmet : ItemId.Vest);
                Try(ItemId.Flash);
                return;
            }
            if (!hasPrimary)
            {
                bool awp = p.Money >= 6000 && g.Rng.Next(4) == 0;
                if (awp) Try(W(WeaponId.Awp));
                else if (T) { if (!Try(W(WeaponId.Ak47))) Try(W(WeaponId.Galil)); }
                else { if (!Try(W(g.Rng.Next(2) == 0 ? WeaponId.M4a4 : WeaponId.M4a1s))) Try(W(WeaponId.Famas)); }
            }
            if (p.State.Armor < 70 || !p.State.Helmet) Try(p.Money >= 1000 ? ItemId.VestHelmet : ItemId.Vest);
            if (!T) Try(ItemId.DefuseKit);
            foreach (var n in new[] { ItemId.Smoke, ItemId.Flash, T ? ItemId.Molotov : ItemId.Incendiary, ItemId.HE })
                if (p.Money >= 600) Try(n);
        }

        // ---------------- per tick ----------------
        public PlayerInput Think(int tick)
        {
            var s = _me.State;
            var cmd = new PlayerInput { Tick = tick, InterpTick = tick };
            float yaw = s.Yaw, pitch = s.Pitch;
            if (!s.Alive || _g.Phase == GamePhase.MatchOver) { cmd.SetAngles(yaw, pitch); return cmd; }
            if (_g.Phase == GamePhase.Freeze || _g.Phase == GamePhase.Warmup) { cmd.SetAngles(yaw, pitch); return cmd; }

            if (tick >= _nextPerceive) { _nextPerceive = tick + Math.Max(1, _g.TickRate / 20); Perceive(); }

            bool blind = Now < _blindUntil;
            if (blind)
            {
                cmd.Buttons |= Buttons.Back;
                yaw += 120f * _g.Dt;
                if (Now - _lastSeen < 1.5f) cmd.Buttons |= Buttons.Attack;
                cmd.SetAngles(yaw, pitch);
                return cmd;
            }

            if (_target != null && _target.State.Alive)
                Combat(ref cmd, ref yaw, ref pitch);
            else
            {
                _target = null;
                Objective(ref cmd, ref yaw, ref pitch);
            }
            if (cmd.Select == WeaponSelect.None && !s.Primary.IsEmpty && s.Active != WeaponSlotKind.Primary && s.Active != WeaponSlotKind.Bomb && s.Active != WeaponSlotKind.Grenade && _throwStage == 0)
                cmd.Select = WeaponSelect.Primary;
            if (cmd.Select == WeaponSelect.None && s.Primary.IsEmpty && s.Active == WeaponSlotKind.Melee && !s.Secondary.IsEmpty) cmd.Select = WeaponSelect.Secondary;
            var slot = s.ActiveSlot;
            if (_target == null && s.ActiveDef.IsGun && slot.Clip < s.ActiveDef.ClipSize * 0.35f && slot.Reserve > 0) cmd.Buttons |= Buttons.Reload;
            cmd.SetAngles(yaw, VMath.Clamp(pitch, -88f, 88f));
            return cmd;
        }

        private void Perceive()
        {
            var s = _me.State;
            var eye = s.EyePosition;
            var fwd = VMath.Forward(s.Yaw, s.Pitch);
            ServerGame.Player best = null; float bestScore = float.MaxValue;
            foreach (var p in _g.Players)
            {
                if (p == _me || !p.State.Alive || !IsEnemy(p)) continue;
                var head = p.State.EyePosition;
                var chest = p.State.Position + new Vector3(0, p.State.HullHeight * 0.65f, 0);
                var to = chest - eye; float d = to.Length();
                if (d > 80f) continue;
                float dot = Vector3.Dot(to / d, fwd);
                if (dot < 0.45f && d > 3f && p != _target) continue;
                if (!_g.CanSee(eye, head) && !_g.CanSee(eye, chest)) continue;
                float score = d * (p == _target ? 0.6f : 1f);
                if (score < bestScore) { bestScore = score; best = p; }
            }
            if (best != null)
            {
                if (best != _target)
                {
                    bool surprised = Now - _lastSeen > 3f;
                    _target = best;
                    _react = ReactionTime * (0.8f + (float)_rng.NextDouble() * 0.5f) * (surprised ? 1f : 0.6f);
                    _errYaw = ((float)_rng.NextDouble() - 0.5f) * AimError * 3f;
                    _errPitch = ((float)_rng.NextDouble() - 0.5f) * AimError * 2f;
                    _aimHead = _rng.NextDouble() < HeadshotChance;
                    _burst = 0;
                }
                _lastSeen = Now; _lastSeenPos = best.State.Position;
            }
            else if (_target != null && Now - _lastSeen > 1.2f) _target = null;
            // footsteps of running enemies
            if (_target == null)
                foreach (var p in _g.Players)
                {
                    if (p == _me || !p.State.Alive || !IsEnemy(p)) continue;
                    if (VMath.HorizontalLength(p.State.Velocity) < 140f * VMath.HU || !p.State.OnGround) continue;
                    if (Vector3.Distance(p.State.Position, s.Position) < 25f) { _heard = p.State.Position; _heardTime = Now; }
                }
        }

        private void Combat(ref PlayerInput cmd, ref float yaw, ref float pitch)
        {
            var s = _me.State;
            var t = _target.State;
            var eye = s.EyePosition;
            bool visible = Now - _lastSeen < 0.15f;
            var aimPoint = _aimHead ? t.EyePosition + new Vector3(0, 0.06f, 0) : t.Position + new Vector3(0, t.HullHeight * 0.66f, 0);
            if (!visible) aimPoint = _lastSeenPos + new Vector3(0, 1.5f, 0);
            VMath.AnglesFromDir(aimPoint - eye, out float wy, out float wp);
            float decay = MathF.Exp(-_g.Dt * (2.2f + SprayControl * 2f));
            _errYaw *= decay; _errPitch *= decay;
            WeaponLogic.AimPunch(s, out float pp, out float py);
            wy += _errYaw - py * WeaponLogic.RecoilScale * SprayControl;
            wp += _errPitch - pp * WeaponLogic.RecoilScale * SprayControl;
            TurnTowards(ref yaw, ref pitch, wy, wp, 1f);
            if (!visible) { cmd.Buttons |= Buttons.Walk; return; }

            _react -= _g.Dt * Math.Max(1, _g.TickRate / _g.TickRate);
            var def = s.ActiveDef;
            var slot = s.ActiveSlot;
            if (!def.IsGun) { cmd.Select = !s.Primary.IsEmpty ? WeaponSelect.Primary : WeaponSelect.Secondary; return; }
            if (slot.Clip == 0) { if (slot.Reserve > 0) cmd.Buttons |= Buttons.Reload; else if (!s.Secondary.IsEmpty && s.Active == WeaponSlotKind.Primary) cmd.Select = WeaponSelect.Secondary; return; }
            float dist = Vector3.Distance(eye, aimPoint);
            bool sniper = def.Category == WeaponCategory.Sniper;
            if (def.ZoomFov != null && s.Zoom == 0 && dist > 9f && _rng.Next(4) == 0) cmd.Buttons |= Buttons.Attack2;

            // counter-strafe: stop to shoot accurately at range; strafe between bursts when skilled
            float speed = VMath.HorizontalLength(s.Velocity);
            bool stopToShoot = dist > 6f && Skill > 0.3f;
            if (!stopToShoot || (Now > _pauseUntil && _burst == 0 && Skill > 0.6f && !sniper))
            {
                if (Now > _strafeUntil) { _strafeDir = -_strafeDir; _strafeUntil = Now + 0.3f + (float)_rng.NextDouble() * 0.4f; }
                cmd.Buttons |= _strafeDir > 0 ? Buttons.Right : Buttons.Left;
            }
            if (_react > 0) return;
            float err = MathF.Abs(VMath.AngleDelta(yaw, wy)) + MathF.Abs(wp - pitch);
            float tolerance = MathF.Atan2(0.28f, dist) * VMath.Rad2Deg * 1.4f + 0.3f;
            if (err > tolerance * 1.8f) { _burst = 0; return; }
            if (stopToShoot && speed > def.MoveSpeed * 0.34f)
            {
                cmd.Buttons &= ~(Buttons.Left | Buttons.Right | Buttons.Forward | Buttons.Back);
                if (speed > def.MoveSpeed * 0.45f) return;
            }
            if (Now < _pauseUntil) return;
            // don't shoot through teammates
            foreach (var p in _g.Players)
            {
                if (p == _me || !p.State.Alive || IsEnemy(p)) continue;
                if (Hitboxes.Raycast(HitPose.From(p.State), eye, VMath.Forward(yaw, pitch), dist, out _, out _)) return;
            }
            bool auto = def.Automatic && !slot.BurstMode;
            if (!auto)
            {
                float interval = sniper ? 0f : VMath.Clamp(dist / 120f, 0.12f, 0.45f);
                if (Now - s.LastShotTime > interval + def.CycleTime) { if (!_tapToggle) cmd.Buttons |= Buttons.Attack; _tapToggle = !_tapToggle; }
                return;
            }
            int maxBurst = dist > 40f ? 1 : dist > 22f ? 3 : dist > 12f ? 6 : 30;
            if (_burst >= maxBurst) { _burst = 0; _pauseUntil = Now + (dist > 40f ? 0.35f : 0.28f) * (0.8f + (float)_rng.NextDouble() * 0.5f); return; }
            cmd.Buttons |= Buttons.Attack;
            if (Now - s.LastShotTime < _g.Dt * 1.5f) _burst++;
        }

        private void Objective(ref PlayerInput cmd, ref float yaw, ref float pitch)
        {
            var s = _me.State;
            var bomb = _g.Bomb;
            bool rounds = _g.Config.HasRounds;
            string key; Vector3 goal; Vector3? look = null;

            if (!rounds)
            {
                if (_heard.HasValue && Now - _heardTime < 3f) { goal = _heard.Value; key = "heard" + (int)_heardTime; }
                else { if (_goalKey.StartsWith("wander") && _path != null && _pathIdx < _path.Count) { goal = _goal; key = _goalKey; } else { goal = Nav.RandomNode(_rng); key = "wander" + _rng.Next(); } }
            }
            else if (s.Team == Team.T)
            {
                if (bomb.State == BombState.Planted) { goal = HoldNear(bomb.Position, 6f); key = "post"; look = bomb.Position; }
                else if (bomb.State == BombState.Dropped && ClosestTeammateTo(bomb.Position) == _me) { goal = bomb.Position; key = "getbomb"; }
                else if (s.HasC4)
                {
                    var site = _g.Map.Sites.FirstOrDefault(z => z.Name == Site);
                    goal = site.Name != null ? new Vector3((site.MinX + site.MaxX) / 2, 0, (site.MinZ + site.MaxZ) / 2) : HoldSpot;
                    goal = Nav.Nearest(goal) >= 0 ? Nav.Nodes[Nav.Nearest(goal)] : goal;
                    key = "plant";
                    if (_g.Map.SiteAt(s.Position) != null && _target == null)
                    {
                        // plant: select the bomb and hold fire
                        if (s.Active != WeaponSlotKind.Bomb) cmd.Select = WeaponSelect.Bomb;
                        else if (s.OnGround) { cmd.Buttons |= Buttons.Attack; pitch = -40f; return; }
                    }
                }
                else { goal = HoldSpot; key = "site" + Site; }
            }
            else
            {
                if (bomb.State == BombState.Planted)
                {
                    goal = bomb.Position; key = "retake";
                    if (Vector3.Distance(s.Position, bomb.Position) < 1.2f)
                    {
                        float left = (bomb.ExplodeTick - _g.Tick) * _g.Dt;
                        float need = s.HasKit ? _g.Config.KitDefuseTime : _g.Config.DefuseTime;
                        if (left > need + 0.1f || s.Defusing) { cmd.Buttons |= Buttons.Use; VMath.AnglesFromDir(bomb.Position - s.EyePosition, out yaw, out pitch); return; }
                    }
                }
                else { goal = HoldSpot; key = "hold" + Site; }
            }

            ThrowUtility(ref cmd, ref yaw, ref pitch);
            if (_throwStage > 0) return;

            if (key != _goalKey || _path == null || _g.Tick >= _repathTick)
            {
                _goalKey = key; _goal = goal;
                _path = Nav.FindPath(s.Position, goal);
                _pathIdx = 0;
                _repathTick = _g.Tick + _g.TickRate * 3;
            }
            bool moving = Follow(ref cmd, ref yaw, ref pitch);
            if (!moving)
            {
                var lp = look ?? (_heard.HasValue && Now - _heardTime < 4f ? _heard.Value : (Vector3?)null);
                if (lp.HasValue) { VMath.AnglesFromDir(lp.Value + new Vector3(0, 1.4f, 0) - s.EyePosition, out float ly, out float lpch); TurnTowards(ref yaw, ref pitch, ly, lpch, 0.4f); }
                else yaw += MathF.Sin(Now * 0.7f + _me.Id) * 25f * _g.Dt;
            }
            else if (_heard.HasValue && Now - _heardTime < 2f)
            {
                VMath.AnglesFromDir(_heard.Value + new Vector3(0, 1.4f, 0) - s.EyePosition, out float hy, out float hp);
                TurnTowards(ref yaw, ref pitch, hy, hp, 0.6f);
            }
            // walk silently near the objective
            if (rounds && s.Team == Team.T && bomb.State != BombState.Planted && Vector3.Distance(s.Position, goal) < 12f && !s.HasC4) cmd.Buttons |= Buttons.Walk;
        }

        private void ThrowUtility(ref PlayerInput cmd, ref float yaw, ref float pitch)
        {
            var s = _me.State;
            if (_throwStage == 0)
            {
                if (_utilityDone || s.Team != Team.T || _g.Bomb.State == BombState.Planted || Skill < 0.2f) return;
                var site = _g.Map.Sites.FirstOrDefault(z => z.Name == Site);
                if (site.Name == null) return;
                var center = new Vector3((site.MinX + site.MaxX) / 2, s.Position.Y, (site.MinZ + site.MaxZ) / 2);
                float d = Vector3.Distance(center, s.Position);
                if (d > 22f || d < 8f) return;
                _utilityDone = true;
                if (s.NadeSmoke == 0 && s.NadeFlash == 0 && s.NadeFire == 0) return;
                _throwStage = 1; _throwTime = Now;
                VMath.AnglesFromDir(center - s.EyePosition, out yaw, out _);
                pitch = 20f;
            }
            var want = s.NadeSmoke > 0 ? GrenadeType.Smoke : s.NadeFlash > 0 ? GrenadeType.Flash : s.FireGrenadeType;
            switch (_throwStage)
            {
                case 1:
                    if (s.Active != WeaponSlotKind.Grenade || s.ActiveGrenade != want) { cmd.Select = WeaponSelect.Grenade; if (Now - _throwTime > 2f) _throwStage = 0; return; }
                    _throwStage = 2; _throwTime = Now; return;
                case 2:
                    cmd.Buttons |= Buttons.Attack;
                    if (Now - _throwTime > 0.7f) _throwStage = 3;
                    return;
                case 3:
                    _throwStage = Now - _throwTime > 1.3f ? 0 : 3;
                    return;
            }
        }

        private Vector3 HoldNear(Vector3 p, float r)
        {
            if (_goalKey == "post") return _goal;
            var off = new Vector3(((float)_rng.NextDouble() - 0.5f) * r, 0, ((float)_rng.NextDouble() - 0.5f) * r);
            int n = Nav.Nearest(p + off);
            return n >= 0 ? Nav.Nodes[n] : p;
        }

        private ServerGame.Player ClosestTeammateTo(Vector3 pos)
        {
            ServerGame.Player best = null; float bd = float.MaxValue;
            foreach (var p in _g.Players)
            {
                if (!p.State.Alive || p.State.Team != _me.State.Team || p.Bot == null) continue;
                float d = Vector3.Distance(p.State.Position, pos);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        private bool Follow(ref PlayerInput cmd, ref float yaw, ref float pitch)
        {
            var s = _me.State;
            if (_path == null || _pathIdx >= _path.Count) return false;
            var p = _path[_pathIdx];
            var to = p - s.Position; to.Y = 0;
            float d = to.Length();
            float reach = _pathIdx == _path.Count - 1 ? 0.35f : 0.6f;
            if (d < reach) { _pathIdx++; return _pathIdx < _path.Count; }
            VMath.AnglesFromDir(to, out float wy, out _);
            TurnTowards(ref yaw, ref pitch, wy, 0f, 0.7f);
            // move relative to the current view so turning and walking are decoupled
            var dir = to / d;
            var f = VMath.FlatForward(yaw); var r = VMath.FlatRight(yaw);
            float fm = Vector3.Dot(dir, f), sm = Vector3.Dot(dir, r);
            if (fm > 0.3f) cmd.Buttons |= Buttons.Forward; else if (fm < -0.3f) cmd.Buttons |= Buttons.Back;
            if (sm > 0.3f) cmd.Buttons |= Buttons.Right; else if (sm < -0.3f) cmd.Buttons |= Buttons.Left;
            // stuck: jump and repath
            if (_g.Tick >= _stuckTick)
            {
                if (Vector3.Distance(_stuckPos, s.Position) < 0.25f) { cmd.Buttons |= Buttons.Jump; _repathTick = _g.Tick; }
                _stuckPos = s.Position; _stuckTick = _g.Tick + _g.TickRate;
            }
            return true;
        }

        private void TurnTowards(ref float yaw, ref float pitch, float wy, float wp, float speedMul)
        {
            float max = TurnSpeed * speedMul * _g.Dt;
            float dy = VMath.AngleDelta(yaw, wy), dp = wp - pitch;
            float ease = MathF.Min(1f, _g.Dt * 14f);
            yaw += MathF.Sign(dy) * MathF.Min(MathF.Abs(dy), MathF.Max(max * 0.15f, MathF.Min(max, MathF.Abs(dy) * ease * 4f)));
            pitch += MathF.Sign(dp) * MathF.Min(MathF.Abs(dp), MathF.Max(max * 0.15f, MathF.Min(max, MathF.Abs(dp) * ease * 4f)));
        }
    }
}
