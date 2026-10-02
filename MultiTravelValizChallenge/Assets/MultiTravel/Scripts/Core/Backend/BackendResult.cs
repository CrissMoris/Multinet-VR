using System;

namespace MultiTravel.Core.Backend
{
    /// <summary>Outcome of a backend call: either <see cref="Value"/> (when <see cref="Ok"/>) or <see cref="Error"/>.</summary>
    public sealed class BackendResult<T>
    {
        private BackendResult(bool ok, T value, BackendError error)
        {
            Ok = ok;
            Value = value;
            Error = error;
        }

        public bool Ok { get; }

        /// <summary>Only meaningful when <see cref="Ok"/>.</summary>
        public T Value { get; }

        /// <summary>Only set when not <see cref="Ok"/>.</summary>
        public BackendError Error { get; }

        public static BackendResult<T> Success(T value)
        {
            return new BackendResult<T>(true, value, null);
        }

        public static BackendResult<T> Failure(BackendError error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new BackendResult<T>(false, default, error);
        }

        public override string ToString()
        {
            return Ok ? $"Ok: {Value}" : $"Error: {Error}";
        }
    }
}
