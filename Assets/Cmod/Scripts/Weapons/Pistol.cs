using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// A semi-automatic hitscan pistol. 18-round clip, 1.5 s reload (R, or automatically when you fire empty).
    /// Bullets push rigidbodies with an impulse, pop balloons and leave pooled bullet holes. Each shot
    /// flashes a muzzle light, draws a quick tracer, kicks the view up and kicks the view model back.
    /// </summary>
    public sealed class Pistol : WeaponBase
    {
        public override string Id => "weapon_pistol";
        public override string PrintName => "Pistol";
        public override string Category => "Weapons";
        public override int Slot => 2;
        public override int ClipSize => 18;

        const float FireDelay = 0.12f;
        const float ReloadTime = 1.5f;
        const float Range = 1000f;
        const float Impulse = 9f;            // N*s pushed into rigidbodies per bullet
        const float BaseSpread = 0.35f;      // degrees
        const float MaxSpread = 2.5f;
        static readonly Color FlashColor = new Color(1f, 0.78f, 0.4f, 1f);
        static readonly Color TracerColor = new Color(1f, 0.92f, 0.6f, 0.8f);

        ViewModel _vm;
        Transform _slide;
        float _nextFire;
        bool _reloading;
        float _reloadStart;
        float _spread = BaseSpread;
        float _slideBack;                    // 0..1 slide blowback

        public bool IsReloading => _reloading;

        public override void Initialize() => Clip = ClipSize;

        public override void Deploy()
        {
            BuildViewModel();
            _nextFire = Mathf.Max(_nextFire, Time.time + 0.25f);   // short draw time
        }

        public override void Holster()
        {
            _reloading = false;                // switching away cancels a reload, like most shooters
            _vm?.Destroy();
            _vm = null;
            _slide = null;
        }

        void BuildViewModel()
        {
            _vm?.Destroy();
            _vm = new ViewModel(Owner, "pistol");
            _vm.RestPosition = new Vector3(0.17f, -0.16f, 0.33f);
            var black = new Color(0.12f, 0.12f, 0.13f);
            var steel = new Color(0.3f, 0.31f, 0.33f);
            var grip = new Color(0.22f, 0.17f, 0.13f);

            _vm.Part("Frame", new Vector3(0f, -0.005f, 0.01f), new Vector3(0.032f, 0.03f, 0.17f), black);
            _slide = _vm.Part("Slide", new Vector3(0f, 0.022f, 0.012f), new Vector3(0.034f, 0.028f, 0.18f), steel);
            _vm.Part("Grip", new Vector3(0f, -0.065f, -0.045f), new Vector3(0.03f, 0.1f, 0.045f), grip, PrimitiveType.Cube, null, new Vector3(-14f, 0f, 0f));
            _vm.Part("Guard", new Vector3(0f, -0.04f, 0.015f), new Vector3(0.008f, 0.025f, 0.05f), black);
            _vm.Part("SightF", new Vector3(0f, 0.04f, 0.09f), new Vector3(0.006f, 0.008f, 0.006f), black);
            _vm.Part("SightR", new Vector3(0f, 0.04f, -0.07f), new Vector3(0.02f, 0.008f, 0.006f), black);
            _vm.Muzzle = _vm.Marker("Muzzle", new Vector3(0f, 0.02f, 0.11f));
        }

        // =====================================================================================
        // Firing
        // =====================================================================================

        public override void PrimaryAttack()
        {
            if (Owner == null || _reloading || Time.time < _nextFire) return;
            if (Clip <= 0)
            {
                WeaponFx.Play("pistol_dry", 0.7f);
                _nextFire = Time.time + 0.25f;
                Reload();
                return;
            }

            Clip--;
            _nextFire = Time.time + FireDelay;
            Shoot();
            if (Clip == 0) _nextFire = Time.time + 0.3f;
        }

        void Shoot()
        {
            var ray = Owner.AimRay;
            // Random spread inside a cone; it grows with rapid fire and recovers over time.
            var spreadRot = Quaternion.AngleAxis(Random.Range(0f, 360f), ray.direction) *
                            Quaternion.AngleAxis(Random.Range(0f, _spread), Vector3.Cross(ray.direction, Vector3.up).sqrMagnitude > 1e-4f
                                ? Vector3.Cross(ray.direction, Vector3.up).normalized : Vector3.right);
            var dir = spreadRot * ray.direction;
            _spread = Mathf.Min(MaxSpread, _spread + 0.45f);

            var muzzle = _vm != null ? _vm.MuzzlePosition : ray.origin;
            Vector3 end;
            if (WeaponTrace.Ray(ray.origin, dir, Range, Owner.transform, out var hit))
            {
                end = hit.point;
                OnBulletHit(hit, dir);
            }
            else
            {
                end = ray.origin + dir * 200f;
            }

            // Effects
            WeaponFx.Tracer(muzzle + dir * 0.05f, end, TracerColor, 0.012f, 0.05f);
            WeaponFx.FlashLight(muzzle, FlashColor, 3.5f, 7f, 0.07f);
            WeaponFx.FlashSprite(muzzle, FlashColor, 0.16f, 0.05f);
            WeaponFx.Play("pistol_shot", 0.8f, Random.Range(0.94f, 1.06f));
            _vm?.Kick(0.6f);
            _slideBack = 1f;

            // Recoil: kick the view up and a little sideways.
            Owner.LookPitch = Mathf.Clamp(Owner.LookPitch - Random.Range(0.9f, 1.4f), -89f, 89f);
            Owner.LookYaw = Mathf.Repeat(Owner.LookYaw + Random.Range(-0.35f, 0.35f), 360f);
        }

        static void OnBulletHit(RaycastHit hit, Vector3 dir)
        {
            var ent = EntityManager.FromCollider(hit.collider);
            var balloon = ent != null ? ent.GetComponent<BalloonEntity>() : null;
            if (balloon != null) { balloon.Pop(); return; }

            var rb = hit.rigidbody;
            if (rb != null && !rb.isKinematic) rb.AddForceAtPosition(dir * Impulse, hit.point, ForceMode.Impulse);

            WeaponFx.BulletHole(hit);
            WeaponFx.FlashSprite(hit.point + hit.normal * 0.02f, new Color(1f, 0.85f, 0.55f, 0.9f), 0.12f, 0.06f);
        }

        // =====================================================================================
        // Reloading and animation
        // =====================================================================================

        public override void Reload()
        {
            if (_reloading || Clip >= ClipSize) return;
            _reloading = true;
            _reloadStart = Time.time;
            WeaponFx.Play("pistol_reload", 0.7f);
        }

        public override void Think(float dt)
        {
            if (_vm == null || _vm.Root == null) BuildViewModel();

            if (_reloading && Time.time - _reloadStart >= ReloadTime)
            {
                _reloading = false;
                Clip = ClipSize;
            }

            _spread = Mathf.MoveTowards(_spread, BaseSpread, dt * 3f);
            _slideBack = Mathf.MoveTowards(_slideBack, 0f, dt * 14f);
            if (_slide != null) _slide.localPosition = new Vector3(0f, 0.022f, 0.012f - _slideBack * 0.025f);

            // Reload: dip and tilt the gun, hold it while the magazine goes in, bring it back.
            if (_reloading)
            {
                float p = Mathf.Clamp01((Time.time - _reloadStart) / ReloadTime);
                float k = Mathf.Sin(p * Mathf.PI);
                k = Mathf.Clamp01(k * 1.6f);
                _vm.AnimPosition = new Vector3(-0.02f * k, -0.07f * k, -0.02f * k);
                _vm.AnimEuler = new Vector3(18f * k, 0f, 28f * k);
            }
            else
            {
                _vm.AnimPosition = Vector3.MoveTowards(_vm.AnimPosition, Vector3.zero, dt);
                _vm.AnimEuler = Vector3.MoveTowards(_vm.AnimEuler, Vector3.zero, dt * 120f);
            }
            _vm.Update(dt);
        }

        public override void DrawHUD()
        {
            if (_reloading) WeaponHud.DrawHint("Reloading...");
        }
    }
}
