using System;

namespace TelegramAuth.Services
{
    public enum TelegramAuthBindFailureKind
    {
        UserNotFound,
        UserDisabled
    }

    public sealed class TelegramAuthBindException : Exception
    {
        public TelegramAuthBindFailureKind FailureKind { get; }

        public TelegramAuthBindException(TelegramAuthBindFailureKind failureKind, string message)
            : base(message)
        {
            FailureKind = failureKind;
        }
    }
}
