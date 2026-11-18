using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Base for tools that need two clicks (weld, rope): the first click stores a point, the second
    /// completes the action. The pending point is stored in the first entity's local space, so it
    /// follows the object if it moves between clicks. Holstering or switching tools cancels it.
    /// </summary>
    public abstract class TwoClickTool : ToolMode, IToolStatus
    {
        protected bool HasFirst;
        protected CmodEntity FirstEnt;      // null = world
        protected bool FirstIsEntity;
        protected Vector3 FirstLocal;       // entity-local, or world when FirstEnt is null

        protected abstract string FirstPrompt { get; }
        protected abstract string SecondPrompt { get; }

        public virtual string Status => HasFirst ? SecondPrompt : FirstPrompt;

        protected void SetFirst(RaycastHit hit, CmodEntity ent)
        {
            HasFirst = true;
            FirstEnt = ent;
            FirstIsEntity = ent != null;
            FirstLocal = ent != null ? ent.transform.InverseTransformPoint(hit.point) : hit.point;
        }

        /// <summary>The stored first point in world space now.</summary>
        protected Vector3 FirstWorld => FirstEnt != null ? FirstEnt.transform.TransformPoint(FirstLocal) : FirstLocal;

        protected void ClearFirst()
        {
            HasFirst = false;
            FirstEnt = null;
            FirstIsEntity = false;
        }

        public override void Think(CmodPlayer player)
        {
            // The first object was removed between clicks: start over.
            if (HasFirst && FirstIsEntity && FirstEnt == null) ClearFirst();
        }

        public override void Holster() => ClearFirst();
    }

    /// <summary>Helpers shared by the tool modes.</summary>
    public static class ToolHelpers
    {
        /// <summary>Pushes an undo entry that recreates the given (removed) constraints.</summary>
        public static void PushRecreateUndo(string label, List<Constraint> removed)
        {
            if (removed == null || removed.Count == 0) return;
            var copy = new List<Constraint>(removed);
            UndoManager.Push(label, () =>
            {
                foreach (var c in copy) Constraints.Recreate(c);
            });
        }

        /// <summary>Pushes an undo entry that removes a constraint (if it still exists).</summary>
        public static void PushRemoveUndo(string label, Constraint c)
        {
            if (c == null) return;
            UndoManager.Push(label, () => Constraints.Remove(c));
        }

        /// <summary>Removes the entity if it still exists.</summary>
        public static void PushRemoveEntityUndo(string label, CmodEntity e)
        {
            if (e == null) return;
            UndoManager.Push(label, () => { if (e != null) EntityManager.Remove(e); });
        }

        /// <summary>The visual center of an entity (renderer bounds), for effects.</summary>
        public static Vector3 Center(CmodEntity e)
        {
            if (e == null) return Vector3.zero;
            if (e.Renderer != null) return e.Renderer.bounds.center;
            return e.transform.position;
        }

        /// <summary>The latest short notice ("Removed 3 objects"); the toolgun and physgun HUDs show it for a few seconds.</summary>
        public static string LastNotice { get; private set; }
        public static float LastNoticeTime { get; private set; } = -100f;

        /// <summary>Shows a short notice on the weapon HUD and logs it to the console.</summary>
        public static void Notify(string text)
        {
            LastNotice = text;
            LastNoticeTime = Time.unscaledTime;
            CmodLog.Info(text);
        }
    }
}
