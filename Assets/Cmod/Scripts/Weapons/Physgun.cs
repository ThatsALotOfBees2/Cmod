using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// The physics gun.
    ///   LMB (hold)       grab the entity under the crosshair (Hooks.PhysgunPickup can veto); a frozen prop unfreezes.
    ///                    The grab point stays where you grabbed it, and the object keeps its orientation relative to your yaw.
    ///                    It is steered with velocities in FixedThink (no teleporting), so it collides and can be thrown.
    ///   Scroll           pull it closer / push it away while holding.
    ///   E + mouse        rotate it (mouse look is blocked); hold Shift to snap the rotation to 45 degrees.
    ///   RMB              freeze it in place and let go.
    ///   R                unfreeze the entity under the crosshair and everything welded to it.
    /// Visuals: a curved cyan beam from the muzzle to the grab point, a glow sprite at the end and a procedural view model.
    /// </summary>
    public sealed class Physgun : WeaponBase
    {
        public override string Id => "weapon_physgun";
        public override string PrintName => "Physics Gun";
        public override string Category => "Tools";
        public override int Slot => 0;

        const float Range = 1000f;
        const float MinDistance = 0.8f;
        const float MaxLinearSpeed = 80f;
        const float MaxAngularSpeed = 25f;
        const int BeamPoints = 24;
        static readonly Color Cyan = new Color(0.2f, 0.85f, 1f, 1f);
        static readonly Color CyanSoft = new Color(0.2f, 0.85f, 1f, 0.55f);

        // ---- Held object ----------------------------------------------------------------------
        CmodEntity _held;
        Rigidbody _body;
        Vector3 _localGrab;         // grab point in the entity's local space
        float _distance;            // from the eye to the grab point
        Quaternion _rotRel;         // object rotation relative to the player's yaw
        bool _hadGravity;
        RigidbodyInterpolation _oldInterp;
        float _oldMaxAngVel;

        // ---- Input state ----------------------------------------------------------------------
        bool _lmb;
        bool _rotating;
        bool _snapping;
        Vector3 _aimPoint;          // where the beam ends when nothing is held
        bool _aimHit;

        // ---- Visuals ----------------------------------------------------------------------------
        ViewModel _vm;
        Transform _core;
        GlowSprite _muzzleGlow;
        Light _muzzleLight;
        LineRenderer _beam;
        LineRenderer _beamCore;
        GlowSprite _endGlow;
        readonly Vector3[] _pts = new Vector3[BeamPoints];
        float _spin;

        public bool IsHolding => _held != null && _body != null;
        public CmodEntity Held => IsHolding ? _held : null;

        public override bool BlocksMouseLook => IsHolding && CmodInput.Key(KeyCode.E);
        public override bool UsesScrollWheel => IsHolding;

        // =====================================================================================
        // Lifecycle
        // =====================================================================================

        public override void Deploy() => BuildVisuals();

        public override void Holster()
        {
            Release();
            _lmb = false;
            DestroyVisuals();
        }

        void BuildVisuals()
        {
            DestroyVisuals();
            _vm = new ViewModel(Owner, "physgun");
            _vm.RestPosition = new Vector3(0.21f, -0.2f, 0.36f);
            var dark = new Color(0.16f, 0.17f, 0.19f);
            var mid = new Color(0.36f, 0.38f, 0.41f);

            _vm.Part("Body", new Vector3(0f, 0f, -0.02f), new Vector3(0.085f, 0.09f, 0.24f), mid);
            _vm.Part("BodyTop", new Vector3(0f, 0.052f, -0.04f), new Vector3(0.06f, 0.02f, 0.16f), dark);
            _vm.Part("Grip", new Vector3(0f, -0.09f, -0.08f), new Vector3(0.045f, 0.11f, 0.055f), dark, PrimitiveType.Cube, null, new Vector3(-12f, 0f, 0f));
            _vm.GlowPart("StripL", new Vector3(-0.044f, 0.012f, -0.02f), new Vector3(0.004f, 0.012f, 0.2f), Cyan);
            _vm.GlowPart("StripR", new Vector3(0.044f, 0.012f, -0.02f), new Vector3(0.004f, 0.012f, 0.2f), Cyan);
            _vm.GlowPart("StripTop", new Vector3(0f, 0.063f, -0.04f), new Vector3(0.012f, 0.004f, 0.14f), Cyan);
            // Front: a spinning core between three prongs.
            _core = _vm.GlowPart("Core", new Vector3(0f, 0f, 0.13f), new Vector3(0.035f, 0.02f, 0.035f), Cyan, PrimitiveType.Cylinder, null, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                var off = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.035f;
                _vm.Part("Prong" + i, new Vector3(off.x, off.y, 0.14f), new Vector3(0.012f, 0.012f, 0.08f), dark);
            }
            _vm.Muzzle = _vm.Marker("Muzzle", new Vector3(0f, 0f, 0.17f));

            _muzzleGlow = WeaponFx.MakeGlowSprite("MuzzleGlow", _vm.Muzzle, CyanSoft, 0.06f);
            var lightGo = new GameObject("MuzzleLight") { layer = WeaponFx.FxLayer };
            lightGo.transform.SetParent(_vm.Muzzle, false);
            _muzzleLight = lightGo.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.color = Cyan;
            _muzzleLight.range = 2.5f;
            _muzzleLight.intensity = 0f;
            _muzzleLight.shadows = LightShadows.None;

            // Beam: a wide soft outer line plus a thin bright core, both world-space.
            _beam = WeaponFx.MakeLine("PhysgunBeam", null, CyanSoft, 0.05f, BeamPoints);
            _beam.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f));   // thin at the gun, wide at the object
            _beamCore = WeaponFx.MakeLine("PhysgunBeamCore", _beam.transform, new Color(0.85f, 1f, 1f, 0.9f), 0.012f, BeamPoints);
            _beamCore.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            _endGlow = WeaponFx.MakeGlowSprite("PhysgunEndGlow", _beam.transform, Cyan, 0.45f);
            SetBeamVisible(false);
        }

        void DestroyVisuals()
        {
            _vm?.Destroy();
            _vm = null;
            if (_beam != null) WeaponFx.DestroyWithMaterials(_beam.gameObject);
            _beam = null;
            _beamCore = null;
            _endGlow = null;
            _muzzleGlow = null;
            _muzzleLight = null;
            _core = null;
        }

        // =====================================================================================
        // Grabbing
        // =====================================================================================

        bool TryGrab()
        {
            _aimHit = Owner.TraceAim(out var hit, Range);
            _aimPoint = _aimHit ? hit.point : Owner.EyePosition + Owner.AimRay.direction * 30f;
            if (!_aimHit) return false;

            var ent = EntityManager.FromCollider(hit.collider);
            if (ent == null || ent.Body == null) return false;
            if (!Hooks.RunAllowed(Hooks.PhysgunPickup, ent)) return false;

            _held = ent;
            _body = ent.Body;
            if (ent.Frozen) ent.Frozen = false;   // grabbing a frozen prop unfreezes it
            _localGrab = ent.transform.InverseTransformPoint(hit.point);
            _distance = Mathf.Max(MinDistance, hit.distance);
            _rotRel = Quaternion.Inverse(YawRotation()) * _body.rotation;

            _hadGravity = _body.useGravity;
            _oldInterp = _body.interpolation;
            _oldMaxAngVel = _body.maxAngularVelocity;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.maxAngularVelocity = MaxAngularSpeed + 5f;
            _body.WakeUp();

            WeaponFx.Play("physgun_grab", 0.35f);
            return true;
        }

        /// <summary>Lets go, keeping the object's current velocity (so it can be thrown).</summary>
        void Release()
        {
            if (_body != null)
            {
                _body.useGravity = _hadGravity;
                _body.interpolation = _oldInterp;
                _body.maxAngularVelocity = _oldMaxAngVel > 0f ? _oldMaxAngVel : 7f;
                if (!_body.isKinematic) _body.WakeUp();
            }
            _held = null;
            _body = null;
            _rotating = false;
            _snapping = false;
        }

        Quaternion YawRotation() => Quaternion.Euler(0f, Owner != null ? Owner.LookYaw : 0f, 0f);

        /// <summary>The rotation the object is steered toward (relative to yaw), snapped while Shift-rotating.</summary>
        Quaternion TargetRelative => _snapping ? Snap45(_rotRel) : _rotRel;

        static Quaternion Snap45(Quaternion q)
        {
            var e = q.eulerAngles;
            e.x = Mathf.Round(e.x / 45f) * 45f;
            e.y = Mathf.Round(e.y / 45f) * 45f;
            e.z = Mathf.Round(e.z / 45f) * 45f;
            return Quaternion.Euler(e);
        }

        Vector3 GrabPointWorld => _held != null ? _held.transform.TransformPoint(_localGrab) : _aimPoint;

        // =====================================================================================
        // Input
        // =====================================================================================

        public override void PrimaryHeld(bool held)
        {
            _lmb = held;
            if (!held)
            {
                if (IsHolding) Release();
                return;
            }
            // Keep trying while the button is held, so sweeping the beam over an object picks it up.
            if (!IsHolding) TryGrab();
        }

        public override void SecondaryAttack()
        {
            if (!IsHolding) return;
            var ent = _held;
            var body = _body;
            var at = GrabPointWorld;
            Release();
            PhysicsCompat.SetVelocity(body, Vector3.zero);
            body.angularVelocity = Vector3.zero;
            ent.Frozen = true;
            WeaponFx.Play("physgun_freeze", 0.6f);
            WeaponFx.FlashSprite(at, Cyan, 0.9f, 0.3f);
            WeaponFx.FlashLight(at, Cyan, 2.5f, 4f, 0.25f);
        }

        public override void Reload()
        {
            if (Owner == null || !Owner.TraceAim(out var hit, Range)) return;
            var ent = EntityManager.FromCollider(hit.collider);
            if (ent == null) return;

            int count = 0;
            foreach (var e in Constraints.GetConnected(ent, ConstraintType.Weld))
            {
                if (e == null || e.Body == null || !e.Frozen) continue;
                e.Frozen = false;
                count++;
                WeaponFx.FlashSprite(ToolHelpers.Center(e), CyanSoft, 0.7f, 0.25f);
            }
            if (count == 0) return;
            WeaponFx.Play("physgun_unfreeze", 0.6f);
            ToolHelpers.Notify(count == 1 ? "Unfroze 1 object" : $"Unfroze {count} objects");
        }

        public override void Think(float dt)
        {
            if (_vm == null || _vm.Root == null) BuildVisuals();

            // The held object was removed, frozen by something else, or lost its body.
            if (!ReferenceEquals(_held, null) && (_held == null || _body == null || _body.isKinematic)) Release();

            bool holding = IsHolding;

            // Rotation with E + mouse (Shift snaps to 45 degrees).
            bool wasSnapping = _snapping;
            _rotating = holding && CmodInput.Key(KeyCode.E);
            _snapping = _rotating && (CmodInput.Key(KeyCode.LeftShift) || CmodInput.Key(KeyCode.RightShift));
            if (wasSnapping && !_snapping && holding) _rotRel = Snap45(_rotRel);   // keep the snapped pose when snapping ends
            if (_rotating)
            {
                var d = CmodInput.LookDelta;
                // Rotate in the player's yaw frame: mouse X spins around up, mouse Y tips around right.
                _rotRel = Quaternion.AngleAxis(d.x, Vector3.up) * Quaternion.AngleAxis(-d.y, Vector3.right) * _rotRel;
            }

            // Scroll: distance.
            if (holding)
            {
                float s = CmodInput.Scroll;
                if (Mathf.Abs(s) > 0.01f)
                    _distance = Mathf.Clamp(_distance + s * Mathf.Max(0.25f, _distance * 0.12f), MinDistance, Range);
            }
            else if (_lmb)
            {
                // Not holding but beaming: refresh where the beam points (TryGrab already traced this frame
                // when PrimaryHeld ran, but Think runs first, so trace here too for an up-to-date end point).
                _aimHit = Owner.TraceAim(out var hit, Range);
                _aimPoint = _aimHit ? hit.point : Owner.EyePosition + Owner.AimRay.direction * 30f;
            }

            _vm.Update(dt);
            UpdateVisuals(dt);
        }

        // =====================================================================================
        // Physics steering
        // =====================================================================================

        public override void FixedThink(float dt)
        {
            if (!IsHolding || Owner == null || dt <= 0f) return;
            if (_body.isKinematic) { Release(); return; }

            // Linear: move the grab point toward the spot in front of the eye.
            var ray = Owner.AimRay;
            var target = ray.origin + ray.direction * _distance;
            var current = _held.transform.TransformPoint(_localGrab);
            var desiredVel = (target - current) * (0.45f / dt);
            desiredVel = Vector3.ClampMagnitude(desiredVel, MaxLinearSpeed);
            var vel = PhysicsCompat.GetVelocity(_body);
            PhysicsCompat.SetVelocity(_body, Vector3.Lerp(vel, desiredVel, 0.65f));

            // Angular: turn toward yaw * relative rotation; replace (damp) the existing spin.
            var targetRot = YawRotation() * TargetRelative;
            var dq = targetRot * Quaternion.Inverse(_body.rotation);
            dq.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 desiredAng = Vector3.zero;
            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x) && Mathf.Abs(angle) > 0.01f)
                desiredAng = axis.normalized * (angle * Mathf.Deg2Rad * (0.35f / dt));
            desiredAng = Vector3.ClampMagnitude(desiredAng, MaxAngularSpeed);
            _body.angularVelocity = Vector3.Lerp(_body.angularVelocity, desiredAng, 0.6f);
        }

        // =====================================================================================
        // Visuals
        // =====================================================================================

        void SetBeamVisible(bool on)
        {
            if (_beam != null) _beam.enabled = on;
            if (_beamCore != null) _beamCore.enabled = on;
            if (_endGlow != null) _endGlow.gameObject.SetActive(on);
        }

        void UpdateVisuals(float dt)
        {
            bool holding = IsHolding;
            bool beamOn = _lmb && (holding || _aimHit);

            _spin += dt * (holding ? 900f : _lmb ? 500f : 120f);
            if (_core != null) _core.localRotation = Quaternion.Euler(90f, _spin, 0f);
            if (_muzzleLight != null) _muzzleLight.intensity = Mathf.MoveTowards(_muzzleLight.intensity, beamOn ? 1.6f : 0.25f, dt * 10f);
            if (_muzzleGlow != null) _muzzleGlow.Size = beamOn ? 0.09f + Mathf.Sin(Time.time * 30f) * 0.01f : 0.05f;

            SetBeamVisible(beamOn);
            if (!beamOn || _vm == null) return;

            // Quadratic Bezier: muzzle -> a control point straight ahead of the eye -> grab point.
            // When the object lags behind or is held off-center, the beam bends toward it like GMod's.
            var p0 = _vm.MuzzlePosition;
            var p2 = GrabPointWorld;
            var ray = Owner.AimRay;
            float d = holding ? _distance : Vector3.Distance(ray.origin, p2);
            var p1 = ray.origin + ray.direction * (d * 0.55f);
            for (int i = 0; i < BeamPoints; i++)
            {
                float t = i / (float)(BeamPoints - 1);
                float u = 1f - t;
                _pts[i] = u * u * p0 + 2f * u * t * p1 + t * t * p2;
            }
            _beam.SetPositions(_pts);
            _beamCore.SetPositions(_pts);

            // Beam flicker and the end glow.
            float flicker = 0.85f + Mathf.PerlinNoise(Time.time * 12f, 0.3f) * 0.3f;
            _beam.widthMultiplier = (holding ? 0.06f : 0.035f) * flicker;
            _beamCore.widthMultiplier = (holding ? 0.014f : 0.008f) * flicker;
            _endGlow.transform.position = p2;
            _endGlow.Size = holding ? 0.5f * flicker : 0.25f;
        }

        public override void DrawHUD()
        {
            if (IsHolding)
                WeaponHud.DrawHint(_rotating
                    ? "Mouse: rotate    Shift: snap to 45°    release E to stop"
                    : "Scroll: distance    E + mouse: rotate    RMB: freeze    release LMB: drop");
            WeaponHud.DrawNotice();
        }
    }
}
