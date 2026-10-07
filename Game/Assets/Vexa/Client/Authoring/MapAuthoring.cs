using UnityEngine;
using Vexa.Core;

namespace Vexa.Client.Authoring
{
    /// <summary>
    /// Put on any GameObject with a BoxCollider to choose its surface (affects wall penetration and sounds).
    /// Colliders without this component export as concrete.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VexaSurface : MonoBehaviour
    {
        public SurfaceMaterial Material = SurfaceMaterial.Concrete;
    }

    /// <summary>Spawn point. Team None = deathmatch spawn. The transform's Y rotation is the facing direction.</summary>
    public sealed class VexaSpawn : MonoBehaviour
    {
        public Team Team = Team.None;

        void OnDrawGizmos()
        {
            Gizmos.color = Team == Team.CT ? new Color(0.3f, 0.5f, 1f) : Team == Team.T ? new Color(1f, 0.7f, 0.2f) : Color.white;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(0.8f, 1.8f, 0.8f));
            Gizmos.DrawLine(transform.position + Vector3.up * 1.6f, transform.position + Vector3.up * 1.6f + transform.forward * 0.8f);
        }
    }

    public enum ZoneKind { BombSite, BuyZone }

    /// <summary>Bomb site or buy zone. Uses the attached BoxCollider's bounds (set it to trigger).</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VexaZone : MonoBehaviour
    {
        public ZoneKind Kind = ZoneKind.BombSite;
        public string SiteName = "A";
        public Team BuyTeam = Team.T;

        void OnDrawGizmos()
        {
            var c = GetComponent<BoxCollider>();
            if (c == null) return;
            Gizmos.color = Kind == ZoneKind.BombSite ? new Color(1f, 0.2f, 0.2f, 0.25f) : new Color(0.2f, 1f, 0.2f, 0.2f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        }
    }
}
