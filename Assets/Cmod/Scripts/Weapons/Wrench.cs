using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// A melee wrench with a 2 m reach. LMB is a quick swing; RMB a slower, heavier swing that hits harder.
    /// The hit lands at the strike point of the swing animation (a forgiving sphere trace), pushes
    /// rigidbodies with an impulse, pops balloons and plays a metal clank (or a dull thud on the world).
    /// </summary>
    public sealed class Wrench : WeaponBase
    {
        public override string Id => "weapon_wrench";
        public override string PrintName => "Wrench";
        public override string Category => "Weapons";
        public override int Slot => 3;

        const float Reach = 2f;
        const float TraceRadius = 0.12f;

        struct SwingKind
        {
            public float WindUp;     // seconds until the hit
            public float Duration;   // whole animation
            public float Cooldown;   // until the next swing
            public float Impulse;    // N*s
            public float Arc;        // degrees of the swing
        }

        static readonly SwingKind Light = new SwingKind { WindUp = 0.11f, Duration = 0.38f, Cooldown = 0.42f, Impulse = 30f, Arc = 75f };
        static readonly SwingKind Heavy = new SwingKind { WindUp = 0.3f, Duration = 0.7f, Cooldown = 0.9f, Impulse = 80f, Arc = 110f };

        ViewModel _vm;
        float _nextSwing;
        bool _swinging;
        bool _hitDone;
        float _swingStart;
        SwingKind _kind;

        public override void Deploy() => BuildViewModel();

        public override void Holster()
        {
            _swinging = false;
            _vm?.Destroy();
            _vm = null;
        }

        void BuildViewModel()
        {
            _vm?.Destroy();
            _vm = new ViewModel(Owner, "wrench");
            _vm.RestPosition = new Vector3(0.2f, -0.2f, 0.34f);
            var steel = new Color(0.62f, 0.64f, 0.67f);
            var dark = new Color(0.3f, 0.31f, 0.33f);
            var grip = new Color(0.55f, 0.08f, 0.06f);

            // Pivot at the hand; the wrench points up and forward from it.
            var pivot = _vm.Marker("Pivot", Vector3.zero);
            pivot.localRotation = Quaternion.Euler(-35f, 0f, 8f);
            _vm.Part("Grip", new Vector3(0f, 0.02f, 0f), new Vector3(0.03f, 0.1f, 0.03f), grip, PrimitiveType.Cube, pivot);
            _vm.Part("Shaft", new Vector3(0f, 0.15f, 0f), new Vector3(0.022f, 0.18f, 0.012f), steel, PrimitiveType.Cube, pivot);
            // Open-ended jaw: a head block with two prongs.
            _vm.Part("Head", new Vector3(0f, 0.255f, 0f), new Vector3(0.06f, 0.035f, 0.016f), dark, PrimitiveType.Cube, pivot);
            _vm.Part("JawL", new Vector3(-0.022f, 0.29f, 0f), new Vector3(0.016f, 0.04f, 0.016f), dark, PrimitiveType.Cube, pivot);
            _vm.Part("JawR", new Vector3(0.022f, 0.29f, 0f), new Vector3(0.016f, 0.04f, 0.016f), dark, PrimitiveType.Cube, pivot);
            _vm.Muzzle = _vm.Marker("HeadPoint", new Vector3(0f, 0.28f, 0f), pivot);
        }

        public override void PrimaryAttack() => StartSwing(Light, 1f);
        public override void SecondaryAttack() => StartSwing(Heavy, 0.8f);

        void StartSwing(SwingKind kind, float pitch)
        {
            if (Owner == null || Time.time < _nextSwing) return;
            _kind = kind;
            _swinging = true;
            _hitDone = false;
            _swingStart = Time.time;
            _nextSwing = Time.time + kind.Cooldown;
            WeaponFx.Play("wrench_swing", 0.6f, pitch * Random.Range(0.95f, 1.05f));
        }

        public override void Think(float dt)
        {
            if (_vm == null || _vm.Root == null) BuildViewModel();

            if (_swinging)
            {
                float t = Time.time - _swingStart;
                if (!_hitDone && t >= _kind.WindUp)
                {
                    _hitDone = true;
                    DoHit();
                }
                if (t >= _kind.Duration) _swinging = false;
                Animate(t);
            }
            else
            {
                _vm.AnimEuler = Vector3.MoveTowards(_vm.AnimEuler, Vector3.zero, dt * 300f);
                _vm.AnimPosition = Vector3.MoveTowards(_vm.AnimPosition, Vector3.zero, dt * 1.5f);
            }
            _vm.Update(dt);
        }

        /// <summary>Wind up (raise and cock back), strike down-left across the screen, recover.</summary>
        void Animate(float t)
        {
            float windT = Mathf.Clamp01(t / _kind.WindUp);
            float strikeLen = Mathf.Max(0.05f, (_kind.Duration - _kind.WindUp) * 0.35f);
            float strikeT = Mathf.Clamp01((t - _kind.WindUp) / strikeLen);
            float recoverT = Mathf.Clamp01((t - _kind.WindUp - strikeLen) / Mathf.Max(0.05f, _kind.Duration - _kind.WindUp - strikeLen));

            // Wind-up pose -> strike pose -> rest.
            var wind = new Vector3(-_kind.Arc * 0.35f, -10f, -15f);
            var strike = new Vector3(_kind.Arc * 0.65f, 25f, 30f);
            Vector3 euler, pos;
            if (t < _kind.WindUp)
            {
                float e = Smooth(windT);
                euler = Vector3.Lerp(Vector3.zero, wind, e);
                pos = Vector3.Lerp(Vector3.zero, new Vector3(0.03f, 0.04f, -0.03f), e);
            }
            else if (strikeT < 1f)
            {
                float e = strikeT * strikeT;               // accelerate into the hit
                euler = Vector3.Lerp(wind, strike, e);
                pos = Vector3.Lerp(new Vector3(0.03f, 0.04f, -0.03f), new Vector3(-0.08f, -0.05f, 0.08f), e);
            }
            else
            {
                float e = Smooth(recoverT);
                euler = Vector3.Lerp(strike, Vector3.zero, e);
                pos = Vector3.Lerp(new Vector3(-0.08f, -0.05f, 0.08f), Vector3.zero, e);
            }
            _vm.AnimEuler = euler;
            _vm.AnimPosition = pos;
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        void DoHit()
        {
            var ray = Owner.AimRay;
            if (!WeaponTrace.Sphere(ray.origin, ray.direction, TraceRadius, Reach, Owner.transform, out var hit)) return;

            var ent = EntityManager.FromCollider(hit.collider);
            var balloon = ent != null ? ent.GetComponent<BalloonEntity>() : null;
            if (balloon != null) { balloon.Pop(); return; }

            var rb = hit.rigidbody;
            bool physical = rb != null && !rb.isKinematic;
            if (physical)
            {
                var dir = (ray.direction + Vector3.up * 0.15f).normalized;
                rb.AddForceAtPosition(dir * _kind.Impulse, hit.point, ForceMode.Impulse);
            }

            bool metal = ent != null;
            WeaponFx.PlayAt(metal ? "wrench_hit" : "wrench_hit_world", hit.point, 0.9f, Random.Range(0.92f, 1.08f));
            WeaponFx.FlashSprite(hit.point + hit.normal * 0.02f, new Color(1f, 0.9f, 0.6f, 0.9f), 0.18f, 0.08f);
            _vm?.Kick(0.3f);
            // A small view punch so the hit is felt.
            Owner.LookPitch = Mathf.Clamp(Owner.LookPitch + 0.6f, -89f, 89f);
        }
    }
}
