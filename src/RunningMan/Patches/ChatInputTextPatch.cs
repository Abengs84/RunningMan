using System.Reflection;
using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Route /run chat input through RunningMan RPC instead of normal speech.
    /// </summary>
    [HarmonyPatch(typeof(Chat), "SendInput")]
    public static class ChatSendInputPatch
    {
        private static FieldInfo _inputField;

        private static bool Prefix(Chat __instance)
        {
            var input = GetInputText(__instance);
            if (string.IsNullOrEmpty(input))
            {
                return true;
            }

            if (!input.StartsWith("/run", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var argLine = input.Length > 4 ? input.Substring(4).Trim() : string.Empty;
            ClearInput(__instance);
            ValheimUtil.RunCommand(argLine);
            return false;
        }

        private static string GetInputText(Chat chat)
        {
            _inputField ??= AccessTools.Field(typeof(Chat), "m_input");
            if (_inputField?.GetValue(chat) == null)
            {
                return null;
            }

            var inputField = _inputField.GetValue(chat);
            var textProperty = inputField.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            return textProperty?.GetValue(inputField) as string;
        }

        private static void ClearInput(Chat chat)
        {
            var inputField = _inputField?.GetValue(chat);
            if (inputField == null)
            {
                return;
            }

            var textProperty = inputField.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            textProperty?.SetValue(inputField, string.Empty);
        }
    }
}
