using System;
using System.Collections.Generic;
using Vexa.Core.Net;

namespace Vexa.Core.Server
{
    /// <summary>
    /// Team kills: a money fine, a warning in chat, and after <see cref="MatchConfig.TeamKillKickLimit"/> of them in a
    /// match the player is kicked and can't rejoin that match under the same name. Warmup kills don't count; bots pay
    /// the fine but are never kicked (they would only be replaced by another bot).
    /// </summary>
    public sealed partial class ServerGame
    {
        private readonly HashSet<string> _banned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int peer, int tick)> _drops = new List<(int, int)>();

        private void OnTeamKill(Player attacker, Player victim)
        {
            bool fined = Config.HasEconomy && Config.TeamKillPenalty > 0;
            if (fined) AddMoney(attacker, -Config.TeamKillPenalty);
            if (Phase == GamePhase.Warmup) return;
            attacker.TeamKills++;
            int limit = Config.TeamKillKickLimit;
            bool kickable = limit > 0 && !attacker.IsBot;
            if (kickable && attacker.TeamKills >= limit)
            {
                Kick(attacker, $"{limit} takım arkadaşını öldürdüğün için maçtan atıldın.");
                SystemChat($"{attacker.Name} takım arkadaşlarını öldürdüğü için maçtan atıldı.");
                return;
            }
            string text = $"Takım arkadaşın {victim.Name} öldü.";
            if (fined) text += $" Ceza: -${Config.TeamKillPenalty}.";
            if (kickable) text += $" Uyarı {attacker.TeamKills}/{limit}: {limit}. seferde maçtan atılırsın.";
            SystemChat(text, attacker);
        }

        /// <summary>Removes a player from the match with a reason shown on their screen, and keeps the name out until
        /// the match is over.</summary>
        public void Kick(Player p, string reason)
        {
            if (p.Kicked) return;
            p.Kicked = true;
            p.Pending.Clear();
            _banned.Add(p.Name);
            Info($"kicked {p.Name}: {reason}");
            if (p.IsBot) { RemovePlayer(p); OnPlayerLeft(p); return; }
            WriteKicked(reason);
            SendTo(p, Delivery.ReliableOrdered);
            // give the reliable message time to arrive before the connection goes
            _drops.Add((p.Peer, Tick + TickRate / 2));
        }

        public bool IsBanned(string name) => _banned.Contains(name);

        private void RefuseBanned(int peer)
        {
            WriteKicked("Bu maçtan atıldın, maç bitene kadar geri giremezsin.");
            _net.Send(peer, _w.Data, _w.Length, Delivery.ReliableOrdered);
            _drops.Add((peer, Tick + TickRate / 2));
        }

        private void WriteKicked(string reason)
        {
            _w.Reset();
            _w.Byte((byte)Msg.Kicked);
            _w.String(reason);
        }

        private void DropKicked()
        {
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                var (peer, tick) = _drops[i];
                if (Tick < tick) continue;
                _drops.RemoveAt(i);
                _net.Disconnect(peer);
                // not every transport reports a disconnect the server started itself
                if (_byPeer.TryGetValue(peer, out var p)) { RemovePlayer(p); OnPlayerLeft(p); }
            }
        }

        /// <summary>A new match lets everyone back in.</summary>
        private void ClearBans() => _banned.Clear();
    }
}
