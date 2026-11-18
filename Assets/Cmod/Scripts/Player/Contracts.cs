using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// The local player. Contract only: the implementation is in Player/CmodPlayer.cs (the other half
    /// of this partial class). Spawned by CmodGame at the spawn point.
    /// </summary>
    public partial class CmodPlayer : MonoBehaviour
    {
        public static CmodPlayer Local { get; internal set; }

        public string PlayerName = "player";
        public Camera Cam;              // first-person camera (child of the player)
        public float Health = 100f;
        public float MaxHealth = 100f;

        public bool Noclip;             // V toggles; fly through walls, no gravity

        /// <summary>Weapons the player carries, in pickup order.</summary>
        public readonly List<WeaponBase> Weapons = new List<WeaponBase>();
        public WeaponBase ActiveWeapon { get; protected set; }

        /// <summary>Camera position in world space.</summary>
        public Vector3 EyePosition => Cam != null ? Cam.transform.position : transform.position + Vector3.up * 1.6f;
        /// <summary>Ray from the camera through the crosshair.</summary>
        public Ray AimRay => Cam != null ? new Ray(Cam.transform.position, Cam.transform.forward) : new Ray(EyePosition, transform.forward);

        /// <summary>Raycast from the eye, ignoring the player's own collider. Default max distance is 32768.</summary>
        public partial bool TraceAim(out RaycastHit hit, float maxDistance = 32768f);

        /// <summary>Gives the weapon if the player doesn't have it yet (calls Initialize). Returns the instance.</summary>
        public partial WeaponBase Give(string weaponId);
        public partial void SelectWeapon(WeaponBase weapon);
        public partial void SelectWeapon(string weaponId);
        public partial void StripWeapons();

        public partial void TakeDamage(float amount, Vector3 fromDirection);
        /// <summary>Moves the player to a position, resetting velocity.</summary>
        public partial void Teleport(Vector3 position, float yawDegrees);
        /// <summary>Respawns at the spawn point with full health and default weapons.</summary>
        public partial void Respawn();

        /// <summary>Mouse sensitivity and field of view come from convars "sensitivity" and "fov".</summary>
        public float LookPitch;
        public float LookYaw;
        public Vector3 Velocity;
    }
}
