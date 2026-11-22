using System;
using System.Collections.Generic;
using System.Linq;
using GrappleHook.Core;

namespace GrappleHook.Tests
{
    public class WeaponControlIdsTests
    {
        [Fact]
        public void TheShootControlsAreNeverBanned()
        {
            // the grapple takes these over rather than hiding them, so banning one would
            // silently disable the only way to fire the hook from the terminal
            foreach (string id in new[] { WeaponControlIds.Shoot, WeaponControlIds.ShootOn, WeaponControlIds.ShootOff, WeaponControlIds.ShootOnce })
            {
                Assert.False(WeaponControlIds.IsBannedAction(id), $"{id} must stay enabled");
                Assert.False(WeaponControlIds.IsBannedControl(id), $"{id} must stay visible");
            }
        }

        [Fact]
        public void BannedListsHaveNoDuplicates()
        {
            Assert.Equal(WeaponControlIds.BannedActions.Length, WeaponControlIds.BannedActions.Distinct().Count());
            Assert.Equal(WeaponControlIds.BannedControls.Length, WeaponControlIds.BannedControls.Distinct().Count());
        }

        [Fact]
        public void EveryHiddenControlHasItsActionsBannedToo()
        {
            // hiding a control but leaving its action runnable puts the block into a state
            // the terminal cannot show or undo
            List<string> exempt = new List<string> { "Range", "TargetingGroup_Selector" };

            foreach (string id in WeaponControlIds.BannedControls)
            {
                if (exempt.Contains(id)) continue;

                Assert.True(WeaponControlIds.IsBannedAction(id), $"control {id} is hidden but its action is not banned");
            }
        }

        [Fact]
        public void ToggleActionsAreBannedInAllThreeForms()
        {
            foreach (string id in WeaponControlIds.BannedActions.ToArray())
            {
                if (id.EndsWith("_On") || id.EndsWith("_Off")) continue;
                if (!WeaponControlIds.IsBannedAction(id + "_On") && !WeaponControlIds.IsBannedAction(id + "_Off")) continue;

                Assert.True(WeaponControlIds.IsBannedAction(id + "_On"), $"{id}_On is not banned");
                Assert.True(WeaponControlIds.IsBannedAction(id + "_Off"), $"{id}_Off is not banned");
            }
        }

        [Fact]
        public void RangeControlsAreBanned()
        {
            // the grapple sets Range to 0 and drives its own distance
            Assert.True(WeaponControlIds.IsBannedControl("Range"));
            Assert.True(WeaponControlIds.IsBannedAction("IncreaseRange"));
            Assert.True(WeaponControlIds.IsBannedAction("DecreaseRange"));
        }

        [Fact]
        public void UnknownIdsAreLeftAlone()
        {
            Assert.False(WeaponControlIds.IsBannedAction("OnOff"));
            Assert.False(WeaponControlIds.IsBannedAction("ShowInTerminal"));
            Assert.False(WeaponControlIds.IsBannedControl("CustomData"));
        }
    }
}
