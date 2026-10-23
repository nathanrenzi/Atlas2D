using Atlas2D.Misc.Input;
using Atlas2D.Rendering;
using GLFW;
using System.Numerics;

namespace Atlas2D
{
    public static class Input
    {
        public static float ScrollDelta { get; private set; }
        public static Vector2 MousePosition { get; private set; }
        public static Vector2 MouseDelta { get; private set; }
        public static float JoystickDeadzone { get; private set; }
        public static event Action<int, GamepadConnectionStatus> OnGamepadConnectionStatusChange;

        private static bool[] KEYS;
        private static bool[] KEYS_LAST;
        private static InputState[] MOUSE = new InputState[8];
        private static InputState[] MOUSE_LAST = new InputState[8];
        private static GamePadState[] GAMEPADSTATES = new GamePadState[16];
        private static GamePadState[] GAMEPADSTATES_LAST = new GamePadState[16];
        private static int _totalKeyCount;
        private static KeyCode[] _keyValues;
        private static MouseCallback _scrollCallback;
        private static bool _scrollCallbackFrame = false;
        private static bool _mouseCallbackFrame = false;
        private static MouseCallback _mouseCallback;
        private static CharCallback _charCallback;
        private static JoystickCallback _joystickCallback;
        private static List<IKeyListener> _keyListeners = new List<IKeyListener>();
        private static bool _lockKeyInput = false;
        private static HashSet<(object, uint)> _keyLocks = new();
        private static List<int> _connectedGamepads = new List<int>();
        private static Dictionary<KeyCode, int> _keyIndexCache = new();

        public static void AddListener(IKeyListener listener)
        {
            if(_keyListeners.Contains(listener)) return;
            _keyListeners.Add(listener);
        }

        public static void RemoveListener(IKeyListener listener)
        {
            _keyListeners.Remove(listener);
        }

        /// <summary>
        /// Locks the key input until the same caller unlocks it. While any caller holds a lock,
        /// only <see cref="IKeyListener"/> instances will receive key inputs.
        /// </summary>
        public static bool LockKeyInput(object callerObj, uint priority = 0)
        {
            if (callerObj != null) _keyLocks.Add((callerObj, priority));
            _lockKeyInput = _keyLocks.Count > 0;
            return _lockKeyInput;
        }

        /// <summary>
        /// Removes a lock added with <see cref="LockKeyInput"/>.
        /// </summary>
        public static bool UnlockKeyInput(object callerObj, uint priority = 0)
        {
            if (callerObj != null) _keyLocks.Remove((callerObj, priority));
            _lockKeyInput = _keyLocks.Count > 0;
            return _lockKeyInput;
        }

        public static void Initialize()
        {
            _totalKeyCount = Enum.GetNames<KeyCode>().Length;
            _keyValues = Enum.GetValues<KeyCode>();

            KEYS = new bool[_totalKeyCount];
            KEYS_LAST = new bool[_totalKeyCount];

            for (int i = 0; i < KEYS.Length; i++)
            {
                KEYS[i] = false;
            }

            for (int i = 0; i < MOUSE.Length; i++)
            {
                MOUSE[i] = InputState.Release;
                MOUSE_LAST[i] = InputState.Release;
            }

            for (int i = 0; i < _keyValues.Length; i++)
            {
                _keyIndexCache[_keyValues[i]] = i;
            }

            _scrollCallback = new MouseCallback(ScrollCallback);
            Glfw.SetScrollCallback(Display.Window, _scrollCallback);

            _mouseCallback = new MouseCallback(MouseCallback);
            Glfw.SetCursorPositionCallback(Display.Window, _mouseCallback);

            _charCallback = new CharCallback(CharCallback);
            Glfw.SetCharCallback(Display.Window, _charCallback);

            _joystickCallback = new JoystickCallback(JoystickCallback);
            Glfw.SetJoystickCallback(_joystickCallback);

            for(int i = 0; i < 16; i++)
            {
                if(Glfw.JoystickIsGamepad(i))
                {
                    _connectedGamepads.Add(i);
                    OnGamepadConnectionStatusChange?.Invoke(i, GamepadConnectionStatus.Connected);
                }
            }
        }

        private static void CharCallback(Window window, uint charCode)
        {
            var evt = new KeyEvent
            {
                Type = KeyEventType.Character,
                Character = (char)charCode,
                KeyCode = KeyCode.Unknown,
                IsPressed = true
            };

            NativeCallbackGuard.Run(() =>
            {
                for (int i = 0; i < _keyListeners.Count; i++)
                {
                    _keyListeners[i].OnKeyEvent(evt);
                }
            });
        }

        private static void JoystickCallback(Joystick joystick, ConnectionStatus status)
        {
            GamepadConnectionStatus gamepadStatus = GamepadConnectionStatus.Unknown;
            switch (status)
            { 
                case ConnectionStatus.Connected:
                    gamepadStatus = GamepadConnectionStatus.Connected;
                    _connectedGamepads.Add((int)joystick);
                    break;
                case ConnectionStatus.Disconnected:
                    gamepadStatus = GamepadConnectionStatus.Disconnected;
                    _connectedGamepads.Remove((int)joystick);
                    break;
                case ConnectionStatus.Unknown:
                    gamepadStatus = GamepadConnectionStatus.Unknown;
                    _connectedGamepads.Remove((int)joystick);
                    break;
            }
            GAMEPADSTATES[(int)joystick] = new GamePadState();
            NativeCallbackGuard.Run(() => OnGamepadConnectionStatusChange?.Invoke((int)joystick, gamepadStatus));
        }

        private static void ScrollCallback(Window window, double xOffset, double yOffset)
        {
            ScrollDelta = (float)yOffset / 10;
            _scrollCallbackFrame = true;
        }

        private static void MouseCallback(Window window, double x, double y)
        {
            _mouseCallbackFrame = true;
            Vector2 pos = new Vector2((float)x, (float)y);
            MouseDelta = pos - MousePosition;
            MousePosition = pos;
        }

        public static void ProcessInput()
        {
            // Resetting the LAST tables
            for (int i = 0; i < KEYS_LAST.Length; i++)
            {
                KEYS_LAST[i] = KEYS[i];
            }
            for (int i = 0; i < MOUSE_LAST.Length; i++)
            {
                MOUSE_LAST[i] = MOUSE[i];
            }

            Glfw.PollEvents();
            NativeCallbackGuard.RethrowPending();
            if (!_scrollCallbackFrame)
            {
                // If the scroll delta was not updated this frame, set the delta to 0
                ScrollDelta = 0;
            }
            if(!_mouseCallbackFrame)
            {
                MouseDelta = Vector2.Zero;
            }

            for (int i = 0; i < _connectedGamepads.Count; i++)
            {
                int gamepad = _connectedGamepads[i];
                GAMEPADSTATES_LAST[gamepad] = GAMEPADSTATES[gamepad];
                Glfw.GetGamepadState(gamepad, out GAMEPADSTATES[gamepad]);
            }

            for (int i = 0; i < _totalKeyCount; i++)
            {
                if ((int)_keyValues[i] == -1) continue;
                KEYS[i] = Glfw.GetKey(Display.Window, (Keys)_keyValues[i]) == InputState.Press;

                // Key pressed
                if (KEYS[i] && !KEYS_LAST[i])
                {
                    var evt = new KeyEvent
                    {
                        Type = KeyEventType.Key,
                        KeyCode = _keyValues[i],
                        Character = char.TryParse(_keyValues[i].ToString(), out char c) ? c : null,
                        IsPressed = true
                    };

                    for (int x = 0; x < _keyListeners.Count; x++)
                    {
                        _keyListeners[x].OnKeyEvent(evt);
                    }
                }
                // Key released
                else if (!KEYS[i] && KEYS_LAST[i])
                {
                    var evt = new KeyEvent
                    {
                        Type = KeyEventType.Key,
                        KeyCode = _keyValues[i],
                        Character = char.TryParse(_keyValues[i].ToString(), out char c) ? c : null,
                        IsPressed = false
                    };

                    for (int x = 0; x < _keyListeners.Count; x++)
                    {
                        _keyListeners[x].OnKeyEvent(evt);
                    }
                }
            }

            for (int i = 0; i < MOUSE.Length; i++)
            {
                // Mouse presses
                MOUSE[i] = Glfw.GetMouseButton(Display.Window, (GLFW.MouseButton)i);
            }

            _scrollCallbackFrame = false;
            _mouseCallbackFrame = false;
        }

        private static bool IsSpecialKey(KeyCode key)
        {
            return key >= KeyCode.Escape;
        }

        public static bool GetMouseButtonDown(MouseButton button)
        {
            return MOUSE[(int)button] == InputState.Press && MOUSE_LAST[(int)button] == InputState.Release;
        }

        public static bool GetMouseButtonUp(MouseButton button)
        {
            return MOUSE[(int)button] == InputState.Release && MOUSE_LAST[(int)button] == InputState.Press;
        }

        public static bool GetMouseButton(MouseButton button)
        {
            return MOUSE[(int)button] == InputState.Press;
        }

        public static bool GetKey(KeyCode key)
        {
            int index = GetKeyIndex(key);
            if (index == -1) return false;

            return _lockKeyInput ? false : KEYS[index];
        }

        public static bool GetKeyDown(KeyCode key)
        {
            int index = GetKeyIndex(key);
            if (index == -1) return false;

            return _lockKeyInput ? false : KEYS[index] && !KEYS_LAST[index];
        }

        public static bool GetKeyUp(KeyCode key)
        {
            int index = GetKeyIndex(key);
            if (index == -1) return false;

            return _lockKeyInput ? false : !KEYS[index] && KEYS_LAST[index];
        }

        private static int GetKeyIndex(KeyCode key)
        {
            return _keyIndexCache.TryGetValue(key, out int index) ? index : -1;
        }

        public static void SetJoystickDeadzone(float deadzone)
        {
            JoystickDeadzone = MathF.Max(MathF.Min(deadzone, 1), 0.001f);
        }

        public static bool IsGamepadConnected(int gamepadIndex)
        {
            return _connectedGamepads.Contains(gamepadIndex);
        }

        public static bool GetGamepadButton(int gamepadIndex, GamepadButton button)
        {
            if (_connectedGamepads.Contains(gamepadIndex))
            {
                GamePadState thisFrame = GAMEPADSTATES[gamepadIndex];
                return thisFrame.GetButtonState((GamePadButton)button) == InputState.Press;
            }
            else
            {
                return false;
            }
        }

        public static bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button)
        {
            if (_connectedGamepads.Contains(gamepadIndex))
            {
                GamePadState thisFrame = GAMEPADSTATES[gamepadIndex];
                GamePadState lastFrame = GAMEPADSTATES_LAST[gamepadIndex];
                return thisFrame.GetButtonState((GamePadButton)button) == InputState.Press
                    && lastFrame.GetButtonState((GamePadButton)button) == InputState.Release;
            }
            else
            {
                return false;
            }
        }

        public static bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button)
        {
            if (_connectedGamepads.Contains(gamepadIndex))
            {
                GamePadState thisFrame = GAMEPADSTATES[gamepadIndex];
                GamePadState lastFrame = GAMEPADSTATES_LAST[gamepadIndex];
                return thisFrame.GetButtonState((GamePadButton)button) == InputState.Release
                    && lastFrame.GetButtonState((GamePadButton)button) == InputState.Press;
            }
            else
            {
                return false;
            }
        }

        public static float GetGamepadAxis(int gamepadIndex, GamepadAxis axis)
        {
            if (_connectedGamepads.Contains(gamepadIndex))
            {
                GamePadState thisFrame = GAMEPADSTATES[gamepadIndex];
                float value = thisFrame.GetAxis((GamePadAxis)axis);
                if(axis <= GamepadAxis.RightY)
                {
                    return MathF.Abs(value) <= JoystickDeadzone ? 0 : MathF.Sign(value) * ((MathF.Abs(value) - JoystickDeadzone) / (1 - JoystickDeadzone));
                }
                else
                {
                    return value;
                }
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the screen-space mouse position, with (0, 0) being the top-left of the screen.
        /// </summary>
        /// <returns></returns>
        public static Vector2 GetMouseScreenPosition()
        {
            return MousePosition;
        }

        /// <summary>
        /// Gets the mouse position, offset to match the current camera position and zoom.
        /// </summary>
        /// <returns></returns>
        public static Vector2 GetMouseWorldPosition()
        {
            Vector2 centered = new Vector2(
                MousePosition.X - Display.WindowSize.X * 0.5f,
                (Display.WindowSize.Y * 0.5f) - MousePosition.Y
            );
            centered /= CameraNode.Main.Zoom;

            return centered + CameraNode.Main.WorldPosition;
        }
    }
}
