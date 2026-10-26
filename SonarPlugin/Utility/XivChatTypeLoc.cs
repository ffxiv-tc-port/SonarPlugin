using Dalamud.Game.Text;
using SonarPlugin.Config;
using System.Collections.Generic;

namespace SonarPlugin.Utility
{
    /// <summary>Display-only zh-TW names for the chat channel combo; the English name stays the lookup key (see <see cref="XivChatTypeUtils"/>).</summary>
    /// <remarks>Names come from the game's LogFilter sheet where it has an entry; the rest are descriptive.</remarks>
    public static class XivChatTypeLoc
    {
        private static readonly Dictionary<XivChatType, string> s_tcNames = new()
        {
            { XivChatType.None, "無" },
            { XivChatType.Debug, "偵錯" },
            { XivChatType.Urgent, "緊急" },
            { XivChatType.Notice, "通知" },
            { XivChatType.Say, "說話" },
            { XivChatType.Shout, "喊話" },
            { XivChatType.TellOutgoing, "悄悄話（發送）" },
            { XivChatType.TellIncoming, "悄悄話（接收）" },
            { XivChatType.Party, "小隊" },
            { XivChatType.CrossParty, "小隊（跨界）" },
            { XivChatType.Alliance, "團隊" },
            { XivChatType.Ls1, "通訊貝1" },
            { XivChatType.Ls2, "通訊貝2" },
            { XivChatType.Ls3, "通訊貝3" },
            { XivChatType.Ls4, "通訊貝4" },
            { XivChatType.Ls5, "通訊貝5" },
            { XivChatType.Ls6, "通訊貝6" },
            { XivChatType.Ls7, "通訊貝7" },
            { XivChatType.Ls8, "通訊貝8" },
            { XivChatType.FreeCompany, "公會" },
            { XivChatType.NoviceNetwork, "新人頻道" },
            { XivChatType.CustomEmote, "自訂情感動作" },
            { XivChatType.StandardEmote, "情感動作" },
            { XivChatType.Yell, "呼喊" },
            { XivChatType.PvPTeam, "戰隊" },
            { XivChatType.CrossLinkShell1, "跨界通訊貝1" },
            { XivChatType.CrossLinkShell2, "跨界通訊貝2" },
            { XivChatType.CrossLinkShell3, "跨界通訊貝3" },
            { XivChatType.CrossLinkShell4, "跨界通訊貝4" },
            { XivChatType.CrossLinkShell5, "跨界通訊貝5" },
            { XivChatType.CrossLinkShell6, "跨界通訊貝6" },
            { XivChatType.CrossLinkShell7, "跨界通訊貝7" },
            { XivChatType.CrossLinkShell8, "跨界通訊貝8" },
            { XivChatType.Echo, "默語" },
            { XivChatType.SystemMessage, "系統資訊" },
            { XivChatType.SystemError, "戰鬥系統資訊" },
            { XivChatType.GatheringSystemMessage, "採集系統資訊" },
            { XivChatType.ErrorMessage, "錯誤資訊" },
            { XivChatType.NPCDialogue, "NPC角色對話" },
            { XivChatType.NPCDialogueAnnouncements, "NPC角色通告" },
            { XivChatType.RetainerSale, "市場出售資訊" },
        };

        /// <summary>Returns the display label for <paramref name="englishName"/> (a <c>FancyName</c> or enum name); the English name itself when not Traditional Chinese or unmapped.</summary>
        public static string GetDisplayName(string englishName, LocalizationPreset preset)
        {
            if (preset != LocalizationPreset.ChineseTraditional) return englishName;
            var type = XivChatTypeUtils.GetValueFromInfoAttribute(englishName);
            return s_tcNames.TryGetValue(type, out var name) ? name : englishName;
        }
    }
}
