namespace Atlas2D
{
    /// <summary>
    /// An error the game cannot recover from. The message is shown to the player before the game exits.
    /// </summary>
    public class FatalException : Exception
    {
        public FatalException(string message) : base(message) { }
        public FatalException(string message, Exception innerException) : base(message, innerException) { }
    }
}
