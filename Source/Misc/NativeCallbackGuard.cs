using System.Runtime.ExceptionServices;

namespace Atlas2D
{
    /// <summary>
    /// Exceptions can't safely unwind through native code, so callbacks from GLFW store them here
    /// and they're rethrown once control is back in managed code.
    /// </summary>
    internal static class NativeCallbackGuard
    {
        private static ExceptionDispatchInfo _pending;

        public static void Run(Action action)
        {
            if (_pending != null) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                _pending = ExceptionDispatchInfo.Capture(e);
            }
        }

        public static void RethrowPending()
        {
            ExceptionDispatchInfo pending = _pending;
            if (pending == null) return;
            _pending = null;
            pending.Throw();
        }
    }
}
