using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// C-PLAYER's boot step: registers the player convars and spawns the local player with its HUD.
    /// </summary>
    public static partial class CmodModules
    {
        public static partial void Player_Spawn(Pose spawn)
        {
            RegisterPlayerConVars();

            // Only one camera and one AudioListener may be active: switch off whatever the scene had.
            foreach (var cam in PhysicsCompat.FindAll<Camera>())
                if (cam != null) cam.enabled = false;
            foreach (var listener in PhysicsCompat.FindAll<AudioListener>())
                if (listener != null) listener.enabled = false;

            // Replace an earlier player if Player_Spawn runs twice (domain reload quirks, tests).
            if (CmodPlayer.Local != null) Object.Destroy(CmodPlayer.Local.gameObject);

            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, spawn.rotation.eulerAngles.y, 0f));
            go.AddComponent<CharacterController>();
            var player = go.AddComponent<CmodPlayer>();
            var nameVar = ConCommands.GetVar("name");
            if (nameVar != null && !string.IsNullOrEmpty(nameVar.Value)) player.PlayerName = nameVar.Value;

            player.Build(spawn);
            CmodPlayer.Local = player;
            go.AddComponent<Hud>();

            player.GiveDefaultLoadout();
            Hooks.Run(Hooks.PlayerSpawned, player);
            CmodLog.Info($"Player '{player.PlayerName}' spawned at {spawn.position}.");
        }

        static void RegisterPlayerConVars()
        {
            ConCommands.CreateVar("sensitivity", "2", "Mouse sensitivity.", 0.05f, 20f, archive: true);
            ConCommands.CreateVar("m_invert", "0", "Invert the mouse Y axis (0/1).", 0f, 1f, archive: true);
            var fov = ConCommands.CreateVar("fov", "80", "Field of view in degrees (60-120).", 60f, 120f, archive: true);
            ConCommands.CreateVar("cl_drawhud", "1", "Draw the HUD (0/1).", 0f, 1f);

            // Keep the player's name in sync with the "name" convar if another module made one.
            var nameVar = ConCommands.GetVar("name");
            if (nameVar != null)
            {
                nameVar.Changed -= OnNameChanged;
                nameVar.Changed += OnNameChanged;
            }

            // Apply FOV changes immediately (the player also re-reads it each frame).
            fov.Changed -= OnFovChanged;
            fov.Changed += OnFovChanged;
        }

        static void OnNameChanged(ConVar v)
        {
            if (CmodPlayer.Local != null && !string.IsNullOrEmpty(v.Value)) CmodPlayer.Local.PlayerName = v.Value;
        }

        static void OnFovChanged(ConVar v)
        {
            var p = CmodPlayer.Local;
            if (p != null && p.Cam != null) p.Cam.fieldOfView = Mathf.Clamp(v.Float, 60f, 120f);
        }
    }
}
