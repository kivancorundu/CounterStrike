using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Vexa.Core;
using Vexa.Core.Server;
using Vexa.Net;

// vexa-server --port 27015 --tick 64 --map kasaba --mode competitive --difficulty 0.5
// modes: competitive (5v5 MR12, bots fill empty slots), casual, deathmatch, practice
int port = 27015, tick = 64, bots = 0, maxPlayers = 12;
string mapName = "training", modeName = "deathmatch";
float difficulty = 0.5f;
for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--tick": tick = int.Parse(args[++i]); break;
        case "--map": mapName = args[++i]; break;
        case "--bots": bots = int.Parse(args[++i]); break;
        case "--max-players": maxPlayers = int.Parse(args[++i]); break;
        case "--mode": modeName = args[++i].ToLowerInvariant(); break;
        case "--difficulty": difficulty = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
    }
}
if (tick != 64 && tick != 128) { Console.WriteLine("tick must be 64 or 128"); return 1; }

string mapPath = Path.Combine(AppContext.BaseDirectory, "Maps", mapName + ".vxmap");
var map = MapData.Parse(File.ReadAllText(mapPath));
using var transport = LiteNetTransport.StartServer(port, maxPlayers);
MatchConfig config;
switch (modeName)
{
    case "competitive": config = MatchConfig.Competitive(); break;
    case "casual": config = MatchConfig.Casual(); break;
    case "practice": config = MatchConfig.Practice(); break;
    case "deathmatch": config = MatchConfig.Deathmatch(); break;
    default: Console.WriteLine("unknown mode: " + modeName); return 1;
}
config.BotDifficulty = Math.Clamp(difficulty, 0f, 1f);
var game = new ServerGame(transport, map, tick, config);
game.Log += m => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");
for (int i = 0; i < bots; i++) game.AddBot("Bot " + (i + 1));
Console.WriteLine($"VEXA dedicated server | {config.Mode} | map {map.Name} | {tick} tick | UDP {port} | {bots} extra bots");

bool running = true;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

// fixed-rate loop with a precise sleep (sleep coarse, spin the last ~1 ms)
var sw = Stopwatch.StartNew();
double dt = 1.0 / tick;
double next = sw.Elapsed.TotalSeconds;
long ticks = 0; double worst = 0; var statT = sw.Elapsed.TotalSeconds;
while (running)
{
    double now = sw.Elapsed.TotalSeconds;
    if (now < next)
    {
        double wait = next - now;
        if (wait > 0.002) Thread.Sleep((int)((wait - 0.001) * 1000));
        else Thread.SpinWait(50);
        continue;
    }
    double t0 = sw.Elapsed.TotalSeconds;
    game.Step();
    double cost = sw.Elapsed.TotalSeconds - t0;
    worst = Math.Max(worst, cost);
    ticks++;
    next += dt;
    if (now - next > 0.25) next = now; // fell far behind: don't spiral
    if (now - statT > 30)
    {
        Console.WriteLine($"[stats] players {game.Players.Count} | worst tick {worst * 1000:0.00} ms (budget {dt * 1000:0.0} ms)");
        statT = now; worst = 0;
    }
}
Console.WriteLine("server stopped");
return 0;
