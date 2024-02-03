using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace WFInfo.Components;

public static class KeyNameHelpers
{
    public static string GetKeyName(Key key)
    {
        switch (key)
        {
            case Key.OemTilde:
                return "Tilde";
            case Key.Return:
                return "Enter";
            case Key.Next:
                return "PageDown";
            case >= Key.NumPad0 and <= Key.NumPad9:
                return key.ToString();
            case Key.Decimal:
                return "NumpadDot";
            case Key.Add or Key.Subtract or Key.Multiply or Key.Divide:
                return $"NumPad{key.ToString()[..3]}";
        }

        var temp = GetCharFromKey(key);

        if (temp > ' ')
            return char.ToUpper(temp).ToString();

        return key.ToString();
    }

    private enum MapType : uint
    {
        MAPVK_VK_TO_VSC = 0x0,
        MAPVK_VSC_TO_VK = 0x1,
        MAPVK_VK_TO_CHAR = 0x2,
        MAPVK_VSC_TO_VK_EX = 0x3,
    }

    [DllImport("user32.dll")]
    private static extern int ToUnicode(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 4)]
        StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, MapType uMapType);

    public static char GetCharFromKey(Key key)
    {
        var ch = ' ';

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var keyboardState = new byte[256];
        //  Disabled to avoid Shifted variants   EX: Shift + \ => |
        //  But we don't care about the character, we just want the key
        //  So ignore they current keyboard state
        //GetKeyboardState(keyboardState);

        var scanCode = MapVirtualKey((uint)virtualKey, MapType.MAPVK_VK_TO_VSC);
        var stringBuilder = new StringBuilder(2);

        var result = ToUnicode((uint)virtualKey, scanCode, keyboardState, stringBuilder, stringBuilder.Capacity, 0);
        switch (result)
        {
            case -1:
                break;
            case 0:
                break;
            default:
            {
                ch = stringBuilder[0];
                break;
            }
        }

        return ch;
    }
}