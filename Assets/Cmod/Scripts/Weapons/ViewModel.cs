using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// A simple procedural first-person view model: a root object parented to the player's camera,
    /// built from primitive parts by the owning weapon. It adds walk bob, mouse sway, a recoil kick
    /// and a deploy raise, plus a per-weapon animation offset (AnimPosition / AnimEuler) the weapon sets
    /// every frame. Weapons create it in Deploy and Destroy it in Holster, so nothing leaks when
    /// weapons are stripped.
    /// </summary>
    public sealed class ViewModel
    {
        public readonly GameObject Root;
        /// <summary>Holds the parts; everything the weapon builds goes under this.</summary>
        public readonly Transform Model;
        /// <summary>Where shots, beams and tracers start. Set by the weapon.</summary>
        public Transform Muzzle;

        /// <summary>Resting local position on the camera (right, down, forward).</summary>
        public Vector3 RestPosition = new Vector3(0.2f, -0.19f, 0.36f);
        /// <summary>Extra animation the weapon sets every frame (swing, reload dip).</summary>
        public Vector3 AnimPosition;
        public Vector3 AnimEuler;

        readonly CmodPlayer _owner;
        float _kick;            // 0..1 recoil amount, decays
        float _deploy = 1f;     // 1 = fully lowered, animates to 0
        float _bobPhase;
        Vector2 _sway;

        public ViewModel(CmodPlayer owner, string name)
        {
            _owner = owner;
            Root = new GameObject("ViewModel_" + name) { layer = WeaponFx.FxLayer };
            if (owner != null && owner.Cam != null) Root.transform.SetParent(owner.Cam.transform, false);
            Root.transform.localPosition = RestPosition + new Vector3(0f, -0.3f, 0f);
            var model = new GameObject("Model") { layer = WeaponFx.FxLayer };
            model.transform.SetParent(Root.transform, false);
            Model = model.transform;
        }

        /// <summary>Adds a box (or other primitive) part with a shared lit material.</summary>
        public Transform Part(string name, Vector3 pos, Vector3 scale, Color color, PrimitiveType shape = PrimitiveType.Cube,
            Transform parent = null, Vector3? euler = null)
        {
            var go = WeaponFx.MakePart(name, parent != null ? parent : Model, shape, pos, scale, WeaponFx.LitShared(color),
                euler.HasValue ? Quaternion.Euler(euler.Value) : (Quaternion?)null);
            return go.transform;
        }

        /// <summary>Adds a self-lit (emissive) part.</summary>
        public Transform GlowPart(string name, Vector3 pos, Vector3 scale, Color color, PrimitiveType shape = PrimitiveType.Cube,
            Transform parent = null, Vector3? euler = null)
        {
            var go = WeaponFx.MakePart(name, parent != null ? parent : Model, shape, pos, scale, WeaponFx.GlowShared(color),
                euler.HasValue ? Quaternion.Euler(euler.Value) : (Quaternion?)null);
            return go.transform;
        }

        /// <summary>An empty marker transform (muzzle, pivot).</summary>
        public Transform Marker(string name, Vector3 pos, Transform parent = null)
        {
            var go = new GameObject(name) { layer = WeaponFx.FxLayer };
            go.transform.SetParent(parent != null ? parent : Model, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        /// <summary>Adds recoil (0..1, accumulates up to 1.5).</summary>
        public void Kick(float amount) => _kick = Mathf.Min(1.5f, _kick + amount);

        public Vector3 MuzzlePosition => Muzzle != null ? Muzzle.position : (Root != null ? Root.transform.position : Vector3.zero);

        /// <summary>Call every frame from the weapon's Think.</summary>
        public void Update(float dt)
        {
            if (Root == null) return;
            dt = Mathf.Max(0f, dt);

            _deploy = Mathf.MoveTowards(_deploy, 0f, dt * 4f);
            _kick = Mathf.MoveTowards(_kick, 0f, dt * (3f + _kick * 6f));

            // Walk bob, scaled by horizontal speed (only on the ground).
            // (Only contract members of CmodPlayer are used: a small vertical speed means "on the ground".)
            float speed = 0f;
            bool grounded = true;
            if (_owner != null)
            {
                var v = _owner.Velocity;
                grounded = !_owner.Noclip && Mathf.Abs(v.y) < 0.6f;
                v.y = 0f;
                speed = v.magnitude;
            }
            float bobAmt = grounded ? Mathf.Clamp01(speed / 9f) : 0f;
            _bobPhase += dt * (4f + speed * 0.9f);
            var bob = new Vector3(Mathf.Sin(_bobPhase) * 0.012f, -Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.012f, 0f) * bobAmt;

            // Sway: lag behind mouse movement a little.
            var look = CmodInput.LookDelta;
            _sway = Vector2.Lerp(_sway, Vector2.ClampMagnitude(look, 6f), 1f - Mathf.Exp(-dt * 10f));

            var pos = RestPosition + bob + AnimPosition
                      + new Vector3(-_sway.x * 0.004f, -_sway.y * 0.004f, -_kick * 0.06f)
                      + new Vector3(0f, -_deploy * _deploy * 0.3f, 0f);
            var rot = Quaternion.Euler(AnimEuler + new Vector3(-_kick * 9f + _sway.y * 0.6f, -_sway.x * 0.6f, 0f));
            Root.transform.localPosition = pos;
            Root.transform.localRotation = rot;
        }

        public void SetVisible(bool visible)
        {
            if (Root != null) Root.SetActive(visible);
        }

        public void Destroy() => WeaponFx.DestroyWithMaterials(Root);
    }
}
