using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Vexa.Core;
using NVec3 = System.Numerics.Vector3;

namespace Vexa.Client
{
    public static class UnityBridge
    {
        public static Vector3 ToU(this NVec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static NVec3 ToN(this Vector3 v) => new NVec3(v.x, v.y, v.z);

        /// <summary>Core angles (yaw 0 = +Z, pitch up positive) to a Unity rotation.</summary>
        public static Quaternion ViewRotation(float yaw, float pitch) => Quaternion.Euler(-pitch, yaw, 0f);
    }

    /// <summary>Loads ".vxmap" files from StreamingAssets on every platform (Android needs UnityWebRequest).</summary>
    public static class MapLoader
    {
        public static MapData Load(string name)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Maps", name + ".vxmap");
            string text;
            if (path.Contains("://") || path.Contains(":///"))
            {
                using (var req = UnityWebRequest.Get(path))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone) { }
                    text = req.downloadHandler.text;
                }
            }
            else text = File.ReadAllText(path);
            return MapData.Parse(text);
        }
    }
}
