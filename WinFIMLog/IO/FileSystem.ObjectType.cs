namespace WinFIMLog.IO
{
    public static partial class FileSystem
    {
        /// <summary>
        ///     Filesystem object type.
        /// </summary>
        /// <remarks>
        ///     Hardlinks and junctions are ignored as it does not matter for our application.
        /// </remarks>
        public enum ObjectType
        {
            Directory,

            File,

            SymbolicLink,

            Unknown
        }
    }
}