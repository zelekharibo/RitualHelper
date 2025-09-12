using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RitualHelper.Utils
{
    internal static class Keyboard
    {
        // keyboard input constants
        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;
        
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public static async Task KeyDown(Keys key)
        {
            keybd_event((byte)key, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            await Task.Delay(10);
        }

        public static async Task KeyUp(Keys key)
        {
            keybd_event((byte)key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(10);
        }

        public static async Task PressKey(Keys key, int holdDuration = 50)
        {
            await KeyDown(key);
            await Task.Delay(holdDuration);
            await KeyUp(key);
        }
    }
}
