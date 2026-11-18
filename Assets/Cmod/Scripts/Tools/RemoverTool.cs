using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Remover: LMB removes the entity, RMB removes it plus everything constrained to it.
    /// Undo respawns the removed entities (same prop, pose, color, frozen state) and rebuilds their
    /// constraints, including ones to entities that still exist.
    /// </summary>
    public sealed class RemoverTool : ToolMode
    {
        public override string Id => "remover";
        public override string Name => "Remover";
        public override string Category => "Construction";
        public override string Description => "Removes objects from the world.";
        public override string Instructions => "LMB: remove object   RMB: remove object and everything constrained to it";

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null) return false;
            RemoveSet(new List<CmodEntity> { ent }, "Remover");
            return true;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null) return false;
            var set = Constraints.GetConnected(ent);
            var list = new List<CmodEntity>(set);
            RemoveSet(list, "Remover (constrained)");
            if (list.Count > 1) ToolHelpers.Notify($"Removed {list.Count} objects");
            return true;
        }

        /// <summary>Snapshots, removes and pushes one undo entry for a group of entities.</summary>
        static void RemoveSet(List<CmodEntity> ents, string label)
        {
            // Capture everything before anything is destroyed (removal prunes constraints).
            var snaps = new List<ToolEntities.Snapshot>();
            var cons = new List<Constraint>();
            var seen = new HashSet<Constraint>();
            foreach (var e in ents)
            {
                if (e == null) continue;
                snaps.Add(ToolEntities.Capture(e));
                foreach (var c in Constraints.For(e)) if (seen.Add(c)) cons.Add(c);
            }

            foreach (var e in ents)
            {
                if (e == null) continue;
                var pos = ToolHelpers.Center(e);
                WeaponFx.FlashSprite(pos, new Color(1f, 1f, 1f, 0.8f), 1.2f, 0.25f);
                WeaponFx.PlayAt("remove", pos, 0.8f, Random.Range(0.95f, 1.1f));
                // Make sure the constraints go even if the EntityRemoved hook is not wired.
                Constraints.RemoveAll(e);
                EntityManager.Remove(e);
            }

            UndoManager.Push(label, () =>
            {
                var map = new Dictionary<CmodEntity, CmodEntity>(RefComparer<CmodEntity>.Instance);
                foreach (var s in snaps)
                {
                    var restored = ToolEntities.Restore(s);
                    if (restored != null) map[s.Original] = restored;
                }
                foreach (var c in cons)
                {
                    Constraints.Recreate(c, old =>
                    {
                        if (old is null) return null;
                        if (map.TryGetValue(old, out var r)) return r;
                        return old != null ? old : null;   // still alive (not part of this removal)?
                    });
                }
            });
        }
    }
}
