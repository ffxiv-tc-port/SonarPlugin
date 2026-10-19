using AG.EnumLocalization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SonarPlugin.Localization
{
    [EnumLocStrings("Config")]
    public enum ConfigLoc
    {
        [EnumLoc("Contribute.Global", Fallback = "Global Contribute")]
        GlobalContribute,

        [EnumLoc("Contribute.GlobalTooltip", Fallback = "Disable this to disable contributing both hunts and fates.\nAccessible via /sonaron and /sonaroff commands.")]
        GlobalContributeTooltip,

        [EnumLoc("Contribute.Hunts", Fallback = "Contribute Hunts")]
        ContributeHunts,

        [EnumLoc("Contribute.Fates", Fallback = "Contribute Fates")]
        ContributeFates,

        [EnumLoc("Contribute.Tooltip", Fallback = "Contributing reports is required in order to receive reports from other Sonar users.\nIf disabled Sonar will continue to work in local mode, where you'll see what's detected within your game but you'll not receive from others.")]
        ContributeTooltip,

        [EnumLoc("Contribute.GlobalDisabled", Fallback = "Global Disabled")]
        ContributeGlobalDisabled,

        [EnumLoc("TrackAll.Hunts", Fallback = "Track All Hunts")]
        TrackAllHunts,

        [EnumLoc("TrackAll.Fates", Fallback = "Track All Fates")]
        TrackAllFates,

        [EnumLoc("TrackAll.Tooltip", Fallback = "Checked: Track all reports regardless of jurisdiction settings.\nUnchecked: Track reports within jurisdiction settings only.")]
        TrackAllTooltip,

        [EnumLoc(Fallback = "Receive Jurisdiction")]
        ReceiveJurisdiction,

        [EnumLoc("Server.Connect", Fallback = "Connect to Sonar server (receive and share hunt / FATE reports)")]
        ConnectToServer,

        [EnumLoc("Server.ConnectTooltip", Fallback = "On: connects to the Sonar server and receives reports from other Sonar users. While connected Sonar sends:\n- your character name, home world and a hash of your character ID\n- the world, zone and instance you are in, and your position\n- a hashed hardware identifier and the client ID the server assigned to this install\n- version info (Sonar, plugin, game, Dalamud, operating system) and your contribute settings\n- hunts and FATEs you detect (position, HP, nearby player count), only while contributing is on\nThe support window only sends what you type when you submit it yourself.\nOff: nothing is sent; you only see the hunts and FATEs your own game detects.")]
        ConnectToServerTooltip,

        [EnumLoc("Server.DisabledStatus", Fallback = "Not connected (turned off in settings)")]
        ServerDisabledStatus,

    }
}
