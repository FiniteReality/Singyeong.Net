using System;

namespace Singyeong
{
    /// <summary>
    /// An exception thrown when a Singyeong server sends us an opcode we do
    /// not handle.
    /// </summary>
    public class UnhandledOpcodeException : Exception
    {
        /// <summary>
        /// Gets a value representing the <see cref="SingyeongOpcode"/> which
        /// was unhandled.
        /// </summary>
        public SingyeongOpcode Opcode { get; }

        /// <summary>
        /// Initializes a new instance of
        /// <see cref="UnhandledOpcodeException"/>
        /// </summary>
        /// <param name="opcode">
        /// The <see cref="SingyeongOpcode"/> which was unhandled.
        /// </param>
        public UnhandledOpcodeException(SingyeongOpcode opcode)
            : base(GetMessage(opcode))
        {
            Opcode = opcode;
        }

        private static string GetMessage(SingyeongOpcode opcode)
            => $"Unhandled opcode '{opcode}'";
    }
}