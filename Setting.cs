using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;

namespace SyncLights
{
    [FileLocation(nameof(SyncLights))]
    [SettingsUIGroupOrder(kKeybindingGroup)]
    [SettingsUIShowGroupName(kKeybindingGroup)]
    [SettingsUIKeyboardAction(Mod.kToggleToolActionName, ActionType.Button, usages: new string[] { Usages.kMenuUsage }, interactions: new string[] { "UIButton" })]
    [SettingsUIKeyboardAction(Mod.kClearActionName, ActionType.Button, usages: new string[] { Usages.kMenuUsage }, interactions: new string[] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kKeybindingGroup = "KeyBinding";

        public Setting(IMod mod) : base(mod)
        {

        }

        [SettingsUIKeyboardBinding(BindingKeyboard.T, Mod.kToggleToolActionName)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding ToggleToolBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Y, Mod.kClearActionName)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding ClearSelectionBinding { get; set; }

        public override void SetDefaults()
        {
            // Set default key bindings
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "SyncLights" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kKeybindingGroup), "Key bindings" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding)), "Toggle Sync Tool" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding)), "Press to activate/deactivate the traffic light synchronization tool" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ClearSelectionBinding)), "Clear Selection" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ClearSelectionBinding)), "Clear all selected intersections" },

                { m_Setting.GetBindingKeyLocaleID(Mod.kToggleToolActionName), "Toggle key" },
                { m_Setting.GetBindingKeyLocaleID(Mod.kClearActionName), "Clear key" },

                { m_Setting.GetBindingMapLocaleID(), "Traffic Light Sync Settings" },
            };
        }

        public void Unload()
        {

        }
    }
}