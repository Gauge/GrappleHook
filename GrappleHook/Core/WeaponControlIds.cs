using System;

namespace GrappleHook.Core
{
    /// <summary>
    /// Which vanilla weapon terminal controls the grapple blocks hide, and which ones they
    /// take over. Kept out of the terminal code itself so the lists can be checked without
    /// a running game.
    /// </summary>
    public static class WeaponControlIds
    {
        /// <summary>Shoot toggle. Taken over: it fires the grapple instead of the gun.</summary>
        public const string Shoot = "Shoot";
        public const string ShootOn = "Shoot_On";
        public const string ShootOff = "Shoot_Off";
        public const string ShootOnce = "ShootOnce";

        /// <summary>
        /// Turret targeting and range actions. A grapple has no targeting and no range, so
        /// these are disabled rather than left to do nothing.
        /// </summary>
        public static readonly string[] BannedActions = new string[]
        {
            "IncreaseRange",
            "DecreaseRange",
            "EnableIdleMovement",
            "EnableIdleMovement_On",
            "EnableIdleMovement_Off",
            "TargetMeteors",
            "TargetMeteors_On",
            "TargetMeteors_Off",
            "TargetMissiles",
            "TargetMissiles_On",
            "TargetMissiles_Off",
            "TargetSmallShips",
            "TargetSmallShips_On",
            "TargetSmallShips_Off",
            "TargetLargeShips",
            "TargetLargeShips_On",
            "TargetLargeShips_Off",
            "TargetCharacters",
            "TargetCharacters_On",
            "TargetCharacters_Off",
            "TargetStations",
            "TargetStations_On",
            "TargetStations_Off",
            "TargetNeutrals",
            "TargetNeutrals_On",
            "TargetNeutrals_Off",
            "TargetFriends",
            "TargetFriends_On",
            "TargetFriends_Off",
            "TargetEnemies",
            "TargetEnemies_On",
            "TargetEnemies_Off",
            "EnableTargetLocking",
            "TargetingGroup_Weapons",
            "TargetingGroup_Propulsion",
            "TargetingGroup_PowerSystems",
            "TargetingGroup_CycleSubsystems",
            "FocusLockedTarget",
        };

        public static readonly string[] BannedControls = new string[]
        {
            "Range",
            "EnableIdleMovement",
            "TargetMeteors",
            "TargetMissiles",
            "TargetSmallShips",
            "TargetLargeShips",
            "TargetCharacters",
            "TargetStations",
            "TargetNeutrals",
            "TargetFriends",
            "TargetEnemies",
            "EnableTargetLocking",
            "TargetingGroup_Selector",
            "FocusLockedTarget",
        };

        public static bool IsBannedAction(string id)
        {
            return Array.IndexOf(BannedActions, id) >= 0;
        }

        public static bool IsBannedControl(string id)
        {
            return Array.IndexOf(BannedControls, id) >= 0;
        }
    }
}
