using System;
using System.Collections.Generic;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Implementation half of the local player (the contract half is Player/Contracts.cs).
    ///
    /// Layout of the "Player" GameObject (built by <see cref="Build"/>):
    ///   Player            CharacterController (pivot at the feet), CmodPlayer, Hud, AudioSource
    ///   └─ Camera         Camera + AudioListener, local Y = eye height, pitch only
    ///      └─ Flashlight  Spot light, toggled with F
    ///
    /// The root rotates with yaw only; the camera child carries the pitch. The player lives on
    /// the built-in "Ignore Raycast" layer so other systems' raw Physics.Raycast calls from the
    /// eye never hit the player's own capsule.
    /// </summary>
    public partial class CmodPlayer
    {
        // ---- Tunables (metres, seconds) -----------------------------------------------------
        public const float StandHeight = 1.8f;
        public const float CrouchHeight = 1.1f;
        public const float Radius = 0.35f;
        public const float StandEyeHeight = 1.62f;
        public const float CrouchEyeHeight = 0.9f;
        public const float DeadEyeHeight = 0.3f;

        public const float WalkSpeed = 5f;
        public const float SprintSpeed = 9f;
        public const float CrouchSpeed = 2.5f;
        public const float JumpSpeed = 5.5f;

        const float GroundAccel = 55f;       // m/s² towards the wished velocity on the ground
        const float GroundFriction = 40f;    // m/s² deceleration on the ground without input
        const float AirAccel = 14f;          // m/s² of air control
        const float StickVelocity = 2f;      // small downward speed that keeps the capsule grounded
        const float CoyoteTime = 0.12f;      // jumping is still allowed this long after leaving a ledge
        const float JumpBuffer = 0.15f;      // a Space press this long before landing still jumps
        const float FallDamageSpeed = 14f;   // landing faster than this hurts
        const float FallDamagePerMs = 9f;    // damage per m/s above the threshold
        const float RespawnDelay = 2f;
        const float NoclipSpeed = 10f;
        const float NoclipFastSpeed = 35f;
        const float PlayerMassKg = 80f;      // used when pushing / standing on rigidbodies
        const int PlayerLayer = 2;           // built-in "Ignore Raycast"

        // ---- Runtime state -----------------------------------------------------------------
        CharacterController _cc;
        AudioSource _audio;
        Light _flashlight;
        AudioClip _clickClip;

        Pose _spawnPose;
        bool _crouched;
        bool _grounded;
        float _lastGroundedTime = -10f;
        float _jumpPressedTime = -10f;
        float _eyeHeight = StandEyeHeight;
        float _landDip;                 // camera dip (metres) after a landing, springs back to 0
        float _viewPunchPitch;          // degrees, decays to 0 (damage / landing kick)
        float _viewPunchRoll;
        bool _wasNoclip;
        float _sprintBlend;             // 0..1, eases the sprint FOV kick

        /// <summary>True between death and respawn.</summary>
        public bool IsDead { get; private set; }
        /// <summary>Unscaled time when the player died (for the HUD countdown).</summary>
        public float DeathTime { get; private set; }
        /// <summary>Unscaled time of the last damage (the HUD flashes red).</summary>
        public float LastDamageTime { get; private set; } = -10f;
        /// <summary>Unscaled time of the last weapon switch (the HUD's weapon popup).</summary>
        public float LastWeaponSwitchTime { get; private set; } = -10f;
        public bool IsCrouching => _crouched;
        public bool IsGrounded => _grounded;
        public bool FlashlightOn => _flashlight != null && _flashlight.enabled;
        public float RespawnDelaySeconds => RespawnDelay;

        /// <summary>The weapons the player starts with (see BOARD.md, Weapon IDs).</summary>
        public static readonly string[] DefaultLoadout = { "weapon_physgun", "weapon_toolgun", "weapon_pistol", "weapon_wrench" };

        static readonly RaycastHit[] s_hitBuffer = new RaycastHit[64];
        static readonly Collider[] s_overlapBuffer = new Collider[32];
        readonly List<WeaponBase> _sortedWeapons = new List<WeaponBase>();

        // =====================================================================================
        // Construction
        // =====================================================================================

        /// <summary>Creates the components and child objects. Called once by Player_Spawn.</summary>
        internal void Build(Pose spawn)
        {
            _spawnPose = spawn;
            gameObject.layer = PlayerLayer;

            _cc = gameObject.GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();
            _cc.radius = Radius;
            _cc.height = StandHeight;
            _cc.center = new Vector3(0f, StandHeight * 0.5f, 0f);
            _cc.stepOffset = 0.4f;
            _cc.slopeLimit = 46f;
            _cc.skinWidth = 0.04f;
            _cc.minMoveDistance = 0f;

            // Camera
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, StandEyeHeight, 0f);
            Cam = camGo.AddComponent<Camera>();
            Cam.nearClipPlane = 0.03f;
            Cam.farClipPlane = 2000f;
            Cam.fieldOfView = FovConVar();
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();

            // Flashlight
            var flGo = new GameObject("Flashlight");
            flGo.transform.SetParent(camGo.transform, false);
            flGo.transform.localPosition = new Vector3(0.15f, -0.12f, 0.1f);
            _flashlight = flGo.AddComponent<Light>();
            _flashlight.type = LightType.Spot;
            _flashlight.range = 35f;
            _flashlight.spotAngle = 55f;
            _flashlight.innerSpotAngle = 25f;
            _flashlight.intensity = 2.2f;
            _flashlight.color = new Color(1f, 0.96f, 0.86f);
            _flashlight.shadows = LightShadows.Soft;
            _flashlight.enabled = false;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _clickClip = MakeClickClip();

            Teleport(spawn.position, spawn.rotation.eulerAngles.y);
        }

        /// <summary>Gives every default weapon (missing ones are skipped with a warning).</summary>
        internal void GiveDefaultLoadout()
        {
            foreach (var id in DefaultLoadout) Give(id);
            var physgun = FindWeapon("weapon_physgun");
            SelectWeapon(physgun ?? (Weapons.Count > 0 ? Weapons[0] : null));
        }

        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        // =====================================================================================
        // Contract: tracing
        // =====================================================================================

        public partial bool TraceAim(out RaycastHit hit, float maxDistance)
        {
            var ray = AimRay;
            int n = Physics.RaycastNonAlloc(ray, s_hitBuffer, maxDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = s_hitBuffer[i].collider;
                if (c == null || IsOwnCollider(c)) continue;
                if (s_hitBuffer[i].distance < bestDist) { bestDist = s_hitBuffer[i].distance; best = i; }
            }
            if (best < 0) { hit = default; return false; }
            hit = s_hitBuffer[best];
            return true;
        }

        /// <summary>True for the player's capsule and anything parented under the player (view models).</summary>
        bool IsOwnCollider(Collider c) => c == _cc || c.transform == transform || c.transform.IsChildOf(transform);

        // =====================================================================================
        // Contract: weapons
        // =====================================================================================

        public partial WeaponBase Give(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            var existing = FindWeapon(weaponId);
            if (existing != null) return existing;

            var w = WeaponRegistry.Create(weaponId);
            if (w == null) { CmodLog.Warn($"Give: unknown weapon '{weaponId}'"); return null; }
            w.Owner = this;
            if (w.ClipSize > 0) w.Clip = w.ClipSize;   // start with a full clip; Initialize may override
            Weapons.Add(w);
            SafeCall(w, "Initialize", () => w.Initialize());
            if (ActiveWeapon == null) SelectWeapon(w);
            return w;
        }

        public partial void SelectWeapon(WeaponBase weapon)
        {
            if (weapon != null && !Weapons.Contains(weapon)) { CmodLog.Warn($"SelectWeapon: player doesn't carry {weapon.Id}"); return; }
            if (weapon == ActiveWeapon) { LastWeaponSwitchTime = Time.unscaledTime; return; }

            var old = ActiveWeapon;
            if (old != null) SafeCall(old, "Holster", () => old.Holster());
            ActiveWeapon = weapon;
            if (weapon != null) SafeCall(weapon, "Deploy", () => weapon.Deploy());
            LastWeaponSwitchTime = Time.unscaledTime;
            Hooks.Run(Hooks.WeaponSwitched, this, weapon);
        }

        public partial void SelectWeapon(string weaponId)
        {
            var w = FindWeapon(weaponId);
            if (w == null) { CmodLog.Warn($"SelectWeapon: player doesn't carry '{weaponId}'"); return; }
            SelectWeapon(w);
        }

        public partial void StripWeapons()
        {
            var old = ActiveWeapon;
            if (old != null)
            {
                SafeCall(old, "Holster", () => old.Holster());
                ActiveWeapon = null;
                Hooks.Run(Hooks.WeaponSwitched, this, null);
            }
            Weapons.Clear();
        }

        public WeaponBase FindWeapon(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            foreach (var w in Weapons)
                if (string.Equals(w.Id, weaponId, StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        /// <summary>Weapons ordered by slot, then pickup order (the order the HUD list and scroll wheel use).</summary>
        public IReadOnlyList<WeaponBase> SortedWeapons()
        {
            _sortedWeapons.Clear();
            _sortedWeapons.AddRange(Weapons);
            // Stable insertion sort by slot (List.Sort is not stable; the list is tiny).
            for (int i = 1; i < _sortedWeapons.Count; i++)
            {
                var w = _sortedWeapons[i];
                int j = i - 1;
                while (j >= 0 && _sortedWeapons[j].Slot > w.Slot) { _sortedWeapons[j + 1] = _sortedWeapons[j]; j--; }
                _sortedWeapons[j + 1] = w;
            }
            return _sortedWeapons;
        }

        // =====================================================================================
        // Contract: health, teleport, respawn
        // =====================================================================================

        public partial void TakeDamage(float amount, Vector3 fromDirection)
        {
            if (IsDead || amount <= 0f) return;
            if (ConCommands.GetVar("god")?.Bool ?? false) { LastDamageTime = Time.unscaledTime; return; }

            Health = Mathf.Max(0f, Health - amount);
            LastDamageTime = Time.unscaledTime;

            // A small view kick away from where the damage came from.
            float kick = Mathf.Clamp(amount * 0.25f, 1f, 8f);
            _viewPunchPitch -= kick;
            if (fromDirection.sqrMagnitude > 0.0001f)
            {
                float side = Vector3.Dot(fromDirection.normalized, transform.right);
                _viewPunchRoll += side * kick;
            }

            if (Health <= 0f) Die();
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            Health = 0f;
            DeathTime = Time.unscaledTime;
            if (Noclip) Noclip = false;
            if (_flashlight != null) _flashlight.enabled = false;
            StripWeapons();   // holsters first, so the physgun lets go of whatever it held
            CmodLog.Info($"{PlayerName} died.");
        }

        public partial void Teleport(Vector3 position, float yawDegrees)
        {
            bool wasEnabled = _cc != null && _cc.enabled;
            if (_cc != null) _cc.enabled = false;   // a CharacterController overrides direct transform writes
            transform.position = position;
            LookYaw = yawDegrees;
            transform.rotation = Quaternion.Euler(0f, LookYaw, 0f);
            Velocity = Vector3.zero;
            _lastGroundedTime = -10f;
            _grounded = false;
            if (_cc != null) _cc.enabled = wasEnabled && !Noclip;
            Physics.SyncTransforms();
        }

        public partial void Respawn()
        {
            IsDead = false;
            Health = MaxHealth;
            Noclip = false;
            _wasNoclip = false;
            _viewPunchPitch = _viewPunchRoll = 0f;
            _landDip = 0f;
            LastDamageTime = -10f;

            if (_crouched) SetCrouched(false, force: true);
            if (_cc != null) _cc.enabled = true;

            // Prefer the game's current spawn point (a map change may move it); fall back to where we first spawned.
            var spawn = CmodGame.Instance != null ? CmodGame.Instance.SpawnPoint : _spawnPose;
            Teleport(spawn.position, spawn.rotation.eulerAngles.y);
            LookPitch = 0f;
            _eyeHeight = StandEyeHeight;

            StripWeapons();
            GiveDefaultLoadout();
            Hooks.Run(Hooks.PlayerSpawned, this);
        }

        // =====================================================================================
        // Frame loop
        // =====================================================================================

        void Update()
        {
            try { Tick(Time.deltaTime); }
            catch (Exception ex) { LogThrottled("player.update", $"[player] {ex.GetType().Name}: {ex.Message}"); }
        }

        void FixedUpdate()
        {
            var w = ActiveWeapon;
            if (w != null && !IsDead) SafeCall(w, "FixedThink", () => w.FixedThink(Time.fixedDeltaTime));
        }

        void Tick(float dt)
        {
            if (IsDead)
            {
                UpdateDeadCamera(dt);
                if (Time.unscaledTime - DeathTime >= RespawnDelay) Respawn();
                return;
            }

            HandleToggles();
            HandleLook();
            if (Noclip) MoveNoclip(dt); else MoveWalking(dt);
            UpdateCamera(dt);
            HandleWeaponSelection();
            DriveWeapon(dt);
        }

        void HandleToggles()
        {
            if (CmodInput.KeyDown(KeyCode.V)) Noclip = !Noclip;
            if (CmodInput.KeyDown(KeyCode.F) && _flashlight != null)
            {
                _flashlight.enabled = !_flashlight.enabled;
                PlayClick(_flashlight.enabled ? 1.0f : 0.8f);
            }

            // Noclip can also be toggled from outside (the "noclip" console command), so react to changes here.
            if (Noclip != _wasNoclip)
            {
                _wasNoclip = Noclip;
                if (Noclip)
                {
                    _cc.enabled = false;
                    if (_crouched) SetCrouched(false, force: true);
                }
                else
                {
                    _cc.enabled = true;
                    Velocity = Vector3.zero;
                    _lastGroundedTime = -10f;
                }
            }
        }

        void HandleLook()
        {
            var w = ActiveWeapon;
            bool blocked = false;
            if (w != null)
            {
                try { blocked = w.BlocksMouseLook; }
                catch (Exception ex) { LogThrottled(w.Id + ".BlocksMouseLook", $"[{w.Id}] BlocksMouseLook: {ex.Message}"); }
            }
            if (blocked) return;

            var d = CmodInput.LookDelta;
            LookYaw = Mathf.Repeat(LookYaw + d.x, 360f);
            LookPitch = Mathf.Clamp(LookPitch - d.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(0f, LookYaw, 0f);
        }

        // ---- Walking --------------------------------------------------------------------------

        void MoveWalking(float dt)
        {
            if (dt <= 0f) return;
            if (!_cc.enabled) _cc.enabled = true;

            // Crouch (Ctrl). Standing up needs headroom.
            bool wantCrouch = CmodInput.Key(KeyCode.LeftControl) || CmodInput.Key(KeyCode.RightControl);
            if (wantCrouch != _crouched) SetCrouched(wantCrouch, force: false);

            // Wish direction from WASD, in the yaw plane.
            var input = CmodInput.Move;
            var wishDir = transform.right * input.x + transform.forward * input.y;
            wishDir.y = 0f;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();
            bool sprint = !_crouched && input.y > 0f && (CmodInput.Key(KeyCode.LeftShift) || CmodInput.Key(KeyCode.RightShift));
            float speed = _crouched ? CrouchSpeed : sprint ? SprintSpeed : WalkSpeed;
            var wishVel = wishDir * speed;
            _sprintBlend = Mathf.MoveTowards(_sprintBlend, sprint && wishDir.sqrMagnitude > 0.01f ? 1f : 0f, dt * 4f);

            var v = Velocity;
            var h = new Vector3(v.x, 0f, v.z);

            if (_grounded)
            {
                // Snappy ground movement: accelerate towards the wish velocity, brake without input.
                float rate = wishDir.sqrMagnitude > 0.0001f ? GroundAccel : GroundFriction;
                h = Vector3.MoveTowards(h, wishVel, rate * dt);
            }
            else if (wishDir.sqrMagnitude > 0.0001f)
            {
                // Air control: steer freely, but never brake below the current momentum
                // (a sprint-jump keeps its speed while you turn).
                float cap = Mathf.Max(h.magnitude, speed);
                h += wishDir * (AirAccel * dt);
                if (h.magnitude > cap) h = h.normalized * cap;
            }

            // Jump with buffering and coyote time.
            if (CmodInput.KeyDown(KeyCode.Space)) _jumpPressedTime = Time.time;
            bool jumped = false;
            float vy = v.y;
            if (Time.time - _jumpPressedTime <= JumpBuffer && Time.time - _lastGroundedTime <= CoyoteTime)
            {
                vy = JumpSpeed;
                jumped = true;
                _jumpPressedTime = -10f;
                _lastGroundedTime = -10f;
                _grounded = false;
            }

            var g = Physics.gravity;
            if (!_grounded || jumped) vy += g.y * dt;
            else vy = -StickVelocity;
            // Horizontal gravity components (rare, but honour sv_gravity tricks).
            h += new Vector3(g.x, 0f, g.z) * dt;

            float fallSpeed = -vy;   // remembered for fall damage
            bool wasGrounded = _grounded;

            var flags = _cc.Move(new Vector3(h.x, vy, h.z) * dt);

            // Walking down stairs and slopes: snap back to the ground instead of floating off each step.
            if (wasGrounded && !jumped && !_cc.isGrounded) SnapToGround();

            _grounded = _cc.isGrounded;
            if ((flags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;
            if ((flags & CollisionFlags.Sides) != 0)
            {
                // Lose the velocity that went into the wall, so we slide instead of sticking.
                var actual = _cc.velocity;
                h = new Vector3(actual.x, 0f, actual.z);
            }

            if (_grounded)
            {
                if (!wasGrounded) OnLanded(fallSpeed);
                _lastGroundedTime = Time.time;
                if (vy < 0f) vy = -StickVelocity;
            }

            Velocity = new Vector3(h.x, vy, h.z);
        }

        void SnapToGround()
        {
            float maxDrop = _cc.stepOffset + 0.1f;
            var origin = transform.position + Vector3.up * (Radius + 0.05f);
            if (Physics.SphereCast(origin, Radius * 0.95f, Vector3.down, out var hit, maxDrop + 0.05f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                Vector3.Angle(hit.normal, Vector3.up) <= _cc.slopeLimit)
            {
                _cc.Move(Vector3.down * (maxDrop + 0.05f));
            }
        }

        void OnLanded(float impactSpeed)
        {
            // Camera dip proportional to the impact.
            _landDip = Mathf.Clamp(impactSpeed * 0.012f, 0f, 0.25f);
            if (impactSpeed > FallDamageSpeed)
            {
                float dmg = (impactSpeed - FallDamageSpeed) * FallDamagePerMs;
                _viewPunchPitch += Mathf.Clamp(dmg * 0.2f, 2f, 10f);
                TakeDamage(dmg, Vector3.down);
                PlayClick(0.5f);
            }
        }

        /// <summary>Resizes the capsule. In the air, crouching pulls the legs up (the head stays put).</summary>
        void SetCrouched(bool crouch, bool force)
        {
            float delta = StandHeight - CrouchHeight;
            if (crouch)
            {
                ApplyHeight(CrouchHeight);
                _crouched = true;
                if (!_grounded && _cc.enabled && !Noclip)
                {
                    // Crouch-jump: lift the feet so the head doesn't drop.
                    var before = transform.position;
                    _cc.Move(Vector3.up * delta);
                    _eyeHeight -= transform.position.y - before.y;
                }
                return;
            }

            if (force) { ApplyHeight(StandHeight); _crouched = false; return; }

            // Standing up: on the ground the head grows upwards; in the air prefer pushing the feet down.
            var pos = transform.position;
            if (!_grounded && CapsuleFree(pos - Vector3.up * delta, StandHeight))
            {
                ApplyHeight(StandHeight);
                _crouched = false;
                MoveTransform(pos - Vector3.up * delta);
                _eyeHeight += delta;
            }
            else if (CapsuleFree(pos, StandHeight))
            {
                ApplyHeight(StandHeight);
                _crouched = false;
            }
            // Otherwise stay crouched (something is above us).
        }

        void ApplyHeight(float height)
        {
            _cc.height = height;
            _cc.center = new Vector3(0f, height * 0.5f, 0f);
        }

        bool CapsuleFree(Vector3 feet, float height)
        {
            float r = Radius - 0.02f;
            var bottom = feet + Vector3.up * (r + 0.05f);
            var top = feet + Vector3.up * (height - r - 0.02f);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, s_overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (s_overlapBuffer[i] != null && !IsOwnCollider(s_overlapBuffer[i])) return false;
            return true;
        }

        void MoveTransform(Vector3 pos)
        {
            bool en = _cc.enabled;
            _cc.enabled = false;
            transform.position = pos;
            _cc.enabled = en;
        }

        // ---- Noclip ---------------------------------------------------------------------------

        void MoveNoclip(float dt)
        {
            var input = CmodInput.Move;
            var camT = Cam != null ? Cam.transform : transform;
            var dir = camT.forward * input.y + camT.right * input.x;
            if (CmodInput.Key(KeyCode.Space)) dir += Vector3.up;
            if (CmodInput.Key(KeyCode.LeftControl) || CmodInput.Key(KeyCode.RightControl)) dir -= Vector3.up;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            bool fast = CmodInput.Key(KeyCode.LeftShift) || CmodInput.Key(KeyCode.RightShift);
            var target = dir * (fast ? NoclipFastSpeed : NoclipSpeed);
            // A little smoothing so starting and stopping isn't jarring.
            Velocity = Vector3.MoveTowards(Velocity, target, (fast ? 200f : 80f) * dt);
            transform.position += Velocity * dt;
            _grounded = false;
            _sprintBlend = Mathf.MoveTowards(_sprintBlend, 0f, dt * 4f);
        }

        // ---- Camera ---------------------------------------------------------------------------

        void UpdateCamera(float dt)
        {
            if (Cam == null) return;
            float targetEye = _crouched ? CrouchEyeHeight : StandEyeHeight;
            _eyeHeight = Mathf.MoveTowards(_eyeHeight, targetEye, dt * 4.5f);
            _landDip = Mathf.MoveTowards(_landDip, 0f, dt * 1.2f);
            float decay = 1f - Mathf.Exp(-dt * 10f);
            _viewPunchPitch = Mathf.Lerp(_viewPunchPitch, 0f, decay);
            _viewPunchRoll = Mathf.Lerp(_viewPunchRoll, 0f, decay);

            var ct = Cam.transform;
            ct.localPosition = new Vector3(0f, _eyeHeight - _landDip, 0f);
            ct.localRotation = Quaternion.Euler(Mathf.Clamp(LookPitch + _viewPunchPitch, -89.9f, 89.9f), 0f, _viewPunchRoll);
            Cam.fieldOfView = FovConVar() + _sprintBlend * 4f;
        }

        void UpdateDeadCamera(float dt)
        {
            if (Cam == null) return;
            _eyeHeight = Mathf.MoveTowards(_eyeHeight, DeadEyeHeight, dt * 3f);
            _viewPunchRoll = Mathf.MoveTowards(_viewPunchRoll, 35f, dt * 60f);
            var ct = Cam.transform;
            ct.localPosition = new Vector3(0f, _eyeHeight, 0f);
            ct.localRotation = Quaternion.Euler(LookPitch, 0f, _viewPunchRoll);

            // Keep falling while dead so you don't hang in the air.
            if (_cc != null && _cc.enabled)
            {
                var v = Velocity;
                v.x = Mathf.MoveTowards(v.x, 0f, GroundFriction * dt);
                v.z = Mathf.MoveTowards(v.z, 0f, GroundFriction * dt);
                v.y = _cc.isGrounded ? -StickVelocity : v.y + Physics.gravity.y * dt;
                _cc.Move(v * dt);
                Velocity = v;
            }
        }

        static float FovConVar() => Mathf.Clamp(ConCommands.GetVar("fov")?.Float ?? 80f, 60f, 120f);

        // ---- Weapon input ---------------------------------------------------------------------

        void HandleWeaponSelection()
        {
            // Number keys 1..6 pick by slot; pressing again cycles weapons sharing that slot.
            for (int slot = 0; slot < 6; slot++)
            {
                if (!CmodInput.KeyDown(KeyCode.Alpha1 + slot)) continue;
                SelectSlot(slot);
                return;
            }

            float scroll = CmodInput.Scroll;
            if (Mathf.Abs(scroll) < 0.01f) return;
            var w = ActiveWeapon;
            bool weaponUsesWheel = false;
            if (w != null)
            {
                try { weaponUsesWheel = w.UsesScrollWheel; }
                catch (Exception ex) { LogThrottled(w.Id + ".UsesScrollWheel", $"[{w.Id}] UsesScrollWheel: {ex.Message}"); }
            }
            if (weaponUsesWheel) return;
            CycleWeapon(scroll > 0f ? -1 : 1);   // wheel up = previous, like GMod
        }

        void SelectSlot(int slot)
        {
            var list = SortedWeapons();
            int first = -1, activeIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Slot != slot) continue;
                if (first < 0) first = i;
                if (list[i] == ActiveWeapon) activeIdx = i;
            }
            if (first < 0) return;
            int pick = first;
            if (activeIdx >= 0 && activeIdx + 1 < list.Count && list[activeIdx + 1].Slot == slot) pick = activeIdx + 1;
            SelectWeapon(list[pick]);
        }

        /// <summary>Selects the next (+1) or previous (-1) weapon in slot order, wrapping around.</summary>
        public void CycleWeapon(int dir)
        {
            var list = SortedWeapons();
            if (list.Count == 0) return;
            int idx = ActiveWeapon != null ? list.IndexOf(ActiveWeapon) : -1;
            idx = idx < 0 ? (dir > 0 ? 0 : list.Count - 1) : (idx + dir + list.Count) % list.Count;
            SelectWeapon(list[idx]);
        }

        void DriveWeapon(float dt)
        {
            var w = ActiveWeapon;
            if (w == null) return;
            SafeCall(w, "Think", () => w.Think(dt));
            if (w != ActiveWeapon) return;   // Think may have switched weapons

            if (CmodInput.MouseDown(0)) SafeCall(w, "PrimaryAttack", () => w.PrimaryAttack());
            bool held = CmodInput.Mouse(0);
            SafeCall(w, "PrimaryHeld", () => w.PrimaryHeld(held));
            if (CmodInput.MouseDown(1)) SafeCall(w, "SecondaryAttack", () => w.SecondaryAttack());
            if (CmodInput.KeyDown(KeyCode.R)) SafeCall(w, "Reload", () => w.Reload());
        }

        // ---- Physics interaction --------------------------------------------------------------

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var rb = hit.collider != null ? hit.collider.attachedRigidbody : null;
            if (rb == null || rb.isKinematic) return;

            if (hit.moveDirection.y < -0.5f)
            {
                // Standing on it: press down with the player's weight (per frame impulse = m*g*dt).
                float dt = Time.deltaTime;
                rb.AddForceAtPosition(Physics.gravity * (PlayerMassKg * dt), hit.point, ForceMode.Impulse);
                return;
            }

            // Walking into it: push it horizontally towards our own speed. Heavy things resist.
            var pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (pushDir.sqrMagnitude < 0.0001f) return;
            pushDir.Normalize();
            float ourSpeed = new Vector3(Velocity.x, 0f, Velocity.z).magnitude;
            float theirSpeed = Vector3.Dot(PhysicsCompat.GetVelocity(rb), pushDir);
            float deficit = ourSpeed - theirSpeed;
            if (deficit <= 0f) return;
            float massFactor = Mathf.Clamp01(PlayerMassKg / Mathf.Max(1f, rb.mass)) * 0.5f;
            rb.AddForceAtPosition(pushDir * (deficit * massFactor * rb.mass), hit.point, ForceMode.Impulse);
        }

        // ---- Sound ----------------------------------------------------------------------------

        void PlayClick(float pitch)
        {
            if (_audio == null || _clickClip == null) return;
            _audio.pitch = pitch;
            _audio.PlayOneShot(_clickClip, 0.5f);
        }

        /// <summary>A short procedural switch click (no audio assets needed).</summary>
        static AudioClip MakeClickClip()
        {
            const int rate = 44100;
            int len = rate / 25;
            var data = new float[len];
            var rng = new System.Random(98);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 180f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = (noise * 0.6f + Mathf.Sin(t * 2f * Mathf.PI * 2200f) * 0.4f) * env;
            }
            var clip = AudioClip.Create("cmod_click", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---- Error isolation ------------------------------------------------------------------

        static readonly Dictionary<string, float> s_lastLog = new Dictionary<string, float>();

        /// <summary>Runs weapon code so an exception never escapes the player loop (logged at most every 5s per call site).</summary>
        static void SafeCall(WeaponBase w, string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { LogThrottled(w.Id + "." + what, $"[{w.Id}.{what}] {ex.GetType().Name}: {ex.Message}"); }
        }

        static void LogThrottled(string key, string message)
        {
            float now = Time.unscaledTime;
            if (s_lastLog.TryGetValue(key, out var t) && now - t < 5f) return;
            s_lastLog[key] = now;
            CmodLog.Error(message);
        }
    }
}
