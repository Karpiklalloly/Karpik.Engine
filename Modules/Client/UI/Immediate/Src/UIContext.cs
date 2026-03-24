using System;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Immediate-mode UI context. Each frame, call BeginFrame(), issue UI calls, then EndFrame().
    /// </summary>
    public struct UIContext : IDisposable
    {
        // Frame-scoped pools
        private static readonly List<UIElement> _elementPool = new List<UIElement>();
        private static int _poolIndex = 0;

        // Frame text arena for immediate-mode text (to avoid allocations)
        private const int TextArenaSize = 64 * 1024; // 64KB
        private static char[] _textArena = new char[TextArenaSize];
        private static int _textArenaPos;

        private bool _disposed;

        /// <summary>
        /// Begins a UI frame. Resets the internal element pool and text arena.
        /// </summary>
        public static UIContext BeginFrame()
        {
            _poolIndex = 0;
            _elementPool.Clear();
            _textArenaPos = 0;
            return new UIContext();
        }

        /// <summary>
        /// Ends the UI frame. Should be called after all UI calls for the frame.
        /// </summary>
        public void EndFrame()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // No resources to free currently.
            }
        }

        /// <summary>
        /// Allocates a new UIElement from the pool (or creates a new one if needed).
        /// </summary>
        private UIElement AllocElement()
        {
            if (_poolIndex < _elementPool.Count)
            {
                var elem = _elementPool[_poolIndex];
                _poolIndex++;
                // Reset text fields to default (no text)
                elem.StringIndex = 0;
                elem.TextOffset = 0;
                elem.TextLength = 0;
                return elem;
            }
            else
            {
                var elem = new UIElement();
                _elementPool.Add(elem);
                _poolIndex++;
                return elem;
            }
        }

        /// <summary>
        /// Allocates a string in the frame text arena and returns offset and length.
        /// Returns (-1, 0) if the string does not fit.
        /// </summary>
        private static (int offset, int length) AllocTextInArena(string text)
        {
            if (text == null)
                return (0, 0);

            int len = text.Length;
            if (_textArenaPos + len > TextArenaSize)
            {
                // In a real engine, we might grow the arena or log an error.
                // For now, we'll truncate to fit.
                len = TextArenaSize - _textArenaPos;
                if (len <= 0)
                    return (-1, 0);
            }

            text.CopyTo(0, _textArena, _textArenaPos, len);
            int offset = _textArenaPos;
            _textArenaPos += len;
            return (offset, len);
        }

        #region Immediate-mode API

        public bool Button(string label, ref Karpik.Engine.Client.UI.Rectangle bounds)
        {
            var elem = AllocElement();
            elem.NodeIndex = -1; // immediate, not from markup
            elem.Bounds = bounds;
            elem.Color = 0xFFCCCCCC; // default button color
            var (offset, length) = AllocTextInArena(label);
            elem.SetTextFromArena(offset, length);
            elem.Visible = true;
            elem.Interactive = true;
            return false;
        }

        public void Label(string label, ref Karpik.Engine.Client.UI.Rectangle bounds)
        {
            var elem = AllocElement();
            elem.NodeIndex = -1;
            elem.Bounds = bounds;
            elem.Color = 0xFFFFFFFF; // white text (color ignored for label?)
            var (offset, length) = AllocTextInArena(label);
            elem.SetTextFromArena(offset, length);
            elem.Visible = true;
            elem.Interactive = false;
        }

        public void InputField(ref string buffer, string placeholder, ref Karpik.Engine.Client.UI.Rectangle bounds)
        {
            var elem = AllocElement();
            elem.NodeIndex = -1;
            elem.Bounds = bounds;
            elem.Color = 0xFF777777;
            string textToShow = string.IsNullOrEmpty(buffer) ? placeholder : buffer;
            var (offset, length) = AllocTextInArena(textToShow);
            elem.SetTextFromArena(offset, length);
            elem.Visible = true;
            elem.Interactive = true;
        }

        public void Slider(float value, float min, float max, ref Karpik.Engine.Client.UI.Rectangle bounds)
        {
            var elem = AllocElement();
            elem.NodeIndex = -1;
            elem.Bounds = bounds;
            elem.Color = 0xFF888888;
            string text = $"{value:F2}";
            var (offset, length) = AllocTextInArena(text);
            elem.SetTextFromArena(offset, length);
            elem.Visible = true;
            elem.Interactive = true;
        }

        public void ProgressBar(float value, ref Karpik.Engine.Client.UI.Rectangle bounds)
        {
            var elem = AllocElement();
            elem.NodeIndex = -1;
            elem.Bounds = bounds;
            elem.Color = 0xFF448844;
            string text = $"{(value * 100):F0}%";
            var (offset, length) = AllocTextInArena(text);
            elem.SetTextFromArena(offset, length);
            elem.Visible = true;
            elem.Interactive = false;
        }

        public void BeginWindow(string title, ref Karpik.Engine.Client.UI.Rectangle bounds, out Karpik.Engine.Client.UI.Rectangle contentRect)
        {
            var elem = AllocElement();
            elem.NodeIndex = -1;
            elem.Bounds = bounds;
            elem.Color = 0xFF202020;
            var (offset, length) = AllocTextInArena(title);
            elem.SetTextFromArena(offset, length);
            elem.Visible = true;
            elem.Interactive = false;
            contentRect = new Karpik.Engine.Client.UI.Rectangle(bounds.X + 4, bounds.Y + 20, bounds.Width - 8, bounds.Height - 24);
        }

        public void EndWindow()
        {
            // Nothing to do now.
        }

        #endregion
    }
}