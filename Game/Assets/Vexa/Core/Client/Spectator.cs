using System.Collections.Generic;

namespace Vexa.Core.Client
{
    /// <summary>
    /// Who the local player watches while dead (or as a spectator / in a demo). Like CS competitive:
    /// players on a team may only watch living teammates; spectators may watch anyone. When the watched
    /// player dies the camera moves on after a short delay.
    /// </summary>
    public sealed class Spectator
    {
        public enum ViewMode { InEye, Chase }

        public int Target { get; private set; }
        public ViewMode Mode = ViewMode.InEye;
        private float _targetDiedAt = -1;
        private readonly List<int> _candidates = new List<int>();
        public const float SwitchDelay = 1.5f;

        public IReadOnlyList<int> Candidates => _candidates;

        void Refresh(ClientGame c)
        {
            _candidates.Clear();
            var mine = c.LocalTeam;
            foreach (var r in c.Remotes)
            {
                if (!c.TryGetRemotePose(r.Id, out var pose) || !pose.Alive) continue;
                if (mine != Team.None && pose.Team != mine) continue;
                _candidates.Add(r.Id);
            }
            _candidates.Sort();
        }

        /// <summary>Call every frame while the local player is not alive. Returns true if there is someone to watch.</summary>
        public bool Update(ClientGame c, float now)
        {
            Refresh(c);
            if (_candidates.Count == 0) { Target = 0; return false; }
            if (!_candidates.Contains(Target))
            {
                // give the death a moment on screen before moving on
                if (Target != 0 && _targetDiedAt < 0) _targetDiedAt = now;
                if (Target == 0 || now - _targetDiedAt >= SwitchDelay) { Target = _candidates[0]; _targetDiedAt = -1; }
            }
            else _targetDiedAt = -1;
            return Target != 0;
        }

        public bool WatchingDeadTarget => _targetDiedAt >= 0;

        public void Next(int dir)
        {
            if (_candidates.Count == 0) return;
            int i = _candidates.IndexOf(Target);
            i = i < 0 ? 0 : (i + dir + _candidates.Count) % _candidates.Count;
            Target = _candidates[i];
            _targetDiedAt = -1;
        }

        public void Watch(int id) { if (_candidates.Contains(id)) { Target = id; _targetDiedAt = -1; } }
        public void ToggleMode() => Mode = Mode == ViewMode.InEye ? ViewMode.Chase : ViewMode.InEye;
        public void Reset() { Target = 0; _targetDiedAt = -1; }
    }
}
